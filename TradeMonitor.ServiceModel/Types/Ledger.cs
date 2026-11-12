using ServiceStack.DataAnnotations;

namespace TradeMonitor.ServiceModel.Types;

public enum WalletMode
{
    Paper,
    Real,
}

public enum TradeSide
{
    Buy,
    Sell,
}

public enum TradeKind
{
    Fill,
    Settlement,
}

public enum TransferKind
{
    Deposit,
    Withdrawal,
    Sweep,
}

public class Wallet
{
    [AutoIncrement]
    public int Id { get; set; }

    [Required]
    [Index(Unique = true)]
    [StringLength(128)]
    public string Key { get; set; } = "";

    [Required]
    [StringLength(200)]
    public string Name { get; set; } = "";

    public WalletMode Mode { get; set; }

    [StringLength(64)]
    public string? Venue { get; set; }

    [Required]
    [Default("'USD'")]
    [StringLength(16)]
    public string BaseCurrency { get; set; } = "USD";

    public string? Note { get; set; }

    [Default(0)]
    public bool IsArchived { get; set; }

    [Default(0)]
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }
}

[CompositeIndex(true, nameof(WalletId), nameof(ClientId))]
[CompositeIndex(nameof(WalletId), nameof(ExecutedAt))]
public class Trade
{
    [AutoIncrement]
    public int Id { get; set; }

    [ForeignKey(typeof(Wallet), OnDelete = "CASCADE")]
    public int WalletId { get; set; }

    [Required]
    [StringLength(128)]
    public string ClientId { get; set; } = "";

    [Required]
    [StringLength(64)]
    public string Symbol { get; set; } = "";

    [StringLength(200)]
    public string? Title { get; set; }

    public TradeSide Side { get; set; }

    // Existing rows are fills. A settlement is a contract that resolved, including a loss at price 0.
    [Default("'Fill'")]
    public TradeKind Kind { get; set; } = TradeKind.Fill;

    public decimal Qty { get; set; }

    public decimal Price { get; set; }

    public decimal Fee { get; set; }

    [StringLength(16)]
    public string? FeeCurrency { get; set; }

    public DateTime ExecutedAt { get; set; }

    [StringLength(128)]
    public string? OrderRef { get; set; }

    public decimal? TargetPrice { get; set; }

    public string? Note { get; set; }

    public DateTime RecordedAt { get; set; }
}

[CompositeIndex(true, nameof(WalletId), nameof(ClientId))]
[CompositeIndex(nameof(WalletId), nameof(At))]
public class Transfer
{
    [AutoIncrement]
    public int Id { get; set; }

    [ForeignKey(typeof(Wallet), OnDelete = "CASCADE")]
    public int WalletId { get; set; }

    [Required]
    [StringLength(128)]
    public string ClientId { get; set; } = "";

    public TransferKind Kind { get; set; }

    public decimal Amount { get; set; }

    [Required]
    [StringLength(16)]
    public string Currency { get; set; } = "";

    public DateTime At { get; set; }

    public string? Note { get; set; }

    public DateTime RecordedAt { get; set; }
}

[CompositeIndex(nameof(Symbol), nameof(At))]
public class PriceMark
{
    [AutoIncrement]
    public int Id { get; set; }

    // Absent on a mark the agent did not give an idempotency key. Present values are unique.
    [Index(Unique = true)]
    [StringLength(128)]
    public string? ClientId { get; set; }

    [Required]
    [StringLength(64)]
    public string Symbol { get; set; } = "";

    public decimal Price { get; set; }

    public DateTime At { get; set; }

    [StringLength(64)]
    public string? Source { get; set; }

    // Null applies the mark to every wallet. A value limits it to that wallet.
    [ForeignKey(typeof(Wallet), OnDelete = "SET NULL")]
    [Index]
    public int? WalletId { get; set; }

    public DateTime RecordedAt { get; set; }
}

[CompositeIndex(true, nameof(WalletId), nameof(Symbol))]
public class Position
{
    [AutoIncrement]
    public int Id { get; set; }

    [ForeignKey(typeof(Wallet), OnDelete = "CASCADE")]
    public int WalletId { get; set; }

    [Required]
    [StringLength(64)]
    public string Symbol { get; set; } = "";

    public decimal Qty { get; set; }

    public decimal CostBasis { get; set; }

    public decimal AvgPrice { get; set; }

    public decimal RealizedPnl { get; set; }

    public decimal FeesPaid { get; set; }

    public decimal? LastMarkPrice { get; set; }

    public DateTime? LastMarkAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

[CompositeIndex(nameof(WalletId), nameof(At))]
public class BalanceSnapshot
{
    [AutoIncrement]
    public int Id { get; set; }

    [ForeignKey(typeof(Wallet), OnDelete = "CASCADE")]
    public int WalletId { get; set; }

    public DateTime At { get; set; }

    public decimal Cash { get; set; }

    public decimal PositionsValue { get; set; }

    public decimal TotalValue { get; set; }

    public decimal Swept { get; set; }
}
