using TradeMonitor.ServiceModel;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.ServiceInterface;

// Open buy lots and the average-cost quote. Profit is remaining quantity times price, minus remaining cost.
// Remaining cost includes buy fees still on the lot, which is the same cost the ledger uses.
public static class LotQuote
{
    public static (List<LiveLot> Lots, LiveAverageQuote Average) Build(
        IEnumerable<LotTrade> trades,
        string symbol,
        decimal? price,
        decimal? sellQty)
    {
        var rows = new List<LiveLot>();
        foreach (var trade in trades)
        {
            if (trade.Side != TradeSide.Buy || trade.RemainingQty <= 0)
                continue;
            if (!string.Equals(trade.Symbol, symbol, StringComparison.Ordinal))
                continue;
            decimal? profit = price is decimal mark ? trade.RemainingQty * mark - trade.RemainingCost : null;
            decimal? percent = profit is decimal pnl && trade.RemainingCost != 0 ? pnl / trade.RemainingCost * 100m : null;
            rows.Add(new LiveLot
            {
                TradeId = trade.TradeId,
                Symbol = trade.Symbol,
                Qty = trade.RemainingQty,
                BuyPrice = trade.BuyPrice,
                Cost = trade.RemainingCost,
                Price = price,
                UnrealizedPnl = profit,
                UnrealizedPnlPercent = percent,
                ExecutedAt = trade.ExecutedAt,
            });
        }

        rows.Sort((a, b) =>
        {
            var left = a.UnrealizedPnl ?? decimal.MinValue;
            var right = b.UnrealizedPnl ?? decimal.MinValue;
            var profit = right.CompareTo(left);
            if (profit != 0)
                return profit;
            var time = a.ExecutedAt.CompareTo(b.ExecutedAt);
            if (time != 0)
                return time;
            return a.TradeId.CompareTo(b.TradeId);
        });

        var openQty = rows.Sum(x => x.Qty);
        var openCost = rows.Sum(x => x.Cost);
        var qty = sellQty ?? openQty;
        if (qty < 0)
            qty = 0;
        var matched = openQty == 0 || qty == 0 ? 0 : openCost * qty / openQty;
        decimal? averageProfit = price is decimal at ? qty * at - matched : null;
        return (rows, new LiveAverageQuote
        {
            OpenQty = openQty,
            OpenCost = openCost,
            AvgCost = openQty == 0 ? 0 : openCost / openQty,
            SellQty = qty,
            Price = price,
            MatchedCost = matched,
            Profit = averageProfit,
        });
    }
}
