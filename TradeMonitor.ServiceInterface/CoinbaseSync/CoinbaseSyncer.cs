using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using TradeMonitor.ServiceModel;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.ServiceInterface;

public static class CoinbaseWallets
{
    public const string Cash = "coinbase-usdc";
    public const string Source = "walletsync";

    static readonly Regex NonAlnum = new("[^a-z0-9]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string KeyFor(string asset) => "coinbase-" + NonAlnum.Replace(asset.ToLowerInvariant(), "-").Trim('-');

    public static string PythonG(decimal value) =>
        value.ToString("0.############################", CultureInfo.InvariantCulture);

    public static decimal Significant(decimal value, int digits)
    {
        if (value == 0 || digits < 1)
            return 0;
        var negative = value < 0;
        var abs = negative ? -value : value;
        var magnitude = (int)Math.Floor(Math.Log10((double)abs));
        var scale = digits - magnitude - 1;
        decimal rounded;
        if (scale >= 0 && scale <= 28)
            rounded = decimal.Round(abs, scale, MidpointRounding.AwayFromZero);
        else if (scale < 0)
        {
            var factor = Pow10(-scale);
            rounded = decimal.Round(abs / factor, 0, MidpointRounding.AwayFromZero) * factor;
        }
        else
            rounded = abs;
        return negative ? -rounded : rounded;
    }

    static decimal Pow10(int exponent)
    {
        decimal value = 1;
        for (var i = 0; i < exponent; i++)
            value *= 10;
        return value;
    }
}

public sealed class CoinbaseSyncState
{
    public Dictionary<string, string> SkippedFills { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, decimal> LastMarks { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> CbrnTicker { get; set; } = new(StringComparer.Ordinal);

    public List<string> StaleWarned { get; set; } = [];

    public bool TickersLearned { get; set; }

    public bool PmShapeLogged { get; set; }
}

public static class CoinbaseSyncStateFile
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static CoinbaseSyncState Load(string path, CoinbaseSyncLog log)
    {
        if (!File.Exists(path))
            return new CoinbaseSyncState();
        try
        {
            var text = File.ReadAllText(path);
            return JsonSerializer.Deserialize<CoinbaseSyncState>(text, Json) ?? new CoinbaseSyncState();
        }
        catch (JsonException)
        {
            log.Error("coinbase sync state unreadable, starting fresh");
            return new CoinbaseSyncState();
        }
    }

    public static void Save(string path, CoinbaseSyncState state)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, Json));
        File.Move(temp, path, overwrite: true);
    }
}

public sealed partial class CoinbaseSyncer
{
    public const decimal ReconcileTolerance = 0.01m;
    public const int ReconcileCycles = 3;
    public static readonly TimeSpan FillOverlap = TimeSpan.FromMinutes(10);

    const decimal DustFiat = 0.01m;
    const decimal Eps = 0.000000001m;

    readonly ICoinbaseReadApi coinbase;
    readonly ILedgerGateway ledger;
    readonly ILedgerClock clock;
    readonly CoinbaseSyncLog log;
    readonly string statePath;
    readonly CoinbaseSyncState state;
    readonly Dictionary<string, CbProduct?> products = new(StringComparer.Ordinal);

    Dictionary<string, Wallet> wallets = new(StringComparer.Ordinal);
    HashSet<string> knownFillIds = new(StringComparer.Ordinal);
    DateTime? cursor;

    public CoinbaseSyncer(
        ICoinbaseReadApi coinbase,
        ILedgerGateway ledger,
        ILedgerClock clock,
        CoinbaseSyncLog log,
        string statePath)
    {
        this.coinbase = coinbase;
        this.ledger = ledger;
        this.clock = clock;
        this.log = log;
        this.statePath = statePath;
        state = CoinbaseSyncStateFile.Load(statePath, log);
    }

    public bool Loaded { get; private set; }

    public int CashMismatch { get; private set; }

    public CoinbaseSyncState State => state;

    public void MarkLedgerStale() => Loaded = false;

    public void SaveState() => CoinbaseSyncStateFile.Save(statePath, state);

    public async Task<(int Fills, int Marks)> CycleAsync(CancellationToken cancellationToken)
    {
        if (!Loaded)
            LoadLedger();
        _ = await coinbase.AccountsAsync(cancellationToken);
        await LearnTickers(cancellationToken);
        var fills = await SyncFills(cancellationToken);
        var marks = await SyncHoldings(cancellationToken);
        SaveState();
        return (fills, marks);
    }

