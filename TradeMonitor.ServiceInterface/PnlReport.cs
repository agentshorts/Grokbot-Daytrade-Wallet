using ServiceStack;
using TradeMonitor.ServiceModel;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.ServiceInterface;

// Turns one ledger replay into the read models the overview, wallet pages, and pnl_by_day share.
// Realized P&L is booked when a sell matches lots. Unrealized P&L is mark minus remaining cost.
// A missing mark stays null so an unmarked position is never reported as zero P&L.
// Deposits, withdrawals, and sweeps change value and are not P&L.
public static class PnlReport
{
    public const int MaxDays = 3660;
    public const int MarkStaleMinutes = 15;

    public static List<PnlDayResult> Days(LedgerReplay replay, DateTime? from, DateTime? to)
    {
        if (!TryBounds(replay, from, to, out var first, out var last))
            return [];

        var days = new List<PnlDayResult>();
        for (var day = first; day <= last; day = day.AddDays(1))
        {
            var dayEnd = day.AddDays(1).AddTicks(-1);
            var windowStart = from is DateTime start && start > day ? start : day;
            var windowEnd = to is DateTime end && end < dayEnd ? end : dayEnd;
            if (windowStart > windowEnd)
                continue;

            var before = LastBefore(replay.States, windowStart);
            var after = LastAtOrBefore(replay.States, windowEnd);
            var startUnrealized = before == null ? 0 : before.UnrealizedPnl;
            var endUnrealized = after == null ? 0 : after.UnrealizedPnl;
            decimal? change = startUnrealized is decimal opened && endUnrealized is decimal closed
                ? closed - opened
                : null;
            var realized = Delta(replay.States, windowStart, windowEnd, x => x.RealizedPnl);
            days.Add(new PnlDayResult
            {
                Date = day,
                RealizedPnl = realized,
                UnrealizedChange = change,
                Pnl = change is decimal move ? realized + move : null,
                Fees = Delta(replay.States, windowStart, windowEnd, x => x.Fees),
                Deposits = Delta(replay.States, windowStart, windowEnd, x => x.Deposits),
                Withdrawals = Delta(replay.States, windowStart, windowEnd, x => x.Withdrawals),
                Sweeps = Delta(replay.States, windowStart, windowEnd, x => x.Sweeps),
                TotalValue = after?.TotalValue ?? 0,
                ValueChange = (after?.TotalValue ?? 0) - (before?.TotalValue ?? 0),
                Cash = after?.Cash ?? 0,
                PositionsValue = after?.PositionsValue ?? 0,
                OpenExposure = after?.MarkedValue ?? 0,
                UnmarkedCost = after?.UnmarkedCost ?? 0,
                ValuedAtCost = after?.ValuedAtCost ?? false,
                Trades = DeltaCount(replay.States, windowStart, windowEnd),
            });
        }

        return days;
    }

    public static List<PnlDayResult> CombineDays(IEnumerable<IReadOnlyList<PnlDayResult>> series)
    {
        var map = new SortedDictionary<DateTime, PnlDayResult>();
        foreach (var days in series)
        {
            foreach (var day in days)
            {
                if (!map.TryGetValue(day.Date, out var acc))
                {
                    map[day.Date] = Copy(day);
                    continue;
                }

                acc.RealizedPnl += day.RealizedPnl;
                acc.Fees += day.Fees;
                acc.Deposits += day.Deposits;
                acc.Withdrawals += day.Withdrawals;
                acc.Sweeps += day.Sweeps;
                acc.TotalValue += day.TotalValue;
                acc.ValueChange += day.ValueChange;
                acc.Cash += day.Cash;
                acc.PositionsValue += day.PositionsValue;
                acc.OpenExposure += day.OpenExposure;
                acc.UnmarkedCost += day.UnmarkedCost;
                acc.Trades += day.Trades;
                acc.ValuedAtCost |= day.ValuedAtCost;
                acc.UnrealizedChange = Add(acc.UnrealizedChange, day.UnrealizedChange);
                acc.Pnl = Add(acc.Pnl, day.Pnl);
            }
        }

        return map.Values.ToList();
    }

