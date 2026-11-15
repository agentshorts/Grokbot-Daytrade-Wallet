namespace TradeMonitor.ServiceModel;

// Seeded once. A symbol that Coinbase does not list is kept in the table and hidden from the chart.
public static class TrackedSymbolDefaults
{
    public static readonly string[] Products =
    [
        "BTC-USD",
        "ETH-USD",
        "SOL-USD",
        "DOGE-USD",
        "LTC-USD",
        "POL-USD",
        "XRP-USD",
        "ADA-USD",
        "AVAX-USD",
        "LINK-USD",
    ];
}
