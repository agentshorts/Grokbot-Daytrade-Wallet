using ServiceStack.DataAnnotations;

namespace TradeMonitor.ServiceModel.Types;

// One minute of public prices for a product. Older minutes are folded and then deleted.
[CompositeIndex(true, nameof(Symbol), nameof(Start))]
public class PriceBar
{
    [AutoIncrement]
    public int Id { get; set; }

    [Required]
    [StringLength(64)]
    public string Symbol { get; set; } = "";

    // Unix seconds, aligned to the start of the minute.
    public long Start { get; set; }

    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal Volume { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// USD products the live feed follows, in the order shown on the chart.
[CompositeIndex(true, nameof(ProductId))]
public class TrackedSymbol
{
    [AutoIncrement]
    public int Id { get; set; }

    [Required]
    [StringLength(64)]
    public string ProductId { get; set; } = "";

    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
}
