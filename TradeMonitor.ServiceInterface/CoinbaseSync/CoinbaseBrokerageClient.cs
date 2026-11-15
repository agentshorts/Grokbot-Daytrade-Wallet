using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TradeMonitor.ServiceInterface;

public sealed class CoinbaseDownException(string message) : Exception(message);

public sealed class CoinbaseRejectedException(int status, string body) : Exception("HTTP " + status + ": " + body)
{
    public int Status { get; } = status;
}

public sealed class CoinbasePermissionException(string message) : Exception(message);

public static class CoinbaseReadPaths
{
    public static bool IsAllowed(string path)
    {
        if (path is "/api/v3/brokerage/key_permissions"
            or "/api/v3/brokerage/accounts"
            or "/api/v3/brokerage/portfolios"
            or "/api/v3/brokerage/orders/historical/fills")
            return true;
        return OneSegment(path, "/api/v3/brokerage/portfolios/")
            || OneSegment(path, "/api/v3/brokerage/products/")
            || OneSegment(path, "/api/v3/brokerage/market/products/");
    }

    static bool OneSegment(string path, string prefix)
    {
        if (!path.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        var rest = path[prefix.Length..];
        return rest.Length > 0 && !rest.Contains('/') && !rest.Contains("..", StringComparison.Ordinal);
    }
}

public sealed class CoinbaseGetOnlyHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
            throw new InvalidOperationException("Coinbase sync only sends GET requests.");
        var path = request.RequestUri?.IsAbsoluteUri == true
            ? request.RequestUri.AbsolutePath
            : request.RequestUri?.OriginalString ?? "";
        var query = path.IndexOf('?', StringComparison.Ordinal);
        if (query >= 0)
            path = path[..query];
        if (!CoinbaseReadPaths.IsAllowed(path))
            throw new InvalidOperationException("Coinbase sync refused a path that is not a read.");
        return base.SendAsync(request, cancellationToken);
    }
}

public interface ICoinbaseReadApi
{
    Task<IReadOnlyList<CbAccount>> AccountsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<CbPortfolio>> PortfoliosAsync(CancellationToken cancellationToken);
    Task<CbBreakdown> BreakdownAsync(string portfolioId, CancellationToken cancellationToken);
    Task<IReadOnlyList<CbFill>> FillsAsync(string? startIso, CancellationToken cancellationToken);
    Task<CbProduct?> ProductAsync(string productId, CancellationToken cancellationToken);
    Task<decimal?> PublicPriceAsync(string productId, CancellationToken cancellationToken);
    Task<CbPermissions> PermissionsAsync(CancellationToken cancellationToken);
}

public sealed record CbAccount(string Currency, decimal Available);

public sealed record CbPortfolio(string Uuid);

public sealed record CbProduct(
    string? BaseIncrement,
    string? BaseCurrencyId,
    string? ProductType = null,
    string? ProductVenue = null,
    string? Venue = null,
    bool PredictionDetails = false);

public readonly record struct CbPermissions(bool CanTrade, bool CanTransfer);

public sealed record CbFill(
    string TradeId,
    string? OrderId,
    string TradeTime,
    DateTime TradeTimeUtc,
    string TradeType,
    decimal Price,
    decimal Size,
    decimal Commission,
    string ProductId,
    bool SizeInQuote,
    string Side,
    string? ProductType,
    string? EquityTicker,
    string? EquityCbrn);

public sealed record CbSpot(string Asset, bool IsCash, string? AccountType, decimal Qty, decimal Fiat);

public sealed record CbEquity(string Cbrn, decimal Qty, decimal Fiat);

public sealed record CbPrediction(string? ProductId, decimal Qty, decimal Fiat, IReadOnlyList<string> FieldNames);

public sealed class CbBreakdown
{
    public List<CbSpot> Spot { get; init; } = [];
    public List<CbEquity> Equity { get; init; } = [];
    public List<CbPrediction> Prediction { get; init; } = [];
}

public sealed partial class CoinbaseBrokerageClient : ICoinbaseReadApi, IDisposable
{
    readonly HttpClient http;
    readonly CoinbaseSigner signer;
    readonly int retries;
    readonly Func<TimeSpan, CancellationToken, Task> delay;
    bool disposed;

