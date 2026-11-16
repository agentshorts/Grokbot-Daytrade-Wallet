using System.ComponentModel;
using ServiceStack;

namespace TradeMonitor.ServiceModel;

[Api("Prediction products Coinbase sync has seen, and whether each one was booked.")]
[Tag("pnl")]
[ValidateIsAdmin]
[Route("/api/predictions/booking", "GET")]
public class GetPredictionBookings : IGet, IReturn<GetPredictionBookingsResponse>
{
}

public class GetPredictionBookingsResponse
{
    [Description("Off, DryRun, or On. DryRun lists fills that would be booked and writes nothing.")]
    public string Mode { get; set; } = "DryRun";

    public List<PredictionBookingRow> Results { get; set; } = [];

    public ResponseStatus? ResponseStatus { get; set; }
}

public class PredictionBookingRow
{
    public string ProductId { get; set; } = "";

    public DateTime FirstSeen { get; set; }

    public DateTime LastSeen { get; set; }

    [Description("booked, would-book, problem, or seen.")]
    public string Status { get; set; } = "";

    [Description("pm- wallet that books this product, including an archived wallet or a -2 copy.")]
    public string? Wallet { get; set; }

    [Description("Why the latest fill was not booked, or the fill a dry run would book.")]
    public string? Detail { get; set; }
}
