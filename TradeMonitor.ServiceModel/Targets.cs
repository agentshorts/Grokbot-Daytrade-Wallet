using ServiceStack;

namespace TradeMonitor.ServiceModel;

[Api("Swing high and low levels for the live chart")]
[Tag("live")]
[ValidateIsAdmin]
[Route("/api/targets/swing", "GET")]
public class GetSwingTargets : IGet, IReturn<GetSwingTargetsResponse>
{
    public string? Symbol { get; set; }
    public string? Interval { get; set; }
    public int? Wing { get; set; }
    public int? Lookback { get; set; }
}

public class GetSwingTargetsResponse
{
    public string Symbol { get; set; } = "";
    public string Interval { get; set; } = "";
    public decimal Price { get; set; }
    public string Notice { get; set; } = "";
    public List<SwingLevelDto> Levels { get; set; } = [];
    public SwingNearestDto Nearest { get; set; } = new();
    public ResponseStatus? ResponseStatus { get; set; }
}

public class SwingNearestDto
{
    public List<SwingLevelDto> Above { get; set; } = [];
    public List<SwingLevelDto> Below { get; set; } = [];
}

public class SwingLevelDto
{
    public decimal Price { get; set; }
    public string Side { get; set; } = "";
    public int Strength { get; set; }
    public long FirstAt { get; set; }
    public long LastAt { get; set; }
}

[Api("ATR bands for the live chart")]
[Tag("live")]
[ValidateIsAdmin]
[Route("/api/targets/atr", "GET")]
public class GetAtrTargets : IGet, IReturn<GetAtrTargetsResponse>
{
    public string? Symbol { get; set; }
    public string? Interval { get; set; }
    public int? Period { get; set; }
    public int? HorizonBars { get; set; }
}

public class GetAtrTargetsResponse
{
    public string Symbol { get; set; } = "";
    public string Interval { get; set; } = "";
    public int Period { get; set; }
    public int HorizonBars { get; set; }
    public decimal Scale { get; set; }
    public decimal Price { get; set; }
    public decimal? Atr { get; set; }
    public string Notice { get; set; } = "";
    public List<AtrBandDto> Bands { get; set; } = [];
    public ResponseStatus? ResponseStatus { get; set; }
}

public class AtrBandDto
{
    public decimal Multiplier { get; set; }
    public decimal Upper { get; set; }
    public decimal Lower { get; set; }
}