    public static List<EquityPoint> Equity(LedgerReplay replay, DateTime? from, DateTime? to)
    {
        var points = new List<EquityPoint>();
        LedgerState? before = null;
        foreach (var state in replay.States)
        {
            if (from is DateTime start && state.At < start)
            {
                before = state;
                continue;
            }

            if (to is DateTime end && state.At > end)
                break;
            points.Add(ToPoint(state.At, state));
        }

        if (from is DateTime at && (points.Count == 0 || points[0].At != at))
            points.Insert(0, ToPoint(at, before));
        return points;
    }

    public static List<EquityPoint> CombineEquity(IReadOnlyList<IReadOnlyList<EquityPoint>> series)
    {
        var times = new SortedSet<DateTime>();
        foreach (var points in series)
        {
            foreach (var point in points)
                times.Add(point.At);
        }

        var index = new int[series.Count];
        var carried = new EquityPoint?[series.Count];
        var total = new List<EquityPoint>(times.Count);
        foreach (var time in times)
        {
            decimal? unrealized = 0;
            var row = new EquityPoint { At = time, UnrealizedPnl = 0 };
            for (var i = 0; i < series.Count; i++)
            {
                var points = series[i];
                while (index[i] < points.Count && points[index[i]].At <= time)
                {
                    carried[i] = points[index[i]];
                    index[i]++;
                }

                var point = carried[i];
                if (point == null)
                    continue;
                row.TotalValue += point.TotalValue;
                row.Cash += point.Cash;
                row.PositionsValue += point.PositionsValue;
                row.OpenExposure += point.OpenExposure;
                row.UnmarkedCost += point.UnmarkedCost;
                row.Swept += point.Swept;
                row.ValuedAtCost |= point.ValuedAtCost;
                if (point.UnrealizedPnl is null)
                    unrealized = null;
                else if (unrealized is decimal sum)
                    unrealized += point.UnrealizedPnl.Value;
            }

            row.UnrealizedPnl = unrealized;
            total.Add(row);
        }

        return total;
    }

    public static WalletCard Summarize(WalletBook book, DateTime? from, DateTime? to)
    {
        var replay = book.Replay;
        var end = to == null
            ? replay.States.LastOrDefault()
            : LastAtOrBefore(replay.States, to.Value);
        LedgerState? start = from == null ? null : LastBefore(replay.States, from.Value);
        var startUnrealized = start == null ? 0 : start.UnrealizedPnl;
        var endUnrealized = end == null ? 0 : end.UnrealizedPnl;
        decimal? change = startUnrealized is decimal opened && endUnrealized is decimal closed
            ? closed - opened
            : null;
        var windowStart = from ?? DateTime.MinValue;
        var windowEnd = to ?? DateTime.MaxValue;
        var realized = Delta(replay.States, windowStart, windowEnd, x => x.RealizedPnl);
        var sells = replay.LotTrades.Where(x => x.Side == TradeSide.Sell && InRange(x.ExecutedAt, from, to)).ToList();
        var wins = sells.Count(x => x.RealizedPnl > 0);
        var losses = sells.Count(x => x.RealizedPnl < 0);
        var scratches = sells.Count(x => x.RealizedPnl == 0);
        var open = 0;
        if (end != null)
            open = end.Symbols.Values.Count(x => x.Qty > 0);

        return new WalletCard
        {
            Id = book.Wallet.Id,
            Key = book.Wallet.Key,
            Name = book.Wallet.Name,
            Mode = book.Wallet.Mode,
            BaseCurrency = book.Wallet.BaseCurrency,
            IsArchived = book.Wallet.IsArchived,
            SortOrder = book.Wallet.SortOrder,
            TotalValue = end?.TotalValue ?? 0,
            ValueChange = (end?.TotalValue ?? 0) - (start?.TotalValue ?? 0),
            Cash = end?.Cash ?? 0,
            Swept = end?.Swept ?? 0,
            OpenExposure = end?.MarkedValue ?? 0,
            UnmarkedCost = end?.UnmarkedCost ?? 0,
            PositionsValue = end?.PositionsValue ?? 0,
            ValuedAtCost = end?.ValuedAtCost ?? false,
            RealizedPnl = realized,
            UnrealizedPnl = endUnrealized,
            UnrealizedChange = change,
            Pnl = change is decimal move ? realized + move : null,
            Fees = Delta(replay.States, windowStart, windowEnd, x => x.Fees),
            Deposits = Delta(replay.States, windowStart, windowEnd, x => x.Deposits),
            Withdrawals = Delta(replay.States, windowStart, windowEnd, x => x.Withdrawals),
            Sweeps = Delta(replay.States, windowStart, windowEnd, x => x.Sweeps),
            WinRate = Rate(wins, losses),
            Wins = wins,
            Losses = losses,
            Scratches = scratches,
            TradeCount = DeltaCount(replay.States, windowStart, windowEnd),
            OpenPositions = open,
            LastActivityAt = book.LastActivityAt,
            Sparkline = Equity(replay, from, to),
        };
    }