    public CoinbaseBrokerageClient(
        CoinbaseSigner signer,
        HttpMessageHandler? handler = null,
        int retries = 4,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        this.signer = signer;
        this.retries = retries < 1 ? 1 : retries;
        this.delay = delay ?? ((span, token) => Task.Delay(span, token));
        var ownsHandler = handler == null;
        handler ??= new SocketsHttpHandler();
        var guard = new CoinbaseGetOnlyHandler(handler);
        http = new HttpClient(guard, disposeHandler: ownsHandler)
        {
            BaseAddress = new Uri("https://api.coinbase.com"),
            Timeout = TimeSpan.FromSeconds(15),
        };
    }

    public async Task<IReadOnlyList<CbAccount>> AccountsAsync(CancellationToken cancellationToken)
    {
        var all = new List<CbAccount>();
        string? cursor = null;
        for (var page = 0; page < 100; page++)
        {
            var query = new Dictionary<string, string?> { ["limit"] = "250" };
            if (!string.IsNullOrEmpty(cursor))
                query["cursor"] = cursor;
            using var doc = await GetAsync("/api/v3/brokerage/accounts", query, true, cancellationToken);
            if (doc.RootElement.TryGetProperty("accounts", out var accounts) && accounts.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in accounts.EnumerateArray())
                {
                    all.Add(new CbAccount(
                        CoinbaseJson.Text(item, "currency") ?? "",
                        CoinbaseJson.Num(item, "available_balance")));
                }
            }
            var hasNext = doc.RootElement.TryGetProperty("has_next", out var flag) && flag.ValueKind == JsonValueKind.True;
            cursor = CoinbaseJson.Text(doc.RootElement, "cursor");
            if (!hasNext || string.IsNullOrEmpty(cursor))
                return all;
        }
        return all;
    }

    public async Task<IReadOnlyList<CbPortfolio>> PortfoliosAsync(CancellationToken cancellationToken)
    {
        using var doc = await GetAsync("/api/v3/brokerage/portfolios", null, true, cancellationToken);
        var list = new List<CbPortfolio>();
        if (!doc.RootElement.TryGetProperty("portfolios", out var items) || items.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var item in items.EnumerateArray())
        {
            var id = CoinbaseJson.Text(item, "uuid");
            if (!string.IsNullOrEmpty(id))
                list.Add(new CbPortfolio(id));
        }
        return list;
    }

    public async Task<CbBreakdown> BreakdownAsync(string portfolioId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(portfolioId) || portfolioId.Contains('/') || portfolioId.Contains("..", StringComparison.Ordinal))
            throw new InvalidOperationException("Coinbase sync refused a portfolio id.");
        using var doc = await GetAsync("/api/v3/brokerage/portfolios/" + portfolioId, null, true, cancellationToken);
        var root = doc.RootElement.TryGetProperty("breakdown", out var breakdown) ? breakdown : doc.RootElement;
        return new CbBreakdown
        {
            Spot = ParseSpot(root),
            Equity = ParseEquity(root),
            Prediction = ParsePrediction(root),
        };
    }

    public async Task<IReadOnlyList<CbFill>> FillsAsync(string? startIso, CancellationToken cancellationToken)
    {
        var all = new List<CbFill>();
        string? cursor = null;
        for (var page = 0; page < 500; page++)
        {
            var query = new Dictionary<string, string?> { ["limit"] = "100" };
            if (!string.IsNullOrEmpty(startIso))
                query["start_sequence_timestamp"] = startIso;
            if (!string.IsNullOrEmpty(cursor))
                query["cursor"] = cursor;
            using var doc = await GetAsync("/api/v3/brokerage/orders/historical/fills", query, true, cancellationToken);
            var pageRows = ParseFills(doc.RootElement);
            all.AddRange(pageRows);
            cursor = CoinbaseJson.Text(doc.RootElement, "cursor");
            if (string.IsNullOrEmpty(cursor) || pageRows.Count == 0)
                return all;
        }
        return all;
    }

    public async Task<CbProduct?> ProductAsync(string productId, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = await GetAsync("/api/v3/brokerage/products/" + Segment(productId), null, true, cancellationToken);
            var body = doc.RootElement;
            if (body.TryGetProperty("product", out var nested)
                && nested.ValueKind == JsonValueKind.Object
                && !body.TryGetProperty("base_increment", out _))
                body = nested;
            var details = HasPredictionDetails(body);
            return new CbProduct(
                CoinbaseJson.Text(body, "base_increment"),
                CoinbaseJson.Text(body, "base_currency_id"),
                CoinbaseJson.Text(body, "product_type"),
                CoinbaseJson.Text(body, "product_venue"),
                CoinbaseJson.Text(body, "venue"),
                details);
        }
        catch (CoinbaseRejectedException)
        {
            return null;
        }
    }

    public async Task<decimal?> PublicPriceAsync(string productId, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = await GetAsync("/api/v3/brokerage/market/products/" + Segment(productId), null, false, cancellationToken);
            if (!doc.RootElement.TryGetProperty("price", out var price))
                return null;
            var value = CoinbaseJson.ReadDecimal(price);
            return value > 0 ? value : null;
        }
        catch (CoinbaseRejectedException)
        {
            return null;
        }
    }

    public async Task<CbPermissions> PermissionsAsync(CancellationToken cancellationToken)
    {
        using var doc = await GetAsync("/api/v3/brokerage/key_permissions", null, true, cancellationToken);
        return new CbPermissions(
            CoinbaseJson.Flag(doc.RootElement, "can_trade"),
            CoinbaseJson.Flag(doc.RootElement, "can_transfer"));
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        http.Dispose();
        signer.Dispose();
    }

    async Task<JsonDocument> GetAsync(
        string path,
        IReadOnlyDictionary<string, string?>? query,
        bool auth,
        CancellationToken cancellationToken)
    {
        if (!CoinbaseReadPaths.IsAllowed(path))
            throw new InvalidOperationException("Coinbase sync refused a path that is not a read.");
        var url = AppendQuery(path, query);
        string? last = null;
        var wait = TimeSpan.FromSeconds(2);
        for (var attempt = 0; attempt < retries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                if (auth)
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", signer.Create("GET", path));
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                var code = (int)response.StatusCode;
                if (code < 400)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    try
                    {
                        return JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                    }
                    catch (JsonException)
                    {
                        throw new CoinbaseRejectedException(code, "invalid JSON");
                    }
                }
                if (code == 429 || code >= 500)
                    last = "HTTP " + code;
                else
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    throw new CoinbaseRejectedException(code, Scrub(body));
                }
            }
            catch (CoinbaseRejectedException)
            {
                throw;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                last = "timeout";
            }
            catch (HttpRequestException)
            {
                last = "HttpRequestException";
            }

            if (attempt < retries - 1)
            {
                await delay(wait, cancellationToken);
                wait = TimeSpan.FromSeconds(wait.TotalSeconds * 2);
            }
        }
        throw new CoinbaseDownException(path + ": " + last);
    }

    static string AppendQuery(string path, IReadOnlyDictionary<string, string?>? query)
    {
        if (query == null || query.Count == 0)
            return path;
        var builder = new StringBuilder(path);
        var first = true;
        foreach (var pair in query)
        {
            if (string.IsNullOrEmpty(pair.Value))
                continue;
            builder.Append(first ? '?' : '&');
            first = false;
            builder.Append(Uri.EscapeDataString(pair.Key));
            builder.Append('=');
            builder.Append(Uri.EscapeDataString(pair.Value));
        }
        return builder.ToString();
    }

    static bool HasPredictionDetails(JsonElement body)
    {
        foreach (var name in new[] { "prediction_market_product_details", "prediction_market_details", "prediction_details" })
        {
            if (body.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                return true;
        }
        return false;
    }

    static string Segment(string productId)
    {
        if (string.IsNullOrWhiteSpace(productId) || productId.Contains('/') || productId.Contains("..", StringComparison.Ordinal))
            throw new InvalidOperationException("Coinbase sync refused a product id.");
        return productId;
    }

    static string Scrub(string? body)
    {
        if (string.IsNullOrEmpty(body))
            return "";
        var cut = body.Length > 200 ? body[..200] : body;
        return JwtPattern().Replace(cut, "[redacted]");
    }

    static List<CbFill> ParseFills(JsonElement root)
    {
        var list = new List<CbFill>();
        if (!root.TryGetProperty("fills", out var fills) || fills.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var item in fills.EnumerateArray())
        {
            string? ticker = null;
            string? cbrn = null;
            if (item.TryGetProperty("product_details", out var details)
                && details.ValueKind == JsonValueKind.Object
                && details.TryGetProperty("equity_details", out var equity)
                && equity.ValueKind == JsonValueKind.Object)
            {
                ticker = CoinbaseJson.Text(equity, "ticker");
                cbrn = CoinbaseJson.Text(equity, "base_cbrn");
            }
            var time = CoinbaseJson.Text(item, "trade_time") ?? "";
            list.Add(new CbFill(
                CoinbaseJson.Text(item, "trade_id") ?? "",
                CoinbaseJson.Text(item, "order_id"),
                time,
                CoinbaseJson.ParseTime(time),
                CoinbaseJson.Text(item, "trade_type") ?? "",
                CoinbaseJson.Num(item, "price"),
                CoinbaseJson.Num(item, "size"),
                CoinbaseJson.Num(item, "commission"),
                CoinbaseJson.Text(item, "product_id") ?? "",
                CoinbaseJson.Bool(item, "size_in_quote"),
                CoinbaseJson.Text(item, "side") ?? "",
                CoinbaseJson.Text(item, "product_type"),
                ticker,
                cbrn));
        }
        return list;
    }

    static List<CbSpot> ParseSpot(JsonElement root)
    {
        var list = new List<CbSpot>();
        if (!root.TryGetProperty("spot_positions", out var items) || items.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var item in items.EnumerateArray())
        {
            list.Add(new CbSpot(
                CoinbaseJson.Text(item, "asset") ?? "",
                CoinbaseJson.Bool(item, "is_cash"),
                CoinbaseJson.Text(item, "account_type"),
                CoinbaseJson.Num(item, "total_balance_crypto"),
                CoinbaseJson.Num(item, "total_balance_fiat")));
        }
        return list;
    }

    static List<CbEquity> ParseEquity(JsonElement root)
    {
        var list = new List<CbEquity>();
        if (!root.TryGetProperty("equity_positions", out var items) || items.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var item in items.EnumerateArray())
        {
            list.Add(new CbEquity(
                CoinbaseJson.Text(item, "cbrn") ?? "",
                CoinbaseJson.Num(item, "total_balance_equity"),
                CoinbaseJson.Num(item, "total_balance_fiat")));
        }
        return list;
    }

    static List<CbPrediction> ParsePrediction(JsonElement root)
    {
        var list = new List<CbPrediction>();
        if (!root.TryGetProperty("prediction_markets_positions", out var items) || items.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var item in items.EnumerateArray())
        {
            var names = item.ValueKind == JsonValueKind.Object
                ? item.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToList()
                : [];
            list.Add(new CbPrediction(
                CoinbaseJson.Text(item, "product_id") ?? CoinbaseJson.Text(item, "symbol") ?? CoinbaseJson.Text(item, "ticker"),
                CoinbaseJson.FirstNum(item, "total_balance_contracts", "quantity", "size", "net_size"),
                CoinbaseJson.FirstNum(item, "total_balance_fiat", "market_value"),
                names));
        }
        return list;
    }

    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]{10,}", RegexOptions.CultureInvariant)]
    private static partial Regex JwtPattern();
}

