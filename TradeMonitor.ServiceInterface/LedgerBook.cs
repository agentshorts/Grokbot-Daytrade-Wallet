using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.ServiceInterface;

// Replays trades, transfers, and price marks into positions and balance snapshots.
// Buy fees sit in lot cost. A sell's fee reduces proceeds. Lots match first in, first out.
// The last share of a lot takes whatever cost remains so partial fills do not leave dust.
// On a time tie, a wallet-specific price mark wins over a global mark.
public static class LedgerBook
{
    public static LedgerReplay Replay(
        int walletId,
        IEnumerable<Trade> trades,
        IEnumerable<Transfer> transfers,
        IEnumerable<PriceMark> marks,
        DateTime updatedAt)
    {
        var events = new List<Ev>();
        foreach (var trade in trades)
        {
            events.Add(new Ev
            {
                At = trade.ExecutedAt,
                Rank = trade.Side == TradeSide.Buy ? 3 : 4,
                Id = trade.Id,
                Trade = trade,
            });
        }

        foreach (var transfer in transfers)
        {
            events.Add(new Ev
            {
                At = transfer.At,
                Rank = Rank(transfer.Kind),
                Id = transfer.Id,
                Transfer = transfer,
            });
        }

        foreach (var mark in marks)
        {
            if (mark.WalletId != null && mark.WalletId != walletId)
                continue;
            events.Add(new Ev
            {
                At = mark.At,
                Rank = mark.WalletId == null ? 5 : 6,
                Id = mark.Id,
                Mark = mark,
            });
        }

        events.Sort(static (a, b) =>
        {
            var time = a.At.CompareTo(b.At);
            if (time != 0)
                return time;
            var rank = a.Rank.CompareTo(b.Rank);
            if (rank != 0)
                return rank;
            return a.Id.CompareTo(b.Id);
        });

        var symbols = new Dictionary<string, Sym>(StringComparer.Ordinal);
        var markChoices = new Dictionary<string, MarkChoice>(StringComparer.Ordinal);
        var snapshots = new List<BalanceSnapshot>(events.Count);
        decimal cash = 0;
        decimal swept = 0;
        var hasDeposits = false;

        foreach (var ev in events)
        {
            if (ev.Trade is { } trade)
            {
                var sym = Symbol(symbols, trade.Symbol);
                if (trade.Side == TradeSide.Buy)
                {
                    var cost = trade.Qty * trade.Price + trade.Fee;
                    sym.Lots.Add(new Lot { Qty = trade.Qty, Cost = cost });
                    sym.Fees += trade.Fee;
                    cash -= cost;
                }
                else
                {
                    var open = sym.OpenQty;
                    if (trade.Qty > open)
                    {
                        return new LedgerReplay
                        {
                            Oversold = true,
                            OversellSymbol = sym.Symbol,
                            OversellQty = trade.Qty,
                            OpenQty = open,
                        };
                    }

                    var matched = Consume(sym.Lots, trade.Qty);
                    var proceeds = trade.Qty * trade.Price - trade.Fee;
                    sym.Realized += proceeds - matched;
                    sym.Fees += trade.Fee;
                    cash += proceeds;
                }
            }
            else if (ev.Transfer is { } transfer)
            {
                switch (transfer.Kind)
                {
                    case TransferKind.Deposit:
                        cash += transfer.Amount;
                        hasDeposits = true;
                        break;
                    case TransferKind.Withdrawal:
                        cash -= transfer.Amount;
                        break;
                    case TransferKind.Sweep:
                        cash -= transfer.Amount;
                        swept += transfer.Amount;
                        break;
                }
            }
            else if (ev.Mark is { } mark)
            {
                Consider(markChoices, mark);
            }

            var positionsValue = MarketValue(symbols, markChoices);
            snapshots.Add(new BalanceSnapshot
            {
                WalletId = walletId,
                At = ev.At,
                Cash = cash,
                PositionsValue = positionsValue,
                TotalValue = cash + positionsValue,
                Swept = swept,
            });
        }

        var positions = new List<Position>(symbols.Count);
        foreach (var sym in symbols.Values.OrderBy(x => x.Symbol, StringComparer.Ordinal))
        {
            var qty = sym.OpenQty;
            var cost = sym.OpenCost;
            markChoices.TryGetValue(sym.Symbol, out var mark);
            positions.Add(new Position
            {
                WalletId = walletId,
                Symbol = sym.Symbol,
                Qty = qty,
                CostBasis = cost,
                AvgPrice = qty == 0 ? 0 : cost / qty,
                RealizedPnl = sym.Realized,
                FeesPaid = sym.Fees,
                LastMarkPrice = mark?.Price,
                LastMarkAt = mark?.At,
                UpdatedAt = updatedAt,
            });
        }

        var finalValue = MarketValue(symbols, markChoices);
        return new LedgerReplay
        {
            Cash = cash,
            Swept = swept,
            Fees = symbols.Values.Sum(x => x.Fees),
            RealizedPnl = symbols.Values.Sum(x => x.Realized),
            PositionsValue = finalValue,
            TotalValue = cash + finalValue,
            HasDeposits = hasDeposits,
            Positions = positions,
            Snapshots = snapshots,
        };
    }

