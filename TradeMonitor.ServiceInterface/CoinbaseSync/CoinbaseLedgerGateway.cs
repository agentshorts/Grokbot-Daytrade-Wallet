using ServiceStack;
using ServiceStack.Data;
using ServiceStack.OrmLite;
using TradeMonitor.ServiceModel;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.ServiceInterface;

public sealed class LedgerToolException(string message) : Exception(message);

public sealed class LedgerUnavailableException(string message) : Exception(message);

public sealed class CoinbaseLedgerSnapshot
{
    public Dictionary<string, Wallet> Wallets { get; init; } = new(StringComparer.Ordinal);

    public HashSet<string> KnownFillIds { get; init; } = new(StringComparer.Ordinal);

    public DateTime? Cursor { get; init; }
}

public interface ILedgerGateway
{
    CoinbaseLedgerSnapshot Load();

    WalletBalances Balances(string wallet);

    Wallet CreateWallet(string key, string name, string note);

    void RecordTrade(RecordTrade request);

    void RecordTransfer(RecordTransfer request);

    void RecordMarks(IReadOnlyList<PriceMarkWrite> marks);
}

public sealed class CoinbaseLedgerGateway(IDbConnectionFactory dbFactory, ILedgerClock clock) : ILedgerGateway
{
    readonly LedgerServices services = new(dbFactory, clock);

    public CoinbaseLedgerSnapshot Load()
    {
        try
        {
            using var db = dbFactory.Open();
            var wallets = db.Select<Wallet>().ToDictionary(wallet => wallet.Key, StringComparer.Ordinal);
            var coinbaseIds = new HashSet<int>(wallets.Values
                .Where(wallet => wallet.Key.StartsWith("coinbase-", StringComparison.Ordinal))
                .Select(wallet => wallet.Id));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            DateTime? latest = null;
            foreach (var trade in db.Select<Trade>(trade => trade.ClientId.StartsWith("cb-fill-")))
            {
                if (!coinbaseIds.Contains(trade.WalletId))
                    continue;
                ids.Add(trade.ClientId);
                var at = trade.ExecutedAt.Kind == DateTimeKind.Utc
                    ? trade.ExecutedAt
                    : DateTime.SpecifyKind(trade.ExecutedAt, DateTimeKind.Utc);
                if (latest == null || at > latest)
                    latest = at;
            }
            return new CoinbaseLedgerSnapshot
            {
                Wallets = wallets,
                KnownFillIds = ids,
                Cursor = latest,
            };
        }
        catch (Exception ex) when (IsUnavailable(ex))
        {
            throw new LedgerUnavailableException(ex.GetType().Name);
        }
    }

    public WalletBalances Balances(string wallet)
    {
        var response = Invoke(() => (GetWalletBalancesResponse)services.Any(new GetWalletBalances { Wallet = wallet }));
        return response.Result;
    }

    public Wallet CreateWallet(string key, string name, string note)
    {
        var response = Invoke(() => (CreateWalletResponse)services.Any(new CreateWallet
        {
            Key = key,
            Name = name,
            Mode = WalletMode.Real,
            Venue = "Coinbase",
            BaseCurrency = "USD",
            Note = note,
        }));
        return response.Result;
    }

    public void RecordTrade(RecordTrade request) => _ = Invoke(() => services.Any(request));

    public void RecordTransfer(RecordTransfer request) => _ = Invoke(() => services.Any(request));

    public void RecordMarks(IReadOnlyList<PriceMarkWrite> marks) =>
        _ = Invoke(() => services.Any(new RecordPriceMarks { Marks = marks.ToList() }));

    static T Invoke<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (HttpError ex)
        {
            throw new LedgerToolException(ex.Message);
        }
        catch (Exception ex) when (IsUnavailable(ex))
        {
            throw new LedgerUnavailableException(ex.GetType().Name);
        }
    }

    static bool IsUnavailable(Exception ex)
    {
        if (ex is HttpError or LedgerToolException or LedgerUnavailableException)
            return false;
        var name = ex.GetType().Name;
        return name.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)
            || ex is IOException
            || ex is TimeoutException;
    }
}
