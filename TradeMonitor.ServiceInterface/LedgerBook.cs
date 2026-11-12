using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.ServiceInterface;

// Replays trades, transfers, and price marks into positions, balance snapshots, and P&L.
// Buy fees sit in lot cost. A sell's fee reduces proceeds.
// Sells match in the way stored on the sell:
//   Fifo (default, including every sell from before costing existed): oldest lots first.
//   Lot: Trade.LotId is the buy to consume. Other lots stay put.
//   Average: a proportional slice of every open lot of that symbol, so matched cost
//   equals quantity times the average open cost. Open cost includes remaining buy fees.
// The last share of a fifo or lot slice takes whatever cost remains so partial fills do not leave dust.
// On a time tie, a wallet-specific price mark wins over a global mark.
// An open position with no mark has unknown unrealized P&L and is valued at cost.
// Cash within 0.000000001 below zero is stored as zero so float dust does not leave a negative balance.
public static class LedgerBook
{
    public const decimal CashDust = 0.000000001m;
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
            if (transfer.Excluded)
                continue;
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
        var opens = new Dictionary<int, OpenTrade>();
        var snapshots = new List<BalanceSnapshot>(events.Count);
        var states = new List<LedgerState>(events.Count);
        decimal cash = 0;
        decimal swept = 0;
        decimal deposits = 0;
        decimal withdrawals = 0;
        decimal sweeps = 0;
        var tradeCount = 0;
        var hasDeposits = false;

