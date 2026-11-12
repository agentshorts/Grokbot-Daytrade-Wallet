using TradeMonitor.ServiceModel;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.ServiceInterface;

// Closed lots and totals from the ledger replay. Costing stays in LedgerBook.
// A sell at price 0 is a settlement and stays out of the crypto totals.
public static class LiveLots
{
    public static LiveHolding Summarize(IEnumerable<LotTrade> trades, string symbol, decimal? price)
    {
        var rows = trades
            .Where(x => x.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var closed = new List<LiveClosedLot>();
        var settlements = new List<LiveClosedLot>();
        foreach (var trade in rows.Where(x => x.Side == TradeSide.Sell).OrderBy(x => x.ExecutedAt).ThenBy(x => x.TradeId))
        {
            var row = new LiveClosedLot
            {
                TradeId = trade.TradeId,
                Qty = trade.Qty,
                BuyPrice = trade.BuyPrice,
                SellPrice = trade.Price,
                RealizedPnl = trade.RealizedPnl,
                ExecutedAt = trade.ExecutedAt,
                Settlement = trade.Price <= 0,
            };
            if (row.Settlement)
                settlements.Add(row);
            else
                closed.Add(row);
        }

        var open = rows.Where(x => x.Side == TradeSide.Buy && x.RemainingQty > 0).ToList();
        var qty = open.Sum(x => x.RemainingQty);
        var cost = open.Sum(x => x.RemainingCost);
        decimal? unrealized = price is decimal mark
            ? open.Sum(x => x.RemainingQty * mark - x.RemainingCost)
            : open.Any(x => x.UnrealizedPnl == null)
                ? null
                : open.Sum(x => x.UnrealizedPnl ?? 0);
        var realized = closed.Sum(x => x.RealizedPnl);
        return new LiveHolding(closed, settlements, new LiveLotTotals
        {
            Qty = qty,
            AverageCost = qty == 0 ? 0 : cost / qty,
            UnrealizedPnl = unrealized,
            RealizedPnl = realized,
            TotalPnl = unrealized is decimal marked ? realized + marked : null,
        });
    }
}

public sealed record LiveHolding(List<LiveClosedLot> Closed, List<LiveClosedLot> Settlements, LiveLotTotals Totals);
