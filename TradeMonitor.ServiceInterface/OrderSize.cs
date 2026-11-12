namespace TradeMonitor.ServiceInterface;

public readonly record struct ProductSteps(decimal BaseIncrement, decimal QuoteIncrement, decimal BaseMinSize, decimal MinMarketFunds);

public readonly record struct SpendQuote(decimal Qty, decimal Spend, decimal Cost, string? Error);

public static class OrderSize
{
    public static decimal FloorTo(decimal value, decimal increment)
    {
        if (value <= 0)
            return 0;
        if (increment <= 0)
            return value;
        return decimal.Floor(value / increment) * increment;
    }

    public static SpendQuote FromSpend(decimal spend, decimal price, decimal cash, ProductSteps steps)
    {
        if (price <= 0)
            return new SpendQuote(0, 0, 0, "Price must be greater than zero.");
        var capped = spend > cash ? cash : spend;
        if (capped < 0)
            capped = 0;
        var spent = FloorTo(capped, steps.QuoteIncrement);
        var qty = FloorTo(spent / price, steps.BaseIncrement);
        var cost = qty * price;
        if (steps.MinMarketFunds > 0 && spent < steps.MinMarketFunds)
            return new SpendQuote(qty, spent, cost, $"Spend is below the {steps.MinMarketFunds} USD minimum.");
        if (steps.BaseMinSize > 0 && qty < steps.BaseMinSize)
            return new SpendQuote(qty, spent, cost, $"Quantity is below the minimum size of {steps.BaseMinSize}.");
        if (qty <= 0)
            return new SpendQuote(0, spent, 0, "Quantity must be greater than zero.");
        return new SpendQuote(qty, spent, cost, null);
    }

    public static SpendQuote FitQty(decimal qty, decimal price, ProductSteps steps)
    {
        var rounded = FloorTo(qty, steps.BaseIncrement);
        var cost = price > 0 ? rounded * price : 0;
        if (steps.BaseMinSize > 0 && rounded < steps.BaseMinSize)
            return new SpendQuote(rounded, cost, cost, $"Quantity is below the minimum size of {steps.BaseMinSize}.");
        if (price > 0 && steps.MinMarketFunds > 0 && cost < steps.MinMarketFunds)
            return new SpendQuote(rounded, cost, cost, $"Order is below the {steps.MinMarketFunds} USD minimum.");
        if (rounded <= 0)
            return new SpendQuote(0, 0, 0, "Quantity must be greater than zero.");
        return new SpendQuote(rounded, cost, cost, null);
    }
}
