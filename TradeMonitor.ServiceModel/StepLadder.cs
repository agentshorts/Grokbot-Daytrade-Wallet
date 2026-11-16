using ServiceStack;

namespace TradeMonitor.ServiceModel;

[Api("Step ladder volume increases with dormant periods")]
[Tag("live")]
[ValidateIsAdmin]
[Route("/api/studies/step-ladder", "GET")]
public class GetStepLadder : IGet, IReturn<GetStepLadderResponse>
{
    public string? Symbol { get; set; }
    public string? Interval { get; set; }
    public decimal? StepIncrease { get; set; }
    public decimal? DormantThreshold { get; set; }
    public int? DormantPeriod { get; set; }
    public long? From { get; set; }
    public long? To { get; set; }
}

public class GetStepLadderResponse
{
    public string Symbol { get; set; } = "";
    public string Interval { get; set; } = "";
    public decimal StepIncrease { get; set; }
    public decimal DormantThreshold { get; set; }
    public int DormantPeriod { get; set; }
    public List<StepLadderBarDto> Bars { get; set; } = [];
    public ResponseStatus? ResponseStatus { get; set; }
}

public class StepLadderBarDto
{
    public long Time { get; set; }
    public decimal Volume { get; set; }
    public decimal? Vol24 { get; set; }
    public bool Step { get; set; }
    public bool Dormant { get; set; }
}