        foreach (var ev in events)
        {
            if (ev.Trade is { } trade)
            {
                var sym = Symbol(symbols, trade.Symbol);
                if (trade.Side == TradeSide.Buy)
                    ApplyBuy(opens, sym, trade, ref cash);
                else
                {
                    if (!CanSell(sym, trade, out var available))
                    {
                        return new LedgerReplay
                        {
                            Oversold = true,
                            OversellSymbol = sym.Symbol,
                            OversellQty = trade.Qty,
                            OpenQty = available,
                        };
                    }

                    ApplySell(opens, sym, trade, ref cash);
                }

                tradeCount++;
            }
            else if (ev.Transfer is { } transfer)
            {
                switch (transfer.Kind)
                {
                    case TransferKind.Deposit:
                        cash += transfer.Amount;
                        deposits += transfer.Amount;
                        hasDeposits = true;
                        break;
                    case TransferKind.Withdrawal:
                        cash -= transfer.Amount;
                        withdrawals += transfer.Amount;
                        break;
                    case TransferKind.Sweep:
                        cash -= transfer.Amount;
                        sweeps += transfer.Amount;
                        swept += transfer.Amount;
                        break;
                }
            }
            else if (ev.Mark is { } mark)
            {
                Consider(markChoices, mark);
            }

            if (cash < 0m && cash >= -CashDust)
                cash = 0m;

            var snaps = SnapshotSymbols(symbols, markChoices);
            var positionsValue = 0m;
            var markedValue = 0m;
            var unmarkedCost = 0m;
            var openCost = 0m;
            decimal? unrealized = 0m;
            var valuedAtCost = false;
            var anyOpen = false;
            foreach (var snap in snaps.Values)
            {
                if (snap.Qty <= 0)
                    continue;
                anyOpen = true;
                openCost += snap.Cost;
                if (snap.Mark is decimal price)
                {
                    var market = snap.Qty * price;
                    positionsValue += market;
                    markedValue += market;
                }
                else
                {
                    positionsValue += snap.Cost;
                    unmarkedCost += snap.Cost;
                    valuedAtCost = true;
                    unrealized = null;
                }

                if (unrealized is decimal sum && snap.Unrealized is decimal row)
                    unrealized += row;
            }

            if (!anyOpen)
                unrealized = 0;

            states.Add(new LedgerState
            {
                At = ev.At,
                Cash = cash,
                PositionsValue = positionsValue,
                MarkedValue = markedValue,
                UnmarkedCost = unmarkedCost,
                TotalValue = cash + positionsValue,
                Swept = swept,
                OpenCost = openCost,
                UnrealizedPnl = unrealized,
                ValuedAtCost = valuedAtCost,
                RealizedPnl = symbols.Values.Sum(x => x.Realized),
                Fees = symbols.Values.Sum(x => x.Fees),
                Deposits = deposits,
                Withdrawals = withdrawals,
                Sweeps = sweeps,
                TradeCount = tradeCount,
                Symbols = snaps,
            });
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

        var finalValue = states.Count == 0 ? 0 : states[^1].PositionsValue;
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
            States = states,
            LotTrades = BuildTrades(opens, markChoices),
        };
    }

    static void ApplyBuy(Dictionary<int, OpenTrade> opens, Sym sym, Trade trade, ref decimal cash)
    {
        var cost = trade.Qty * trade.Price + trade.Fee;
        sym.Lots.Add(new Lot
        {
            TradeId = trade.Id,
            Price = trade.Price,
            Qty = trade.Qty,
            Cost = cost,
            Fee = trade.Fee,
        });
        sym.Fees += trade.Fee;
        cash -= cost;
        opens[trade.Id] = new OpenTrade
        {
            Trade = trade,
            Symbol = sym.Symbol,
            OriginalCost = cost,
            RemainingQty = trade.Qty,
            RemainingCost = cost,
            RemainingFee = trade.Fee,
        };
    }

    static bool CanSell(Sym sym, Trade trade, out decimal available)
    {
        if (trade.Costing == CostingMethod.Lot)
        {
            var lot = trade.LotId is int lotId ? sym.Lots.FirstOrDefault(x => x.TradeId == lotId) : null;
            available = lot?.Qty ?? 0;
            return lot != null && trade.Qty <= lot.Qty;
        }

        available = sym.OpenQty;
        return trade.Qty <= available;
    }

    static void ApplySell(Dictionary<int, OpenTrade> opens, Sym sym, Trade trade, ref decimal cash)
    {
        var slices = trade.Costing switch
        {
            CostingMethod.Average => ConsumeAverage(sym.Lots, opens, trade.Qty),
            CostingMethod.Lot => ConsumeLot(sym.Lots, opens, trade.LotId ?? 0, trade.Qty),
            _ => Consume(sym.Lots, opens, trade.Qty),
        };
        var sell = new OpenTrade
        {
            Trade = trade,
            Symbol = sym.Symbol,
        };
        decimal feeLeft = trade.Fee;
        decimal pnl = 0;
        for (var i = 0; i < slices.Count; i++)
        {
            var slice = slices[i];
            var sellFee = i == slices.Count - 1 ? feeLeft : trade.Fee * slice.Qty / trade.Qty;
            feeLeft -= sellFee;
            var proceeds = slice.Qty * trade.Price - sellFee;
            var slicePnl = proceeds - slice.Cost;
            pnl += slicePnl;
            sell.MatchedCost += slice.Cost;
            sell.Proceeds += proceeds;
            sell.Realized += slicePnl;
            sell.ClosedQty += slice.Qty;
            sell.BuyNotional += slice.Price * slice.Qty;
            var buy = opens[slice.BuyTradeId];
            buy.Proceeds += proceeds;
            buy.Realized += slicePnl;
            buy.ClosedQty += slice.Qty;
            buy.ExitNotional += trade.Price * slice.Qty;
            buy.MatchedCost += slice.Cost;
            var buyFill = new LotFill
            {
                TradeId = trade.Id,
                Side = TradeSide.Sell,
                Symbol = sym.Symbol,
                Qty = slice.Qty,
                Price = trade.Price,
                BuyFee = slice.BuyFee,
                SellFee = sellFee,
                Cost = slice.Cost,
                Proceeds = proceeds,
                RealizedPnl = slicePnl,
                ExecutedAt = trade.ExecutedAt,
            };
            var sellFill = new LotFill
            {
                TradeId = slice.BuyTradeId,
                Side = TradeSide.Buy,
                Symbol = sym.Symbol,
                Qty = slice.Qty,
                Price = slice.Price,
                BuyFee = slice.BuyFee,
                SellFee = sellFee,
                Cost = slice.Cost,
                Proceeds = proceeds,
                RealizedPnl = slicePnl,
                ExecutedAt = buy.Trade.ExecutedAt,
            };
            buy.Fills.Add(buyFill);
            sell.Fills.Add(sellFill);
        }

        sym.Realized += pnl;
        sym.Fees += trade.Fee;
        cash += trade.Qty * trade.Price - trade.Fee;
        opens[trade.Id] = sell;
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

    static Dictionary<string, SymbolSnap> SnapshotSymbols(
        Dictionary<string, Sym> symbols,
        Dictionary<string, MarkChoice> marks)
    {
        var snaps = new Dictionary<string, SymbolSnap>(symbols.Count, StringComparer.Ordinal);
        foreach (var sym in symbols.Values)
        {
            decimal? markPrice = null;
            DateTime? markAt = null;
            if (marks.TryGetValue(sym.Symbol, out var mark))
            {
                markPrice = mark.Price;
                markAt = mark.At;
            }

            decimal? unrealized;
            if (sym.OpenQty == 0)
                unrealized = 0;
            else if (markPrice is decimal price)
                unrealized = sym.OpenQty * price - sym.OpenCost;
            else
                unrealized = null;

            snaps[sym.Symbol] = new SymbolSnap
            {
                Symbol = sym.Symbol,
                Qty = sym.OpenQty,
                Cost = sym.OpenCost,
                Realized = sym.Realized,
                Fees = sym.Fees,
                Mark = markPrice,
                MarkAt = markAt,
                Unrealized = unrealized,
                ValuedAtCost = sym.OpenQty > 0 && markPrice is null,
            };
        }

        return snaps;
    }

    static List<Slice> Consume(List<Lot> lots, Dictionary<int, OpenTrade> opens, decimal qty)
    {
        var left = qty;
        var slices = new List<Slice>();
        foreach (var lot in lots)
        {
            if (left == 0)
                break;
            var take = lot.Qty <= left ? lot.Qty : left;
            slices.Add(Take(lot, opens[lot.TradeId], take));
            left -= take;
        }

        lots.RemoveAll(x => x.Qty == 0);
        return slices;
    }

    static List<Slice> ConsumeLot(List<Lot> lots, Dictionary<int, OpenTrade> opens, int lotId, decimal qty)
    {
        var lot = lots.First(x => x.TradeId == lotId);
        var slices = new List<Slice> { Take(lot, opens[lot.TradeId], qty) };
        lots.RemoveAll(x => x.Qty == 0);
        return slices;
    }

    // Removes qty/openQty of every lot. The last lot absorbs decimal remainder so matched cost
    // equals openCost * qty / openQty, which is quantity times the average open cost.
    static List<Slice> ConsumeAverage(List<Lot> lots, Dictionary<int, OpenTrade> opens, decimal qty)
    {
        var openQty = lots.Sum(x => x.Qty);
        var openCost = lots.Sum(x => x.Cost);
        var openFee = lots.Sum(x => x.Fee);
        var targetCost = openCost * qty / openQty;
        var targetFee = openFee * qty / openQty;
        var plan = new List<(Lot Lot, decimal Qty, decimal Cost, decimal Fee)>(lots.Count);
        foreach (var lot in lots)
            plan.Add((lot, lot.Qty * qty / openQty, lot.Cost * qty / openQty, lot.Fee * qty / openQty));

        var last = plan.Count - 1;
        var tail = plan[last];
        plan[last] = (
            tail.Lot,
            tail.Qty + (qty - plan.Sum(x => x.Qty)),
            tail.Cost + (targetCost - plan.Sum(x => x.Cost)),
            tail.Fee + (targetFee - plan.Sum(x => x.Fee)));

        var slices = new List<Slice>();
        foreach (var step in plan)
        {
            if (step.Qty == 0 && step.Cost == 0 && step.Fee == 0)
                continue;
            slices.Add(TakeExact(step.Lot, opens[step.Lot.TradeId], step.Qty, step.Cost, step.Fee));
        }

        lots.RemoveAll(x => x.Qty == 0);
        return slices;
    }

    static Slice Take(Lot lot, OpenTrade buy, decimal qty)
    {
        decimal cost;
        decimal fee;
        if (qty == lot.Qty)
        {
            cost = lot.Cost;
            fee = lot.Fee;
            lot.Qty = 0;
            lot.Cost = 0;
            lot.Fee = 0;
        }
        else
        {
            cost = lot.Cost * qty / lot.Qty;
            fee = lot.Fee * qty / lot.Qty;
            lot.Cost -= cost;
            lot.Fee -= fee;
            lot.Qty -= qty;
        }

        buy.RemainingQty -= qty;
        buy.RemainingCost -= cost;
        buy.RemainingFee -= fee;
        return new Slice
        {
            BuyTradeId = lot.TradeId,
            Qty = qty,
            Price = lot.Price,
            Cost = cost,
            BuyFee = fee,
        };
    }

    static Slice TakeExact(Lot lot, OpenTrade buy, decimal qty, decimal cost, decimal fee)
    {
        if (qty > lot.Qty)
            qty = lot.Qty;
        if (cost > lot.Cost)
            cost = lot.Cost;
        if (fee > lot.Fee)
            fee = lot.Fee;
        lot.Qty -= qty;
        lot.Cost -= cost;
        lot.Fee -= fee;
        if (lot.Qty == 0)
        {
            cost += lot.Cost;
            fee += lot.Fee;
            lot.Cost = 0;
            lot.Fee = 0;
        }

        buy.RemainingQty -= qty;
        buy.RemainingCost -= cost;
        buy.RemainingFee -= fee;
        return new Slice
        {
            BuyTradeId = lot.TradeId,
            Qty = qty,
            Price = lot.Price,
            Cost = cost,
            BuyFee = fee,
        };
    }

    static List<LotTrade> BuildTrades(Dictionary<int, OpenTrade> opens, Dictionary<string, MarkChoice> marks)
    {
        var list = new List<LotTrade>(opens.Count);
        foreach (var open in opens.Values.OrderBy(x => x.Trade.ExecutedAt).ThenBy(x => x.Trade.Id))
            list.Add(ToLot(open, marks));
        return list;
    }

    static LotTrade ToLot(OpenTrade open, Dictionary<string, MarkChoice> marks)
    {
        marks.TryGetValue(open.Symbol, out var mark);
        var trade = open.Trade;
        var remaining = open.RemainingQty;
        decimal? exit = open.ClosedQty == 0 ? null : open.ExitNotional / open.ClosedQty;
        if (trade.Side == TradeSide.Sell)
            exit = trade.Price;

        string kind;
        decimal? current;
        decimal? unrealized;
        decimal market;
        var valued = false;
        if (remaining > 0 && mark == null)
        {
            kind = PnlTerms.Unknown;
            current = null;
            unrealized = null;
            market = open.RemainingCost;
            valued = true;
        }
        else if (remaining > 0 && mark != null)
        {
            kind = PnlTerms.Mark;
            current = mark.Price;
            unrealized = remaining * mark.Price - open.RemainingCost;
            market = remaining * mark.Price;
        }
        else
        {
            kind = PnlTerms.Exit;
            current = exit;
            unrealized = 0;
            market = 0;
        }

        var realized = open.Realized;
        decimal? pnl = unrealized is decimal marked ? realized + marked : null;
        var cost = trade.Side == TradeSide.Sell ? open.MatchedCost : open.OriginalCost;
        var buyPrice = trade.Side == TradeSide.Buy || open.ClosedQty == 0
            ? trade.Price
            : open.BuyNotional / open.ClosedQty;
        return new LotTrade
        {
            TradeId = trade.Id,
            Symbol = open.Symbol,
            Side = trade.Side,
            Qty = trade.Qty,
            Price = trade.Price,
            Fee = trade.Fee,
            ExecutedAt = trade.ExecutedAt,
            BuyPrice = buyPrice,
            CurrentOrExitPrice = current,
            PriceKind = kind,
            Status = remaining > 0 ? PnlTerms.Open : PnlTerms.Closed,
            MatchedCost = open.MatchedCost,
            Proceeds = open.Proceeds,
            RealizedPnl = realized,
            UnrealizedPnl = unrealized,
            Pnl = pnl,
            PnlPercent = pnl is decimal value && cost != 0 ? value / cost * 100m : null,
            RemainingQty = remaining,
            RemainingCost = open.RemainingCost,
            ExitPrice = exit,
            LastMarkPrice = mark?.Price,
            LastMarkAt = mark?.At,
            ValuedAtCost = valued,
            MarketValue = market,
            Fills = open.Fills,
        };
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
        public int TradeId { get; init; }
        public decimal Price { get; init; }
        public decimal Qty { get; set; }
        public decimal Cost { get; set; }
        public decimal Fee { get; set; }
    }

    sealed class Slice
    {
        public int BuyTradeId { get; init; }
        public decimal Qty { get; init; }
        public decimal Price { get; init; }
        public decimal Cost { get; init; }
        public decimal BuyFee { get; init; }
    }

    sealed class OpenTrade
    {
        public required Trade Trade { get; init; }
        public string Symbol { get; init; } = "";
        public decimal OriginalCost { get; init; }
        public decimal RemainingQty { get; set; }
        public decimal RemainingCost { get; set; }
        public decimal RemainingFee { get; set; }
        public decimal MatchedCost { get; set; }
        public decimal Proceeds { get; set; }
        public decimal Realized { get; set; }
        public decimal ClosedQty { get; set; }
        public decimal BuyNotional { get; set; }
        public decimal ExitNotional { get; set; }
        public List<LotFill> Fills { get; } = [];
    }

    sealed class MarkChoice
    {
        public decimal Price { get; init; }
        public DateTime At { get; init; }
        public int Id { get; init; }
        public bool WalletSpecific { get; init; }
    }
}