    static int Rank(TransferKind kind) => kind switch
    {
        TransferKind.Deposit => 0,
        TransferKind.Withdrawal => 1,
        TransferKind.Sweep => 2,
        _ => 1,
    };

    static Sym Symbol(Dictionary<string, Sym> symbols, string symbol)
    {
        var key = symbol.Trim().ToUpperInvariant();
        if (!symbols.TryGetValue(key, out var sym))
        {
            sym = new Sym { Symbol = key };
            symbols.Add(key, sym);
        }

        return sym;
    }

    static void Consider(Dictionary<string, MarkChoice> marks, PriceMark mark)
    {
        var key = mark.Symbol.Trim().ToUpperInvariant();
        var next = new MarkChoice
        {
            Price = mark.Price,
            At = mark.At,
            Id = mark.Id,
            WalletSpecific = mark.WalletId != null,
        };
        if (!marks.TryGetValue(key, out var current) || Better(next, current))
            marks[key] = next;
    }

    static bool Better(MarkChoice next, MarkChoice current)
    {
        if (next.At > current.At)
            return true;
        if (next.At < current.At)
            return false;
        if (next.WalletSpecific && !current.WalletSpecific)
            return true;
        if (!next.WalletSpecific && current.WalletSpecific)
            return false;
        return next.Id > current.Id;
    }

    static decimal MarketValue(Dictionary<string, Sym> symbols, Dictionary<string, MarkChoice> marks)
    {
        decimal value = 0;
        foreach (var sym in symbols.Values)
        {
            if (sym.OpenQty == 0)
                continue;
            value += marks.TryGetValue(sym.Symbol, out var mark)
                ? sym.OpenQty * mark.Price
                : sym.OpenCost;
        }

        return value;
    }

    static decimal Consume(List<Lot> lots, decimal qty)
    {
        var left = qty;
        decimal matched = 0;
        foreach (var lot in lots)
        {
            if (left == 0)
                break;
            var take = lot.Qty <= left ? lot.Qty : left;
            matched += Take(lot, take);
            left -= take;
        }

        lots.RemoveAll(x => x.Qty == 0);
        return matched;
    }

    static decimal Take(Lot lot, decimal qty)
    {
        if (qty == lot.Qty)
        {
            var cost = lot.Cost;
            lot.Qty = 0;
            lot.Cost = 0;
            return cost;
        }

        var matched = lot.Cost * qty / lot.Qty;
        lot.Cost -= matched;
        lot.Qty -= qty;
        return matched;
    }

    sealed class Ev
    {
        public DateTime At { get; init; }
        public int Rank { get; init; }
        public int Id { get; init; }
        public Trade? Trade { get; init; }
        public Transfer? Transfer { get; init; }
        public PriceMark? Mark { get; init; }
    }

    sealed class Sym
    {
        public string Symbol { get; init; } = "";
        public List<Lot> Lots { get; } = [];
        public decimal Realized { get; set; }
        public decimal Fees { get; set; }
        public decimal OpenQty => Lots.Sum(x => x.Qty);
        public decimal OpenCost => Lots.Sum(x => x.Cost);
    }

    sealed class Lot
    {
        public decimal Qty { get; set; }
        public decimal Cost { get; set; }
    }

    sealed class MarkChoice
    {
        public decimal Price { get; init; }
        public DateTime At { get; init; }
        public int Id { get; init; }
        public bool WalletSpecific { get; init; }
    }
}

public sealed class LedgerReplay
{
    public bool Oversold { get; init; }
    public string? OversellSymbol { get; init; }
    public decimal OversellQty { get; init; }
    public decimal OpenQty { get; init; }
    public decimal Cash { get; init; }
    public decimal Swept { get; init; }
    public decimal Fees { get; init; }
    public decimal RealizedPnl { get; init; }
    public decimal PositionsValue { get; init; }
    public decimal TotalValue { get; init; }
    public bool HasDeposits { get; init; }
    public List<Position> Positions { get; init; } = [];
    public List<BalanceSnapshot> Snapshots { get; init; } = [];
}
