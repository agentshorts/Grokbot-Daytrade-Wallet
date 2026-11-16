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

    [Description("Latest price in cents.")]
    public decimal? PriceCents { get; set; }

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