    void LoadLedger()
    {
        var snapshot = ledger.Load();
        wallets = snapshot.Wallets;
        knownFillIds = snapshot.KnownFillIds;
        cursor = snapshot.Cursor;
        Loaded = true;
        log.Info("ledger loaded: " + wallets.Count + " wallets, " + knownFillIds.Count
            + " recorded Coinbase fills, cursor " + (cursor is DateTime at ? Iso(at) : "none"));
    }

    async Task LearnTickers(CancellationToken cancellationToken)
    {
        if (state.TickersLearned)
            return;
        foreach (var fill in await coinbase.FillsAsync(null, cancellationToken))
        {
            if (!string.IsNullOrEmpty(fill.EquityCbrn) && !string.IsNullOrEmpty(fill.EquityTicker))
                state.CbrnTicker[fill.EquityCbrn] = fill.EquityTicker;
        }
        state.TickersLearned = true;
    }

    async Task<int> SyncFills(CancellationToken cancellationToken)
    {
        string? start = cursor is DateTime at ? Iso(at - FillOverlap) : null;
        var fills = (await coinbase.FillsAsync(start, cancellationToken))
            .OrderBy(fill => fill.TradeTimeUtc)
            .ThenBy(fill => fill.TradeId, StringComparer.Ordinal)
            .ToList();
        var added = 0;
        foreach (var fill in fills)
        {
            if (string.IsNullOrEmpty(fill.TradeId))
            {
                log.Error("fill missing trade_id; skipped");
                continue;
            }
            var clientId = "cb-fill-" + fill.TradeId;
            if (knownFillIds.Contains(clientId) || state.SkippedFills.ContainsKey(clientId))
                continue;
            if (cursor is DateTime from && fill.TradeTimeUtc != default && fill.TradeTimeUtc < from - FillOverlap)
                continue;
            if (fill.TradeTimeUtc == default)
            {
                state.SkippedFills[clientId] = "unparseable trade_time";
                continue;
            }
            try
            {
                await RecordFill(fill, clientId, cancellationToken);
                added++;
            }
            catch (LedgerToolException ex)
            {
                var reason = ex.Message.Length > 300 ? ex.Message[..300] : ex.Message;
                log.Error("fill " + clientId + " not recorded: " + reason);
                state.SkippedFills[clientId] = reason;
            }
        }
        return added;
    }

    async Task RecordFill(CbFill fill, string clientId, CancellationToken cancellationToken)
    {
        var classified = await Classify(fill, cancellationToken);
        var key = CoinbaseWallets.KeyFor(classified.Asset);
        var side = fill.Side.Equals("BUY", StringComparison.OrdinalIgnoreCase) ? "Buy" : "Sell";
        var qty = fill.SizeInQuote && fill.Price > 0
            ? FloorTo(fill.Size / fill.Price, classified.Increment)
            : fill.Size;
        if (qty <= 0)
            throw new LedgerToolException("zero quantity");
        var kind = classified.Kind;
        EnsureWallet(key, classified.Label, "Real Coinbase " + kind + " holding, created by WalletSync.");
        if (side == "Buy")
            FundBuy(key, fill.TradeId, decimal.Round(qty * fill.Price + fill.Commission, 8, MidpointRounding.ToEven), fill.TradeTimeUtc);
        ledger.RecordTrade(new RecordTrade
        {
            Wallet = key,
            ClientId = clientId,
            Symbol = classified.Symbol,
            Side = side == "Buy" ? TradeSide.Buy : TradeSide.Sell,
            Qty = qty,
            Price = fill.Price,
            Fee = fill.Commission,
            Kind = fill.TradeType.Contains("SETTLE", StringComparison.OrdinalIgnoreCase) ? TradeKind.Settlement : TradeKind.Fill,
            ExecutedAt = fill.TradeTimeUtc,
            Title = classified.Label + " " + side.ToLowerInvariant() + " " + CoinbaseWallets.PythonG(qty),
            OrderRef = fill.OrderId,
            Note = "Real Coinbase " + kind + " fill via WalletSync, trade_id " + fill.TradeId + ".",
        });
        log.Info("recorded " + clientId + " " + side + " " + CoinbaseWallets.PythonG(qty) + " " + classified.Symbol
            + " @ " + CoinbaseWallets.PythonG(fill.Price) + " fee " + CoinbaseWallets.PythonG(fill.Commission));
        if (side == "Sell")
            MoveProceeds(key, fill.TradeId, decimal.Round(qty * fill.Price - fill.Commission, 8, MidpointRounding.ToEven), fill.TradeTimeUtc);
        knownFillIds.Add(clientId);
        if (cursor == null || fill.TradeTimeUtc > cursor)
            cursor = fill.TradeTimeUtc;
    }

