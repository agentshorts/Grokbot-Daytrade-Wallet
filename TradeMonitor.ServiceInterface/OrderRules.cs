using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.ServiceInterface;

public static class OrderRules
{
    public static bool IsDue(LiveOrderType type, TradeSide side, decimal price, decimal? trigger)
    {
        if (trigger is not decimal level || level <= 0)
            return false;
        return type switch
        {
            LiveOrderType.Limit when side == TradeSide.Buy => price <= level,
            LiveOrderType.Limit when side == TradeSide.Sell => price >= level,
            LiveOrderType.StopLoss when side == TradeSide.Sell => price <= level,
            LiveOrderType.TakeProfit when side == TradeSide.Sell => price >= level,
            LiveOrderType.StopBuy when side == TradeSide.Buy => price >= level,
            _ => false,
        };
    }

    public static string? Validate(TradeSide side, LiveOrderType type, decimal qty, decimal? trigger, CostingMethod costing, int? lotId)
    {
        if (!Enum.IsDefined(side))
            return "Order side must be Buy or Sell.";
        if (!Enum.IsDefined(type))
            return "Order type must be market, limit, stop buy, stop-loss, or take-profit.";
        if (!Enum.IsDefined(costing))
            return "Costing must be fifo, lot, or average.";
        if (qty <= 0)
            return "Quantity must be greater than zero.";
        if (type == LiveOrderType.StopBuy && side != TradeSide.Buy)
            return "A stop buy is a buy.";
        if (type is LiveOrderType.StopLoss or LiveOrderType.TakeProfit && side != TradeSide.Sell)
            return "Stop-loss and take-profit orders are sells.";
        if (type == LiveOrderType.Market && trigger != null)
            return "A market order has no trigger price.";
        if (type != LiveOrderType.Market && trigger is null or <= 0)
            return "Trigger price must be greater than zero.";
        if (side == TradeSide.Buy && (costing != CostingMethod.Fifo || lotId != null))
            return "A buy cannot target a lot or an average.";
        if (costing == CostingMethod.Lot && lotId is null or <= 0)
            return "A lot sell needs the buy trade id.";
        if (costing != CostingMethod.Lot && lotId != null)
            return "Only a lot sell can target a buy trade id.";
        return null;
    }

    public static string TitleFor(LiveOrderType type, CostingMethod costing, int? lotId)
    {
        var target = costing switch
        {
            CostingMethod.Lot => $"lot {lotId}",
            CostingMethod.Average => "average",
            _ => null,
        };
        var name = type switch
        {
            LiveOrderType.Limit => "Limit",
            LiveOrderType.StopBuy => "Stop buy",
            LiveOrderType.StopLoss => "Stop-loss",
            LiveOrderType.TakeProfit => "Take-profit",
            _ => "Market",
        };
        return target == null ? name : $"{name} {target}";
    }
}
