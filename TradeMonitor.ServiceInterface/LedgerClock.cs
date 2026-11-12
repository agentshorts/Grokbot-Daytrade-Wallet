namespace TradeMonitor.ServiceInterface;

public interface ILedgerClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemLedgerClock : ILedgerClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