public static class PnlTerms
{
    public const string Exit = "exit";
    public const string Mark = "mark";
    public const string Unknown = "unknown";
    public const string Open = "open";
    public const string Closed = "closed";
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
    public List<LedgerState> States { get; init; } = [];
    public List<LotTrade> LotTrades { get; init; } = [];
}

public sealed class LedgerState
{
    public DateTime At { get; init; }
    public decimal Cash { get; init; }
    public decimal PositionsValue { get; init; }
    public decimal MarkedValue { get; init; }
    public decimal UnmarkedCost { get; init; }
    public decimal TotalValue { get; init; }
    public decimal Swept { get; init; }
    public decimal OpenCost { get; init; }
    public decimal? UnrealizedPnl { get; init; }
    public bool ValuedAtCost { get; init; }
    public decimal RealizedPnl { get; init; }
    public decimal Fees { get; init; }
    public decimal Deposits { get; init; }
    public decimal Withdrawals { get; init; }
    public decimal Sweeps { get; init; }
    public int TradeCount { get; init; }
    public Dictionary<string, SymbolSnap> Symbols { get; init; } = new(StringComparer.Ordinal);
}

public sealed class SymbolSnap
{
    public string Symbol { get; init; } = "";
    public decimal Qty { get; init; }
    public decimal Cost { get; init; }
    public decimal Realized { get; init; }
    public decimal Fees { get; init; }
    public decimal? Mark { get; init; }
    public DateTime? MarkAt { get; init; }
    public decimal? Unrealized { get; init; }
    public bool ValuedAtCost { get; init; }
}