static class CoinbaseJson
{
    public static string? Text(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    public static decimal Num(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) ? ReadDecimal(value) : 0;

    public static decimal FirstNum(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (!item.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) && number == 0)
                continue;
            if (value.ValueKind == JsonValueKind.String && value.GetString() is "0" or "0.0" or "")
                continue;
            return ReadDecimal(value);
        }
        return 0;
    }

    public static bool Bool(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value))
            return false;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => string.Equals(value.GetString(), "true", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    public static bool Flag(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    public static decimal ReadDecimal(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                return value.TryGetProperty("value", out var inner) ? ReadDecimal(inner) : 0;
            case JsonValueKind.String:
                return decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : 0;
            case JsonValueKind.Number:
                return value.TryGetDecimal(out var number) ? number : 0;
            default:
                return 0;
        }
    }

    public static DateTime ParseTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return default;
        var value = text.Trim();
        if (value.EndsWith('Z'))
            value = value[..^1] + "+00:00";
        var dot = value.IndexOf('.');
        if (dot >= 0)
        {
            var end = dot + 1;
            while (end < value.Length && char.IsDigit(value[end]))
                end++;
            var digits = value[(dot + 1)..end];
            if (digits.Length > 7)
                digits = digits[..7];
            value = value[..(dot + 1)] + digits + value[end..];
        }
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            return default;
        return parsed.UtcDateTime;
    }
}
