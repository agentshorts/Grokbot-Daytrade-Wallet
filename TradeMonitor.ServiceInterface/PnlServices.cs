using ServiceStack;
using ServiceStack.OrmLite;
using TradeMonitor.ServiceModel;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.ServiceInterface;

// Read models for the overview, wallet pages, and trades table.
// pnl_by_day calls Any(PnlByDay). Writes stay on the ledger services.
public class PnlServices(ServiceStack.Data.IDbConnectionFactory dbFactory, ILedgerClock clock) : Service
{
    const int MaxTake = 1000;
    static readonly TimeSpan ChartMargin = TimeSpan.FromDays(1);

    public object Any(GetSummary request)
    {
        var from = At(request.From);
        var to = At(request.To);
        Range(from, to);
        var mode = Mode(request.Mode);
        using var db = dbFactory.Open();
        var books = LedgerData.Load(db, Now());
        var universe = Universe(books, request.Wallet, request.IncludeArchived);
        var shown = mode == null ? universe : universe.Where(x => x.Wallet.Mode == mode.Value).ToList();
        return new GetSummaryResponse
        {
            Totals = PnlReport.Total(shown.Select(x => PnlReport.Summarize(x, from, to)).ToList()),
            Modes =
            [
                ModeTotals("all", universe, from, to, null),
                ModeTotals("paper", universe, from, to, WalletMode.Paper),
                ModeTotals("real", universe, from, to, WalletMode.Real),
            ],
            Wallets = shown
                .Select(x => PnlReport.Summarize(x, from, to))
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Name, StringComparer.Ordinal)
                .ThenBy(x => x.Key, StringComparer.Ordinal)
                .ToList(),
        };
    }

    public object Any(GetEquity request)
    {
        var from = At(request.From);
        var to = At(request.To);
        Range(from, to);
        var mode = Mode(request.Mode);
        using var db = dbFactory.Open();
        var shown = Shown(LedgerData.Load(db, Now()), request.Wallet, request.IncludeArchived, mode);
        var series = shown.Select(x => new WalletEquity
        {
            Wallet = x.Wallet.Key,
            Name = x.Wallet.Name,
            Mode = x.Wallet.Mode,
            Points = PnlReport.Equity(x.Replay, from, to),
        }).ToList();
        return new GetEquityResponse
        {
            Wallets = series,
            Total = PnlReport.CombineEquity(series.Select(x => (IReadOnlyList<EquityPoint>)x.Points).ToList()),
        };
    }

    // Gateway entry for the pnl_by_day tool: new PnlByDay { Wallet, From, To }.
    public object Any(PnlByDay request) => ByDay(request);

    PnlByDayResponse ByDay(PnlByDay request)
    {
        var from = At(request.From);
        var to = At(request.To);
        Range(from, to);
        var mode = Mode(request.Mode);
        using var db = dbFactory.Open();
        var shown = Shown(LedgerData.Load(db, Now()), request.Wallet, request.IncludeArchived, mode);
        var wallets = shown.Select(book => new WalletPnlDays
        {
            Wallet = book.Wallet.Key,
            Name = book.Wallet.Name,
            Mode = book.Wallet.Mode,
            Days = PnlReport.Days(book.Replay, from, to),
        }).ToList();
        return new PnlByDayResponse
        {
            Results = PnlReport.CombineDays(wallets.Select(x => (IReadOnlyList<PnlDayResult>)x.Days)),
            Wallets = wallets,
        };
    }

    public object Any(GetPnlBySymbol request)
    {
        var from = At(request.From);
        var to = At(request.To);
        Range(from, to);
        var take = Take(request.Take, allowAll: true);
        var mode = Mode(request.Mode);
        using var db = dbFactory.Open();
        var shown = Shown(LedgerData.Load(db, Now()), request.Wallet, request.IncludeArchived, mode);
        return new GetPnlBySymbolResponse
        {
            Results = PnlReport.Symbols(shown, from, to, take),
        };
    }

    public object Any(GetFees request)
    {
        var days = ByDay(new PnlByDay
        {
            Wallet = request.Wallet,
            Mode = request.Mode,
            From = request.From,
            To = request.To,
            IncludeArchived = request.IncludeArchived,
        });
        return new GetFeesResponse
        {
            Total = days.Results.Sum(x => x.Fees),
            Days = days.Results.Select(x => new FeeDay { Date = x.Date, Fees = x.Fees }).ToList(),
        };
    }

    public object Any(QueryPositions request)
    {
        var mode = Mode(request.Mode);
        var status = Status(request.Status);
        var symbol = Clean(request.Symbol)?.ToUpperInvariant();
        if (symbol?.Length > 64)
            throw Fail(400, "InvalidRequest", "Symbol cannot be longer than 64 characters.");
        using var db = dbFactory.Open();
        var shown = Shown(LedgerData.Load(db, Now()), request.Wallet, request.IncludeArchived, mode);
        return new QueryPositionsResponse
        {
            MarkStaleAfterMinutes = PnlReport.MarkStaleMinutes,
            Results = PnlReport.Positions(shown, Now(), symbol, status),
        };
    }

    public object Any(GetTrade request)
    {
        if (request.Id <= 0)
            throw Fail(404, "UnknownTrade", $"Unknown trade '{request.Id}'.");
        using var db = dbFactory.Open();
        var trade = db.SingleById<Trade>(request.Id)
            ?? throw Fail(404, "UnknownTrade", $"Unknown trade '{request.Id}'.");
        var books = LedgerData.Load(db, Now());
        var book = books.FirstOrDefault(x => x.Wallet.Id == trade.WalletId)
            ?? throw Fail(500, "LedgerCorrupt", $"Trade '{request.Id}' has no wallet.");
        var lot = book.Replay.LotTrades.FirstOrDefault(x => x.TradeId == trade.Id)
            ?? throw Fail(500, "LedgerCorrupt", $"Trade '{request.Id}' is missing from the ledger replay.");

        var times = new List<DateTime> { trade.ExecutedAt };
        times.AddRange(lot.Fills.Select(x => x.ExecutedAt));
        var chartFrom = times.Min() - ChartMargin;
        var chartTo = times.Max() + ChartMargin;
        if (lot.Status == PnlTerms.Open && Now() + ChartMargin > chartTo)
            chartTo = Now() + ChartMargin;

        var marks = book.Marks
            .Where(x => string.Equals(x.Symbol, trade.Symbol, StringComparison.Ordinal)
                && x.At >= chartFrom && x.At <= chartTo)
            .OrderBy(x => x.At)
            .ThenBy(x => x.Id)
            .Select(x => new PriceMarkView
            {
                Id = x.Id,
                ClientId = x.ClientId,
                Symbol = x.Symbol,
                Price = x.Price,
                At = x.At,
                Source = x.Source,
                WalletId = x.WalletId,
                Wallet = x.WalletId == null ? null : book.Wallet.Key,
                RecordedAt = x.RecordedAt,
            })
            .ToList();

        var markers = new List<ChartMarker>
        {
            new()
            {
                TradeId = trade.Id,
                Side = trade.Side,
                At = trade.ExecutedAt,
                Price = trade.Price,
                Qty = trade.Qty,
            },
        };
        foreach (var fill in lot.Fills)
        {
            markers.Add(new ChartMarker
            {
                TradeId = fill.TradeId,
                Side = fill.Side,
                At = fill.ExecutedAt,
                Price = fill.Price,
                Qty = fill.Qty,
            });
        }

        return new GetTradeResponse
        {
            Result = TradeMaps.ToView(trade, book.Wallet, lot),
            Fills = lot.Fills.Select(fill => new TradeFillView
            {
                TradeId = fill.TradeId,
                Side = fill.Side,
                Symbol = fill.Symbol,
                Qty = fill.Qty,
                Price = fill.Price,
                BuyFee = fill.BuyFee,
                SellFee = fill.SellFee,
                Cost = fill.Cost,
                Proceeds = fill.Proceeds,
                RealizedPnl = fill.RealizedPnl,
                ExecutedAt = fill.ExecutedAt,
            }).ToList(),
            Markers = markers.OrderBy(x => x.At).ThenBy(x => x.TradeId).ToList(),
            PriceMarks = marks,
            HasPriceMarks = marks.Count > 0,
            ChartFrom = chartFrom,
            ChartTo = chartTo,
        };
    }

    static ModePnl ModeTotals(string name, List<WalletBook> universe, DateTime? from, DateTime? to, WalletMode? mode)
    {
        var books = mode == null ? universe : universe.Where(x => x.Wallet.Mode == mode.Value).ToList();
        return new ModePnl
        {
            Mode = name,
            Totals = PnlReport.Total(books.Select(x => PnlReport.Summarize(x, from, to)).ToList()),
        };
    }

    List<WalletBook> Shown(List<WalletBook> books, string? wallet, bool includeArchived, WalletMode? mode)
    {
        var universe = Universe(books, wallet, includeArchived);
        return mode == null ? universe : universe.Where(x => x.Wallet.Mode == mode.Value).ToList();
    }

    static List<WalletBook> Universe(List<WalletBook> books, string? wallet, bool includeArchived)
    {
        var key = Clean(wallet);
        if (key != null)
        {
            var match = books.FirstOrDefault(x => x.Wallet.Key == key)
                ?? throw Fail(404, "UnknownWallet", $"Unknown wallet '{key}'.");
            return [match];
        }

        return books.Where(x => includeArchived || !x.Wallet.IsArchived).ToList();
    }

    DateTime Now() => At(clock.UtcNow) ?? DateTime.UtcNow;

    static DateTime? At(DateTime? value) => value is DateTime at ? AsUtc(at) : null;

    static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    static WalletMode? Mode(string? mode)
    {
        var text = Clean(mode);
        if (text == null || text.Equals("all", StringComparison.OrdinalIgnoreCase))
            return null;
        if (text.Equals("paper", StringComparison.OrdinalIgnoreCase))
            return WalletMode.Paper;
        if (text.Equals("real", StringComparison.OrdinalIgnoreCase))
            return WalletMode.Real;
        throw Fail(400, "InvalidRequest", "Mode must be all, paper, or real.");
    }

    static string? Status(string? status)
    {
        var text = Clean(status)?.ToLowerInvariant();
        if (text == null)
            return null;
        if (text is PnlTerms.Open or PnlTerms.Closed)
            return text;
        throw Fail(400, "InvalidRequest", "Status must be open or closed.");
    }

    static void Range(DateTime? from, DateTime? to)
    {
        if (from is DateTime start && to is DateTime end && start > end)
            throw Fail(400, "InvalidRequest", "From is after to.");
    }

    static int? Take(int? take, bool allowAll)
    {
        if (take is null)
            return allowAll ? null : 100;
        if (take < 0)
            throw Fail(400, "InvalidRequest", "Take cannot be negative.");
        if (take > MaxTake)
            throw Fail(400, "InvalidRequest", $"Take cannot exceed {MaxTake}.");
        return take;
    }

    static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return value.Trim();
    }

    static HttpError Fail(int status, string code, string message) => new(status, code, message);
}
