using ServiceStack;

namespace TradeMonitor.ServiceModel;

[Api("TimesFM forecast range for the live chart")]
[Tag("live")]
[ValidateIsAdmin]
[Route("/api/forecast", "GET")]
public class GetForecast : IGet, IReturn<GetForecastResponse>
{
    public string? Symbol { get; set; }
    public string? Interval { get; set; }
    public int? Horizon { get; set; }
}

public class GetForecastResponse
{
    public string Symbol { get; set; } = "";
    public string Interval { get; set; } = "";
    public string Model { get; set; } = "TimesFM 2.5";
    public string Notice { get; set; } = "";
    public string Coverage { get; set; } = "";
    public long GeneratedAt { get; set; }
    public long CandleTime { get; set; }
    public int Horizon { get; set; }
    public List<ForecastStepDto> Steps { get; set; } = [];
    public ResponseStatus? ResponseStatus { get; set; }
}

public class ForecastStepDto
{
    public long At { get; set; }
    public decimal Mean { get; set; }
    public decimal Q10 { get; set; }
    public decimal Q30 { get; set; }
    public decimal Q50 { get; set; }
    public decimal Q70 { get; set; }
    public decimal Q90 { get; set; }
}
