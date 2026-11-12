using TradeMonitor.ServiceModel;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.ServiceInterface;

public static class LiveBooks
{
    public static LiveBookResponse Describe(
        Wallet wallet,
        bool simulated,
        string? simulationId,
        string symbol,
        decimal? price,
        LedgerReplay replay,
        IReadOnlyList<LiveOrder> orders,
        decimal? sellQty,
        int? tradeId)
    {
        var (lots, average) = LotQuote.Build(replay.LotTrades, symbol, price, sellQty);
        var holding = LiveLots.Summarize(replay.LotTrades, symbol, price);
        return new LiveBookResponse
        {
            Wallet = wallet.Key,
            WalletName = wallet.Name,
            Mode = wallet.Mode,
            Simulated = simulated,
            SimulationId = simulationId,
            Symbol = symbol,
            Price = price,
            Cash = replay.Cash,
            RealizedPnl = replay.RealizedPnl,
            Lots = lots,
            ClosedLots = holding.Closed,
            Settlements = holding.Settlements,
            Totals = holding.Totals,
            Average = average,
            Orders = orders.Where(x => x.Status == LiveOrderStatus.Open && string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase)).ToList(),
            TradeId = tradeId,
        };
    }

    public static LiveOrder ToOrder(PaperOrder order) => new()
    {
        Id = order.Id,
        Symbol = order.Symbol,
        Side = order.Side,
        Type = order.Type,
        Qty = order.Qty,
        TriggerPrice = order.TriggerPrice,
        Costing = order.Costing,
        LotId = order.LotId,
        Status = order.Status,
        LastError = order.LastError,
        CreatedAt = order.CreatedAt,
    };
}