    void FundBuy(string key, string tradeId, decimal cost, DateTime at)
    {
        var cash = ledger.Balances(key).Cash;
        var need = decimal.Round(cost - cash, 8, MidpointRounding.ToEven);
        if (need <= Eps)
            return;
        if (key != CoinbaseWallets.Cash && wallets.ContainsKey(CoinbaseWallets.Cash))
        {
            var available = ledger.Balances(CoinbaseWallets.Cash).Cash;
            var take = decimal.Round(Math.Min(available, need), 8, MidpointRounding.ToEven);
            if (take > Eps)
            {
                ledger.RecordTransfer(new RecordTransfer
                {
                    Wallet = CoinbaseWallets.Cash,
                    ClientId = "cb-fund-src-" + tradeId,
                    Kind = TransferKind.Withdrawal,
                    Amount = take,
                    At = at,
                    Note = "Cash used for " + key + " buy (trade " + tradeId + "), WalletSync.",
                });
            }
        }
        ledger.RecordTransfer(new RecordTransfer
        {
            Wallet = key,
            ClientId = "cb-fund-" + tradeId,
            Kind = TransferKind.Deposit,
            Amount = need,
            At = at,
            Note = "Cash for buy trade " + tradeId + ", moved from " + CoinbaseWallets.Cash + " by WalletSync.",
        });
    }

    void MoveProceeds(string key, string tradeId, decimal proceeds, DateTime at)
    {
        if (key == CoinbaseWallets.Cash || !wallets.ContainsKey(CoinbaseWallets.Cash))
            return;
        var cash = ledger.Balances(key).Cash;
        var amount = decimal.Round(Math.Min(proceeds, cash), 8, MidpointRounding.ToEven);
        if (amount <= Eps)
            return;
        ledger.RecordTransfer(new RecordTransfer
        {
            Wallet = key,
            ClientId = "cb-proceeds-" + tradeId,
            Kind = TransferKind.Withdrawal,
            Amount = amount,
            At = at,
            Note = "Sale proceeds (trade " + tradeId + ") moved to " + CoinbaseWallets.Cash + ". Transfer, not profit.",
        });
        ledger.RecordTransfer(new RecordTransfer
        {
            Wallet = CoinbaseWallets.Cash,
            ClientId = "cb-proceeds-dst-" + tradeId,
            Kind = TransferKind.Deposit,
            Amount = amount,
            At = at,
            Note = "Sale proceeds from " + key + " (trade " + tradeId + "). Transfer, not profit.",
        });
    }

    async Task<int> SyncHoldings(CancellationToken cancellationToken)
    {
        var (hold, cash) = await Holdings(cancellationToken);
        foreach (var pair in hold)
        {
            if (pair.Value.Fiat >= DustFiat)
            {
                EnsureWallet(
                    CoinbaseWallets.KeyFor(pair.Key),
                    pair.Key,
                    "Real Coinbase " + pair.Value.Type + " holding found by WalletSync. No lot recorded until a fill is seen.");
            }
        }

        var minute = Minute(clock.UtcNow);
        var marks = new List<PendingMark>();
        foreach (var key in wallets.Keys.OrderBy(name => name, StringComparer.Ordinal))
        {
            if (!key.StartsWith("coinbase-", StringComparison.Ordinal) || key == CoinbaseWallets.Cash)
                continue;
            if (!wallets.TryGetValue(key, out var wallet) || wallet.IsArchived)
                continue;
            var balances = ledger.Balances(key);
            foreach (var position in balances.Positions)
            {
                var symbol = position.Symbol;
                var asset = symbol.EndsWith("-USD", StringComparison.Ordinal) ? symbol[..^4] : symbol;
                if (!hold.TryGetValue(asset, out var holding) || holding.Qty <= 0)
                {
                    var stale = key + "|" + symbol;
                    if (!state.StaleWarned.Contains(stale))
                    {
                        log.Info(key + " " + symbol + " is open in TradeMonitor but not held on Coinbase; not marking");
                        state.StaleWarned.Add(stale);
                    }
                    continue;
                }
                decimal? price = null;
                if (holding.Type == "spot")
                {
                    price = await coinbase.PublicPriceAsync(symbol, cancellationToken);
                    price ??= await coinbase.PublicPriceAsync(asset + "-USDC", cancellationToken);
                }
                if (price is null or <= 0)
                    price = holding.Price;
                if (price is null or <= 0)
                    continue;
                var rounded = CoinbaseWallets.Significant(price.Value, 10);
                var tag = key + "|" + symbol;
                decimal? last = state.LastMarks.TryGetValue(tag, out var remembered) ? remembered : null;
                if (last == null && position.LastMarkPrice is decimal marked)
                    last = CoinbaseWallets.Significant(marked, 10);
                if (last is decimal previous && Math.Abs(previous - rounded) < 0.000000000001m)
                    continue;
                var assetPart = key.StartsWith("coinbase-", StringComparison.Ordinal) ? key["coinbase-".Length..] : key;
                marks.Add(new PendingMark
                {
                    Tag = tag,
                    Price = rounded,
                    Write = new PriceMarkWrite
                    {
                        Symbol = symbol,
                        Price = rounded,
                        At = minute,
                        Wallet = key,
                        ClientId = "cb-mark-" + assetPart + "-" + symbol + "-" + minute.ToString("yyyyMMdd'T'HHmm", CultureInfo.InvariantCulture),
                        Source = CoinbaseWallets.Source,
                    },
                });
            }
        }
        if (marks.Count > 0)
        {
            ledger.RecordMarks(marks.Select(mark => mark.Write).ToList());
            foreach (var mark in marks)
                state.LastMarks[mark.Tag] = mark.Price;
        }
        ReconcileCash(cash);
        return marks.Count;
    }

