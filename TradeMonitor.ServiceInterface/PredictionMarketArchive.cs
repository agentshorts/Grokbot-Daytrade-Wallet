using ServiceStack.OrmLite;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.ServiceInterface;

public sealed class PredictionArchiveResult
{
    public List<string> Lines { get; } = [];
    public int Tagged { get; set; }
    public int Archived { get; set; }
    public int ExcludedTransfers { get; set; }
}

// Tags hand-logged pm-* wallets and archives sync-created prediction contract wallets.
// Rows stay in the database. Safe to run more than once. A second run changes nothing.
public static class PredictionMarketArchive
{
    public static PredictionArchiveResult Apply(System.Data.IDbConnection db)
    {
        var result = new PredictionArchiveResult();
        if (!db.TableExists<Wallet>())
            return result;

        var wallets = db.Select<Wallet>();
        var trades = db.TableExists<Trade>() ? db.Select<Trade>() : [];
        var transfers = db.TableExists<Transfer>() ? db.Select<Transfer>() : [];
        var marks = db.TableExists<PriceMark>() ? db.Select<PriceMark>() : [];

        foreach (var wallet in wallets.OrderBy(wallet => wallet.Key, StringComparer.Ordinal))
        {
            if (!PredictionMarkets.IsHandWallet(wallet.Key) || wallet.Kind == WalletKind.PredictionMarket)
                continue;
            wallet.Kind = WalletKind.PredictionMarket;
            db.Update(wallet);
            result.Tagged++;
            result.Lines.Add("tagged prediction wallet " + wallet.Key);
        }

        var cash = wallets.FirstOrDefault(wallet => wallet.Key == CoinbaseWallets.Cash);
        foreach (var wallet in wallets.OrderBy(wallet => wallet.Key, StringComparer.Ordinal))
        {
            if (!ShouldArchive(wallet, trades))
                continue;

            var mine = trades.Where(trade => trade.WalletId == wallet.Id).ToList();
            if (!wallet.IsArchived)
            {
                wallet.IsArchived = true;
                db.Update(wallet);
                result.Archived++;
                var transferCount = transfers.Count(transfer => transfer.WalletId == wallet.Id);
                var markCount = marks.Count(mark => mark.WalletId == wallet.Id);
                result.Lines.Add("archived prediction wallet " + wallet.Key
                    + " (" + mine.Count + " trades, " + transferCount + " transfers, " + markCount + " marks)");
            }

            if (cash == null)
                continue;
            foreach (var trade in mine.OrderBy(trade => trade.ClientId, StringComparer.Ordinal))
            {
                if (!trade.ClientId.StartsWith(CoinbaseWallets.FillPrefix, StringComparison.Ordinal))
                    continue;
                var tradeId = trade.ClientId[CoinbaseWallets.FillPrefix.Length..];
                ExcludeSweep(db, result, transfers, cash.Id, CoinbaseWallets.FundSourcePrefix + tradeId);
                ExcludeSweep(db, result, transfers, cash.Id, CoinbaseWallets.ProceedsDestinationPrefix + tradeId);
            }
        }

        if (cash != null && result.ExcludedTransfers > 0)
            LedgerProjections.Replace(db, cash.Id, DateTime.UtcNow);

        return result;
    }

    static void ExcludeSweep(
        System.Data.IDbConnection db,
        PredictionArchiveResult result,
        List<Transfer> transfers,
        int cashWalletId,
        string clientId)
    {
        var transfer = transfers.FirstOrDefault(row => row.WalletId == cashWalletId && row.ClientId == clientId);
        if (transfer == null || transfer.Excluded)
            return;
        transfer.Excluded = true;
        db.Update(transfer);
        result.ExcludedTransfers++;
        result.Lines.Add("excluded transfer " + transfer.ClientId + " on " + CoinbaseWallets.Cash);
    }

    // coinbase-* wallets whose trades are prediction product ids, plus empty -kalshi shells the sync created from holdings.
    // pm-*, kalshi-real, coinbase-usdc, and spot coinbase-* wallets are left as they are.
    static bool ShouldArchive(Wallet wallet, List<Trade> trades)
    {
        if (!wallet.Key.StartsWith("coinbase-", StringComparison.Ordinal))
            return false;
        if (wallet.Key == CoinbaseWallets.Cash)
            return false;
        if (string.Equals(wallet.Key, "kalshi-real", StringComparison.OrdinalIgnoreCase))
            return false;
        if (PredictionMarkets.IsHandWallet(wallet.Key))
            return false;

        var mine = trades.Where(trade => trade.WalletId == wallet.Id).ToList();
        if (mine.Count == 0)
            return PredictionMarkets.IsKalshiSlug(wallet.Key);
        if (mine.Any(trade => !PredictionMarkets.IsProductId(trade.Symbol)))
            return false;
        return mine.Any(trade => PredictionMarkets.IsProductId(trade.Symbol));
    }
}
