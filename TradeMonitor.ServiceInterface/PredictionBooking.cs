using TradeMonitor.ServiceModel;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.ServiceInterface;

public enum PredictionBookingMode
{
    Off,
    DryRun,
    On,
}

public readonly record struct PredictionTradeMark(string ClientId, string? OrderRef, TradeKind Kind);

// In memory. The sync loop sees open contracts again each cycle, so a restart refills this list.
public interface IPredictionBookings
{
    PredictionBookingMode Mode { get; set; }

    void Seen(string? productId, DateTime seenUtc);

    void Booked(string? productId, DateTime seenUtc, string? walletKey);

    void WouldBook(string? productId, DateTime seenUtc, string detail);

    void Problem(string? productId, DateTime seenUtc, string detail);

    IReadOnlyList<PredictionSighting> Snapshot();
}

public sealed class PredictionSighting
{
    public required string ProductId { get; init; }

    public DateTime FirstSeenUtc { get; set; }

    public DateTime LastSeenUtc { get; set; }

    public string Status { get; set; } = "seen";

    public string? Detail { get; set; }

    public string? Wallet { get; set; }
}

public sealed class PredictionBookingStore : IPredictionBookings
{
    readonly object gate = new();
    readonly Dictionary<string, PredictionSighting> rows = new(StringComparer.OrdinalIgnoreCase);

    public PredictionBookingMode Mode { get; set; } = PredictionBookingMode.DryRun;

    public void Seen(string? productId, DateTime seenUtc) => Touch(productId, seenUtc, "seen", null, null);

    public void Booked(string? productId, DateTime seenUtc, string? walletKey) =>
        Touch(productId, seenUtc, "booked", null, walletKey);

    public void WouldBook(string? productId, DateTime seenUtc, string detail) =>
        Touch(productId, seenUtc, "would-book", detail, null);

    public void Problem(string? productId, DateTime seenUtc, string detail) =>
        Touch(productId, seenUtc, "problem", detail, null);

    public IReadOnlyList<PredictionSighting> Snapshot()
    {
        lock (gate)
        {
            return rows.Values.Select(Copy).ToList();
        }
    }

    void Touch(string? productId, DateTime seenUtc, string status, string? detail, string? wallet)
    {
        var id = productId?.Trim();
        if (string.IsNullOrEmpty(id))
            return;
        var at = AsUtc(seenUtc);
        lock (gate)
        {
            if (!rows.TryGetValue(id, out var row))
            {
                rows[id] = new PredictionSighting
                {
                    ProductId = id,
                    FirstSeenUtc = at,
                    LastSeenUtc = at,
                    Status = status,
                    Detail = status is "problem" or "would-book" ? detail : null,
                    Wallet = wallet,
                };
                return;
            }

            if (at < row.FirstSeenUtc)
                row.FirstSeenUtc = at;
            if (at > row.LastSeenUtc)
                row.LastSeenUtc = at;
            if (status == "seen")
                return;
            if (status == "booked")
            {
                if (row.Status is "would-book" or "problem")
                {
                    if (!string.IsNullOrEmpty(wallet))
                        row.Wallet = wallet;
                    return;
                }

                row.Status = "booked";
                row.Detail = null;
                if (!string.IsNullOrEmpty(wallet))
                    row.Wallet = wallet;
                return;
            }

            if (status == "would-book" && row.Status == "would-book")
            {
                if (!string.IsNullOrEmpty(detail) && (row.Detail == null || !row.Detail.Contains(detail, StringComparison.Ordinal)))
                    row.Detail = string.IsNullOrEmpty(row.Detail) ? detail : row.Detail + " | " + detail;
                return;
            }

            row.Status = status;
            row.Detail = detail;
        }
    }

    static PredictionSighting Copy(PredictionSighting row) => new()
    {
        ProductId = row.ProductId,
        FirstSeenUtc = row.FirstSeenUtc,
        LastSeenUtc = row.LastSeenUtc,
        Status = row.Status,
        Detail = row.Detail,
        Wallet = row.Wallet,
    };

    static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}

public static class PredictionBooking
{
    public const string FillPrefix = "cbpm-fill-";
    public const string SettlePrefix = "cbpm-settle-";
    public const string WalletNote = "Coinbase prediction contract booked by WalletSync.";

    public static string? ClientIdFor(string? tradeId, TradeKind kind = TradeKind.Fill)
    {
        if (string.IsNullOrWhiteSpace(tradeId))
            return null;
        var prefix = kind == TradeKind.Settlement ? SettlePrefix : FillPrefix;
        var id = prefix + tradeId.Trim();
        return id.Length <= 128 ? id : null;
    }

    public static PredictionBookingMode ParseMode(string? value)
    {
        if (string.Equals(value, "On", StringComparison.OrdinalIgnoreCase))
            return PredictionBookingMode.On;
        if (string.Equals(value, "Off", StringComparison.OrdinalIgnoreCase))
            return PredictionBookingMode.Off;
        return PredictionBookingMode.DryRun;
    }