    async Task<(Dictionary<string, Holding> Hold, decimal Cash)> Holdings(CancellationToken cancellationToken)
    {
        var hold = new Dictionary<string, Holding>(StringComparer.Ordinal);
        decimal cash = 0;
        foreach (var portfolio in await coinbase.PortfoliosAsync(cancellationToken))
        {
            var breakdown = await coinbase.BreakdownAsync(portfolio.Uuid, cancellationToken);
            foreach (var spot in breakdown.Spot)
            {
                if (spot.IsCash)
                {
                    cash += spot.Fiat;
                    continue;
                }
                if (string.IsNullOrEmpty(spot.Asset) || spot.AccountType == "ACCOUNT_TYPE_CCM_EQUITY")
                    continue;
                if (spot.Qty <= 0)
                    continue;
                if (!hold.TryGetValue(spot.Asset, out var holding))
                {
                    holding = new Holding { Type = "spot" };
                    hold[spot.Asset] = holding;
                }
                holding.Qty += spot.Qty;
                holding.Fiat += spot.Fiat;
            }
            foreach (var equity in breakdown.Equity)
            {
                if (!state.CbrnTicker.TryGetValue(equity.Cbrn, out var ticker))
                {
                    var tail = equity.Cbrn.Length > 12 ? equity.Cbrn[^12..] : equity.Cbrn;
                    log.Info("equity position " + tail + " has no known ticker yet");
                    continue;
                }
                if (equity.Qty > 0)
                    hold[ticker] = new Holding { Qty = equity.Qty, Fiat = equity.Fiat, Type = "equity" };
            }
            foreach (var contract in breakdown.Prediction)
            {
                if (!string.IsNullOrEmpty(contract.ProductId) && contract.Qty != 0)
                {
                    hold[contract.ProductId] = new Holding
                    {
                        Qty = Math.Abs(contract.Qty),
                        Fiat = Math.Abs(contract.Fiat),
                        Type = "contract",
                    };
                }
                else if (!state.PmShapeLogged)
                {
                    log.Info("prediction position fields: " + string.Join(", ", contract.FieldNames));
                    state.PmShapeLogged = true;
                }
            }
        }
        foreach (var holding in hold.Values)
            holding.Price = holding.Qty != 0 ? holding.Fiat / holding.Qty : 0;
        return (hold, cash);
    }