    public static PnlTotals Total(IReadOnlyList<WalletCard> cards)
    {
        var totals = new PnlTotals
        {
            Wallets = cards.Count,
            BaseCurrency = "USD",
            UnrealizedPnl = 0,
            UnrealizedChange = 0,
            Pnl = 0,
        };
        if (cards.Count == 0)
            return totals;

        var currencies = cards.Select(x => x.BaseCurrency).Distinct(StringComparer.Ordinal).ToList();
        totals.MixedCurrencies = currencies.Count > 1;
        totals.BaseCurrency = currencies.Count == 1 ? currencies[0] : null;
        foreach (var card in cards)
        {
            totals.TotalValue += card.TotalValue;
            totals.ValueChange += card.ValueChange;
            totals.Cash += card.Cash;
            totals.Swept += card.Swept;
            totals.OpenExposure += card.OpenExposure;
            totals.UnmarkedCost += card.UnmarkedCost;
            totals.PositionsValue += card.PositionsValue;
            totals.ValuedAtCost |= card.ValuedAtCost;
            totals.RealizedPnl += card.RealizedPnl;
            totals.Fees += card.Fees;
            totals.Deposits += card.Deposits;
            totals.Withdrawals += card.Withdrawals;
            totals.Sweeps += card.Sweeps;
            totals.Wins += card.Wins;
            totals.Losses += card.Losses;
            totals.Scratches += card.Scratches;
            totals.TradeCount += card.TradeCount;
            totals.OpenPositions += card.OpenPositions;
            totals.UnrealizedPnl = Add(totals.UnrealizedPnl, card.UnrealizedPnl);
            totals.UnrealizedChange = Add(totals.UnrealizedChange, card.UnrealizedChange);
            totals.Pnl = Add(totals.Pnl, card.Pnl);
        }

        totals.WinRate = Rate(totals.Wins, totals.Losses);
        return totals;
    }

    public static List<SymbolPnl> Symbols(IReadOnlyList<WalletBook> books, DateTime? from, DateTime? to, int? take)
    {
        var rows = new Dictionary<string, SymbolPnl>(StringComparer.Ordinal);
        foreach (var book in books)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var state in book.Replay.States)
            {
                foreach (var name in state.Symbols.Keys)
                    names.Add(name);
            }