    // A NO order is a short on the YES book, so Coinbase reports the opposite side.
    public static bool? LedgerIsBuy(string? reportedSide, string contractSide)
    {
        if (string.IsNullOrWhiteSpace(reportedSide))
            return null;
        bool reportedBuy;
        if (reportedSide.Equals("BUY", StringComparison.OrdinalIgnoreCase))
            reportedBuy = true;
        else if (reportedSide.Equals("SELL", StringComparison.OrdinalIgnoreCase))
            reportedBuy = false;
        else
            return null;
        return contractSide == "NO" ? !reportedBuy : reportedBuy;
    }

    public static bool IsAlreadyBooked(PredictionTradeMark row, TradeKind kind, string? tradeId, string? orderId, string productId)
    {
        if (kind == TradeKind.Settlement)
            return row.Kind == TradeKind.Settlement || SameClient(row.ClientId, SettlePrefix, tradeId, orderId, productId);
        if (row.Kind == TradeKind.Settlement)
            return false;
        return SameOrder(row.OrderRef, orderId) || SameClient(row.ClientId, FillPrefix, tradeId, orderId, null);
    }

    public static string Proposal(string clientId, string contractSide, string side, decimal qty, decimal price, decimal fee, string? orderId)
    {
        var order = string.IsNullOrWhiteSpace(orderId) ? "(none)" : orderId.Trim();
        return clientId + " " + contractSide + " " + side + " " + CoinbaseWallets.PythonG(qty)
            + " @ " + CoinbaseWallets.PythonG(price)
            + " fee " + CoinbaseWallets.PythonG(fee)
            + " order " + order;
    }

    static bool SameOrder(string? orderRef, string? orderId) =>
        !string.IsNullOrEmpty(orderId) && string.Equals(orderRef, orderId, StringComparison.Ordinal);

    static bool SameClient(string? clientId, string prefix, string? tradeId, string? orderId, string? productId)
    {
        if (string.IsNullOrEmpty(clientId))
            return false;
        if (!string.IsNullOrEmpty(tradeId) && clientId == prefix + tradeId)
            return true;
        if (!string.IsNullOrEmpty(orderId) && clientId == prefix + orderId)
            return true;
        return !string.IsNullOrEmpty(productId) && clientId == prefix + productId;
    }

    public static string? ContractSide(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var text = value.Trim();
        if (text.Equals("YES", StringComparison.OrdinalIgnoreCase) || text.EndsWith("_YES", StringComparison.OrdinalIgnoreCase))
            return "YES";
        if (text.Equals("NO", StringComparison.OrdinalIgnoreCase) || text.EndsWith("_NO", StringComparison.OrdinalIgnoreCase))
            return "NO";
        return null;
    }

    // Buy cash is total_value_after_fees. Sell cash is the same field after the fee was taken out.
    // Coinbase reports a NO order at the YES price, so the fill price is not the contract price.
    public static decimal? Price(bool buy, decimal totalValueAfterFees, decimal fees, decimal qty)
    {
        if (qty <= 0 || fees < 0 || totalValueAfterFees < 0)
            return null;
        var gross = buy ? totalValueAfterFees - fees : totalValueAfterFees + fees;
        if (gross < 0 || (buy && gross == 0))
            return null;
        return gross / qty;
    }

    public static TradeKind KindOf(string? tradeType) =>
        !string.IsNullOrEmpty(tradeType) && tradeType.Contains("SETTLE", StringComparison.OrdinalIgnoreCase)
            ? TradeKind.Settlement
            : TradeKind.Fill;

    public static string WalletName(string productId)
    {
        var ticker = ContractTickers.KalshiTicker(productId);
        var name = string.IsNullOrEmpty(ticker) ? productId.Trim() : ticker;
        return name.Length <= 200 ? name : name[..200];
    }

    public static string TradeTitle(string contractSide, string side, decimal qty)
    {
        var title = contractSide + " " + side.ToLowerInvariant() + " " + CoinbaseWallets.PythonG(qty);
        return title.Length <= 200 ? title : title[..200];
    }

    public static string TradeNote(string contractSide, string side, string tradeId) =>
        contractSide + " " + side.ToLowerInvariant() + " via WalletSync, trade_id " + tradeId + ".";
}

public static class PredictionBookingReport
{
    public static List<PredictionBookingRow> List(IReadOnlyList<PredictionSighting> seen, IEnumerable<string> walletKeys)
    {
        var keys = walletKeys.Where(key => !string.IsNullOrEmpty(key)).ToList();
        return seen
            .Where(row => !string.IsNullOrWhiteSpace(row.ProductId))
            .Select(row => Describe(row, keys))
            .OrderBy(row => Rank(row.Status))
            .ThenByDescending(row => row.LastSeen)
            .ThenBy(row => row.ProductId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static PredictionBookingRow Describe(PredictionSighting row, List<string> keys)
    {
        var wallet = PredictionMarkets.MatchingKey(row.ProductId, keys) ?? row.Wallet;
        var status = row.Status;
        var keepDetail = status is "problem" or "would-book";
        if (!keepDetail && !string.IsNullOrEmpty(wallet))
            status = "booked";
        return new PredictionBookingRow
        {
            ProductId = row.ProductId,
            FirstSeen = row.FirstSeenUtc,
            LastSeen = row.LastSeenUtc,
            Status = status,
            Wallet = wallet,
            Detail = keepDetail ? row.Detail : null,
        };
    }

    static int Rank(string status) => status switch
    {
        "problem" => 0,
        "would-book" => 1,
        "seen" => 2,
        _ => 3,
    };
}