public sealed class LotTrade
{
    public int TradeId { get; init; }
    public string Symbol { get; init; } = "";
    public TradeSide Side { get; init; }
    public decimal Qty { get; init; }
    public decimal Price { get; init; }
    public decimal Fee { get; init; }
    public DateTime ExecutedAt { get; init; }
    public decimal BuyPrice { get; init; }
    public decimal? CurrentOrExitPrice { get; init; }
    public string PriceKind { get; init; } = "";
    public string Status { get; init; } = "";
    public decimal MatchedCost { get; init; }
    public decimal Proceeds { get; init; }
    public decimal RealizedPnl { get; init; }
    public decimal? UnrealizedPnl { get; init; }
    public decimal? Pnl { get; init; }
    public decimal? PnlPercent { get; init; }
    public decimal RemainingQty { get; init; }
    public decimal RemainingCost { get; init; }
    public decimal? ExitPrice { get; init; }
    public decimal? LastMarkPrice { get; init; }
    public DateTime? LastMarkAt { get; init; }
    public bool ValuedAtCost { get; init; }
    public decimal MarketValue { get; init; }
    public List<LotFill> Fills { get; init; } = [];
}

public sealed class LotFill
{
    public int TradeId { get; init; }
    public TradeSide Side { get; init; }
    public string Symbol { get; init; } = "";
    public decimal Qty { get; init; }
    public decimal Price { get; init; }
    public decimal BuyFee { get; init; }
    public decimal SellFee { get; init; }
    public decimal Cost { get; init; }
    public decimal Proceeds { get; init; }
    public decimal RealizedPnl { get; init; }
    public DateTime ExecutedAt { get; init; }
}