            var end = to == null ? book.Replay.States.LastOrDefault() : LastAtOrBefore(book.Replay.States, to.Value);
            var start = from == null ? null : LastBefore(book.Replay.States, from.Value);
            foreach (var name in names)
            {
                var endSnap = Snap(end, name);
                var startSnap = Snap(start, name);
                var realized = (endSnap?.Realized ?? 0) - (startSnap?.Realized ?? 0);
                var fees = (endSnap?.Fees ?? 0) - (startSnap?.Fees ?? 0);
                decimal? startUnrealized = startSnap == null ? 0 : startSnap.Unrealized;
                decimal? endUnrealized = endSnap == null ? 0 : endSnap.Unrealized;
                decimal? change = startUnrealized is decimal opened && endUnrealized is decimal closed
                    ? closed - opened
                    : null;
                var qty = endSnap?.Qty ?? 0;
                var trades = book.Replay.LotTrades
                    .Where(x => x.Symbol == name && InRange(x.ExecutedAt, from, to))
                    .ToList();
                var wins = trades.Count(x => x.Side == TradeSide.Sell && x.RealizedPnl > 0);
                var losses = trades.Count(x => x.Side == TradeSide.Sell && x.RealizedPnl < 0);
                var interesting = qty > 0 || realized != 0 || fees != 0 || trades.Count > 0
                    || (change is decimal move && move != 0);
                if (!interesting)
                    continue;

                if (!rows.TryGetValue(name, out var row))
                {
                    row = new SymbolPnl
                    {
                        Symbol = name,
                        UnrealizedPnl = 0,
                        UnrealizedChange = 0,
                        Pnl = 0,
                    };
                    rows[name] = row;
                }

                row.RealizedPnl += realized;
                row.Fees += fees;
                row.Trades += trades.Count;
                row.Wins += wins;
                row.Losses += losses;
                row.Qty += qty;
                row.MarketValue += qty <= 0 ? 0 : endSnap!.Mark is decimal price ? qty * price : endSnap.Cost;
                row.ValuedAtCost |= endSnap?.ValuedAtCost ?? false;
                row.UnrealizedPnl = Add(row.UnrealizedPnl, endUnrealized);
                row.UnrealizedChange = Add(row.UnrealizedChange, change);
                row.Pnl = Add(row.Pnl, change is decimal c ? realized + c : null);
            }
        }

        var ordered = rows.Values
            .OrderBy(x => x.Pnl is null)
            .ThenByDescending(x => x.Pnl ?? decimal.MinValue)
            .ThenBy(x => x.Symbol, StringComparer.Ordinal)
            .ToList();
        if (take is int limit)
            return ordered.Take(limit).ToList();
        return ordered;
    }

    public static List<PositionView> Positions(
        IReadOnlyList<WalletBook> books,
        DateTime asOf,
        string? symbol,
        string? status)
    {
        var rows = new List<PositionView>();
        foreach (var book in books)
        {
            var end = book.Replay.States.LastOrDefault();
            if (end == null)
                continue;
            foreach (var snap in end.Symbols.Values)
            {
                if (symbol != null && snap.Symbol != symbol)
                    continue;
                var open = snap.Qty > 0;
                var rowStatus = open ? PnlTerms.Open : PnlTerms.Closed;
                if (status != null && rowStatus != status)
                    continue;

                decimal? target = null;
                if (open)
                {
                    foreach (var lot in book.Replay.LotTrades
                        .Where(x => x.Symbol == snap.Symbol && x.Side == TradeSide.Buy && x.RemainingQty > 0)
                        .OrderByDescending(x => x.ExecutedAt)
                        .ThenByDescending(x => x.TradeId))
                    {
                        var trade = book.Trades.FirstOrDefault(x => x.Id == lot.TradeId);
                        if (trade?.TargetPrice is decimal chosen)
                        {
                            target = chosen;
                            break;
                        }
                    }
                }

                decimal? distance = null;
                decimal? distancePercent = null;
                if (target is decimal targetPrice && snap.Mark is decimal mark && mark != 0)
                {
                    distance = targetPrice - mark;
                    distancePercent = distance / mark * 100m;
                }

                var stale = snap.MarkAt is DateTime markAt && asOf - markAt > TimeSpan.FromMinutes(MarkStaleMinutes);
                rows.Add(new PositionView
                {
                    Wallet = book.Wallet.Key,
                    WalletName = book.Wallet.Name,
                    Mode = book.Wallet.Mode,
                    Symbol = snap.Symbol,
                    Status = rowStatus,
                    Qty = snap.Qty,
                    AvgPrice = snap.Qty == 0 ? 0 : snap.Cost / snap.Qty,
                    CostBasis = snap.Cost,
                    LastMarkPrice = snap.Mark,
                    LastMarkAt = snap.MarkAt,
                    MarkIsStale = stale,
                    MarkStaleAfterMinutes = MarkStaleMinutes,
                    UnrealizedPnl = open ? snap.Unrealized : 0,
                    UnrealizedPnlPercent = open && snap.Unrealized is decimal pnl && snap.Cost != 0
                        ? pnl / snap.Cost * 100m
                        : null,
                    RealizedPnl = snap.Realized,
                    FeesPaid = snap.Fees,
                    MarketValue = !open ? 0 : snap.Mark is decimal marked ? snap.Qty * marked : snap.Cost,
                    ValuedAtCost = snap.ValuedAtCost,
                    TargetPrice = target,
                    DistanceToTarget = distance,
                    DistanceToTargetPercent = distancePercent,
                });
            }
        }

        return rows
            .OrderBy(x => x.Status == PnlTerms.Closed)
            .ThenBy(x => x.Wallet, StringComparer.Ordinal)
            .ThenBy(x => x.Symbol, StringComparer.Ordinal)
            .ToList();
    }

    static bool TryBounds(LedgerReplay replay, DateTime? from, DateTime? to, out DateTime first, out DateTime last)
    {
        DateTime? dataFirst = replay.States.Count == 0 ? null : UtcDate(replay.States[0].At);
        DateTime? dataLast = replay.States.Count == 0 ? null : UtcDate(replay.States[^1].At);
        if (from == null && to == null)
        {
            if (dataFirst == null)
            {
                first = last = default;
                return false;
            }

            first = dataFirst.Value;
            last = dataLast!.Value;
            return true;
        }

        first = from is DateTime start ? UtcDate(start) : dataFirst ?? UtcDate(to!.Value);
        last = to is DateTime end ? UtcDate(end) : dataLast ?? first;
        if (to == null && dataLast is DateTime known && known > last)
            last = known;
        if (from == null && dataFirst is DateTime earliest && earliest < first)
            first = earliest;
        if (last < first)
            return false;
        if ((last - first).TotalDays > MaxDays)
            throw new HttpError(400, "InvalidRequest", "Date range cannot exceed 10 years.");
        return true;
    }

    static EquityPoint ToPoint(DateTime at, LedgerState? state)
    {
        if (state == null)
            return new EquityPoint { At = at, UnrealizedPnl = 0 };
        return new EquityPoint
        {
            At = at,
            TotalValue = state.TotalValue,
            Cash = state.Cash,
            PositionsValue = state.PositionsValue,
            OpenExposure = state.MarkedValue,
            UnmarkedCost = state.UnmarkedCost,
            Swept = state.Swept,
            UnrealizedPnl = state.UnrealizedPnl,
            ValuedAtCost = state.ValuedAtCost,
        };
    }

    static PnlDayResult Copy(PnlDayResult day) => new()
    {
        Date = day.Date,
        RealizedPnl = day.RealizedPnl,
        UnrealizedChange = day.UnrealizedChange,
        Pnl = day.Pnl,
        Fees = day.Fees,
        Deposits = day.Deposits,
        Withdrawals = day.Withdrawals,
        Sweeps = day.Sweeps,
        TotalValue = day.TotalValue,
        ValueChange = day.ValueChange,
        Cash = day.Cash,
        PositionsValue = day.PositionsValue,
        OpenExposure = day.OpenExposure,
        UnmarkedCost = day.UnmarkedCost,
        ValuedAtCost = day.ValuedAtCost,
        Trades = day.Trades,
    };

    static SymbolSnap? Snap(LedgerState? state, string symbol)
    {
        if (state == null)
            return null;
        return state.Symbols.TryGetValue(symbol, out var snap) ? snap : null;
    }

    static LedgerState? LastBefore(IReadOnlyList<LedgerState> states, DateTime at)
    {
        LedgerState? found = null;
        foreach (var state in states)
        {
            if (state.At < at)
                found = state;
            else
                break;
        }

        return found;
    }

    static LedgerState? LastAtOrBefore(IReadOnlyList<LedgerState> states, DateTime at)
    {
        LedgerState? found = null;
        foreach (var state in states)
        {
            if (state.At <= at)
                found = state;
            else
                break;
        }

        return found;
    }

    static decimal Delta(IReadOnlyList<LedgerState> states, DateTime start, DateTime end, Func<LedgerState, decimal> pick)
    {
        decimal sum = 0;
        for (var i = 0; i < states.Count; i++)
        {
            var at = states[i].At;
            if (at < start)
                continue;
            if (at > end)
                break;
            var prev = i == 0 ? 0 : pick(states[i - 1]);
            sum += pick(states[i]) - prev;
        }

        return sum;
    }

    static int DeltaCount(IReadOnlyList<LedgerState> states, DateTime start, DateTime end)
    {
        var sum = 0;
        for (var i = 0; i < states.Count; i++)
        {
            var at = states[i].At;
            if (at < start)
                continue;
            if (at > end)
                break;
            var prev = i == 0 ? 0 : states[i - 1].TradeCount;
            sum += states[i].TradeCount - prev;
        }

        return sum;
    }

    static decimal? Add(decimal? left, decimal? right) =>
        left is decimal a && right is decimal b ? a + b : null;

    static decimal? Rate(int wins, int losses)
    {
        var decided = wins + losses;
        return decided == 0 ? null : wins / (decimal)decided * 100m;
    }

    static bool InRange(DateTime at, DateTime? from, DateTime? to) =>
        (from == null || at >= from) && (to == null || at <= to);

    public static DateTime UtcDate(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;
        return new DateTime(utc.Year, utc.Month, utc.Day, 0, 0, 0, DateTimeKind.Utc);
    }
}