    void ReconcileCash(decimal coinbaseCash)
    {
        if (!wallets.ContainsKey(CoinbaseWallets.Cash))
            return;
        var ledgerCash = ledger.Balances(CoinbaseWallets.Cash).Cash;
        var diff = decimal.Round(coinbaseCash - ledgerCash, 2, MidpointRounding.ToEven);
        if (Math.Abs(diff) < ReconcileTolerance)
        {
            CashMismatch = 0;
            return;
        }
        CashMismatch++;
        log.Info("cash differs: Coinbase " + coinbaseCash.ToString("0.0000", CultureInfo.InvariantCulture)
            + " vs TradeMonitor " + ledgerCash.ToString("0.0000", CultureInfo.InvariantCulture)
            + " (cycle " + CashMismatch + " of " + ReconcileCycles + ")");
        if (CashMismatch < ReconcileCycles)
            return;
        var now = Second(clock.UtcNow);
        ledger.RecordTransfer(new RecordTransfer
        {
            Wallet = CoinbaseWallets.Cash,
            Kind = diff > 0 ? TransferKind.Deposit : TransferKind.Withdrawal,
            Amount = Math.Abs(diff),
            ClientId = "cb-cash-reconcile-" + now.ToString("yyyyMMdd'T'HHmm", CultureInfo.InvariantCulture),
            At = now,
            Note = "WalletSync reconciliation to live Coinbase USD plus USDC ("
                + coinbaseCash.ToString("0.0000", CultureInfo.InvariantCulture) + ").",
        });
        CashMismatch = 0;
    }

    bool EnsureWallet(string key, string label, string note)
    {
        if (wallets.TryGetValue(key, out var existing))
        {
            if (existing.IsArchived)
            {
                log.Info("wallet " + key + " exists but is archived; not recreating");
                return false;
            }
            return true;
        }
        log.Info("creating wallet " + key);
        var created = ledger.CreateWallet(key, "Coinbase " + label, note);
        wallets[key] = created;
        return true;
    }

    async Task<Classified> Classify(CbFill fill, CancellationToken cancellationToken)
    {
        var equity = fill.EquityTicker != null || fill.EquityCbrn != null
            || string.Equals(fill.ProductType, "EQUITY", StringComparison.OrdinalIgnoreCase);
        if (equity)
        {
            var product = await Product(fill.ProductId, cancellationToken);
            var ticker = string.IsNullOrEmpty(fill.EquityTicker) ? null : fill.EquityTicker;
            if (string.IsNullOrEmpty(ticker))
                ticker = product?.BaseCurrencyId;
            if (string.IsNullOrEmpty(ticker))
                ticker = fill.ProductId.Length >= 8 ? fill.ProductId[..8] : fill.ProductId;
            if (!string.IsNullOrEmpty(fill.EquityCbrn))
                state.CbrnTicker[fill.EquityCbrn] = ticker;
            var increment = product?.BaseIncrement ?? "0.00001";
            return new Classified(ticker, ticker + "-USD", ticker, increment, "equity");
        }
        var match = SpotProduct().Match(fill.ProductId);
        if (match.Success)
        {
            var product = await Product(fill.ProductId, cancellationToken);
            var asset = match.Groups[1].Value;
            return new Classified(asset, asset + "-USD", asset, product?.BaseIncrement ?? "0.00000001", "spot");
        }
        return new Classified(fill.ProductId, fill.ProductId, fill.ProductId, "0.01", "contract");
    }

    async Task<CbProduct?> Product(string productId, CancellationToken cancellationToken)
    {
        if (!products.TryGetValue(productId, out var product))
        {
            product = await coinbase.ProductAsync(productId, cancellationToken);
            products[productId] = product;
        }
        return product;
    }

    static decimal FloorTo(decimal value, string stepText)
    {
        if (!decimal.TryParse(stepText, NumberStyles.Number, CultureInfo.InvariantCulture, out var step) || step <= 0)
            return value;
        return decimal.Floor(value / step) * step;
    }

    static string Iso(DateTime utc)
    {
        var value = utc.Kind == DateTimeKind.Utc ? utc : utc.ToUniversalTime();
        return value.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    }

    static DateTime Minute(DateTime utc)
    {
        var value = utc.Kind == DateTimeKind.Utc ? utc : utc.ToUniversalTime();
        return new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, DateTimeKind.Utc);
    }

    static DateTime Second(DateTime utc)
    {
        var value = utc.Kind == DateTimeKind.Utc ? utc : utc.ToUniversalTime();
        return new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second, DateTimeKind.Utc);
    }

    [GeneratedRegex(@"^([A-Z0-9]+)-(USD|USDC)$", RegexOptions.CultureInvariant)]
    private static partial Regex SpotProduct();

    sealed class Holding
    {
        public decimal Qty { get; set; }
        public decimal Fiat { get; set; }
        public decimal Price { get; set; }
        public string Type { get; set; } = "";
    }

    sealed record Classified(string Asset, string Symbol, string Label, string Increment, string Kind);

    sealed class PendingMark
    {
        public string Tag { get; init; } = "";
        public decimal Price { get; init; }
        public PriceMarkWrite Write { get; init; } = new();
    }
}
