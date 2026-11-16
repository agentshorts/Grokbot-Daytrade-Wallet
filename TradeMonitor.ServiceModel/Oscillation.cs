using System.ComponentModel;
using ServiceStack;

namespace TradeMonitor.ServiceModel;

[Api("How far open prediction contracts have swung, and how often price reversed, over a lookback window. Read only.")]
[Tag("pnl")]
[ValidateIsAdmin]
[Route("/api/predictions/oscillation", "GET")]
public class GetPredictionOscillation : IGet, IReturn<GetPredictionOscillationResponse>
{
    [Description("Lookback window in hours. Defaults to 6. Values are clamped from 1 to 168.")]
    public decimal? LookbackHours { get; set; }

    [Description("Minimum reversal size in cents. A direction change counts when the swing back is at least this large. Defaults to 5.")]
    public decimal? ThresholdCents { get; set; }

    [Description("Optional ticker or Coinbase product id. Analyzed even when no wallet holds it.")]
    public string? Ticker { get; set; }
}

public class GetPredictionOscillationResponse
{
    public decimal LookbackHours { get; set; }
    public decimal ThresholdCents { get; set; }
    public List<PredictionOscillationRow> Results { get; set; } = [];
    public PredictionOscillationRow? Lookup { get; set; }
    public string? LookupError { get; set; }
    public ResponseStatus? ResponseStatus { get; set; }
}

public class PredictionOscillationRow
{
    public string Ticker { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string Series { get; set; } = "";
    public string? Wallet { get; set; }
    public string Name { get; set; } = "";
    public decimal Qty { get; set; }

    [Description("Latest price in cents. This is the YES price the history is built from.")]
    public decimal? PriceCents { get; set; }

    [Description("Current YES price in cents. Bid and ask are set when the source publishes a book.")]
    public decimal? YesCents { get; set; }

    public decimal? YesBidCents { get; set; }
    public decimal? YesAskCents { get; set; }

    [Description("last, bid/ask, close, or empty when the price is missing.")]
    public string YesLabel { get; set; } = "";

    [Description("Current NO price in cents.")]
    public decimal? NoCents { get; set; }

    public decimal? NoBidCents { get; set; }
    public decimal? NoAskCents { get; set; }

    [Description("bid/ask, bid, ask, implied, or empty. Implied means NO is 1 minus YES.")]
    public string NoLabel { get; set; } = "";

    public bool NoImplied { get; set; }

    [Description("Highest price in the window, in cents.")]
    public decimal? HighCents { get; set; }

    [Description("Lowest price in the window, in cents.")]
    public decimal? LowCents { get; set; }

    [Description("High minus low, in cents.")]
    public decimal RangeCents { get; set; }

    [Description("High minus low as a percent of the low. Null when the low is 0.")]
    public decimal? RangePercent { get; set; }

    public int Reversals { get; set; }

    [Description("True when price reversed direction at least twice by the threshold.")]
    public bool Oscillating { get; set; }

    [Description("Close prices in cents, oldest first, capped for a sparkline.")]
    public List<decimal> Sparkline { get; set; } = [];

    [Description("Contract close, UTC.")]
    public DateTime? CloseAt { get; set; }

    [Description("Contract close converted to America/Chicago. The ticker hour itself is Eastern.")]
    public string? CloseCentral { get; set; }

    [Description("coinbase, kalshi, or none.")]
    public string Source { get; set; } = "none";

    public string? Error { get; set; }
}

[Api("Price history, reversal marks, and an open pm- holding for one prediction contract. Read only.")]
[Tag("pnl")]
[ValidateIsAdmin]
[Route("/api/predictions/contract", "GET")]
public class GetPredictionContract : IGet, IReturn<GetPredictionContractResponse>
{
    [Description("Prediction ticker or Coinbase product id.")]
    public string? Ticker { get; set; }

    [Description("Lookback window in hours. Defaults to 6. Values are clamped from 1 to 168. Ignored when AllAvailable is true.")]
    public decimal? LookbackHours { get; set; }

    [Description("Minimum reversal size in cents. Defaults to 5.")]
    public decimal? ThresholdCents { get; set; }

    [Description("Use the longest window the public quotes allow, 168 hours.")]
    public bool? AllAvailable { get; set; }
}

public class GetPredictionContractResponse
{
    public PredictionContractDetail? Result { get; set; }
    public ResponseStatus? ResponseStatus { get; set; }
}

public class PredictionContractDetail
{
    public string Ticker { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string Series { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Subtitle { get; set; }
    public string? Underlying { get; set; }
    public decimal? Strike { get; set; }

    [Description("above, below, or empty.")]
    public string Direction { get; set; } = "";

    public string? Status { get; set; }
    public DateTime? CloseAt { get; set; }
    public string? CloseCentral { get; set; }
    public string? CloseEastern { get; set; }

    public decimal? YesCents { get; set; }
    public decimal? YesBidCents { get; set; }
    public decimal? YesAskCents { get; set; }
    public string YesLabel { get; set; } = "";
    public decimal? NoCents { get; set; }
    public decimal? NoBidCents { get; set; }
    public decimal? NoAskCents { get; set; }
    public string NoLabel { get; set; } = "";

    [Description("True when the current NO quote is 1 minus YES.")]
    public bool NoImplied { get; set; }

    [Description("True when the NO line on the chart is 1 minus the YES close. Public candlesticks publish YES.")]
    public bool NoLineImplied { get; set; } = true;

    public decimal? HighCents { get; set; }
    public decimal? LowCents { get; set; }
    public decimal RangeCents { get; set; }
    public decimal? RangePercent { get; set; }
    public int Reversals { get; set; }
    public bool Oscillating { get; set; }
    public decimal? Volume { get; set; }
    public decimal? OpenInterest { get; set; }
    public decimal LookbackHours { get; set; }
    public decimal ThresholdCents { get; set; }
    public bool AllAvailable { get; set; }

    [Description("coinbase, kalshi, or none.")]
    public string Source { get; set; } = "none";

    public string? Error { get; set; }
    public List<PredictionChartPoint> Points { get; set; } = [];
    public List<PredictionChartMark> Marks { get; set; } = [];
    public List<PredictionHolding> Holdings { get; set; } = [];
}

public class PredictionChartPoint
{
    public DateTime At { get; set; }
    public decimal YesCents { get; set; }
    public decimal NoCents { get; set; }
}

public class PredictionChartMark
{
    public DateTime At { get; set; }

    [Description("high, low, buy, or sell.")]
    public string Kind { get; set; } = "";

    public string Text { get; set; } = "";
}

public class PredictionHolding
{
    public string Wallet { get; set; } = "";
    public string Name { get; set; } = "";

    [Description("YES. An open pm- wallet is long that contract.")]
    public string Side { get; set; } = "YES";

    public decimal Qty { get; set; }

    [Description("Average open cost in dollars, fees included.")]
    public decimal AvgCost { get; set; }

    [Description("Open quantity times the live YES price, minus the open cost. Null when there is no live price.")]
    public decimal? UnrealizedPnl { get; set; }

    public List<PredictionFill> Fills { get; set; } = [];
}

public class PredictionFill
{
    public int TradeId { get; set; }
    public string Side { get; set; } = "buy";
    public decimal Qty { get; set; }
    public decimal Price { get; set; }
    public DateTime At { get; set; }
}
