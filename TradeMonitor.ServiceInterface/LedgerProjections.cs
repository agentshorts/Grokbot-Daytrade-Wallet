using System.Data;
using ServiceStack;
using ServiceStack.OrmLite;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.ServiceInterface;

// Rewrites the stored position and balance rows from a fresh replay.
// Balance reads use these rows, so a flag change such as Transfer.Excluded has to rebuild them.
public static class LedgerProjections
{
    public static void Replace(IDbConnection db, int walletId, DateTime updatedAt)
    {
        var wallet = db.SingleById<Wallet>(walletId)
            ?? throw new HttpError(500, "LedgerCorrupt", "Wallet " + walletId + " is missing.");
        var replay = LedgerBook.Replay(
            walletId,
            db.Select<Trade>(trade => trade.WalletId == walletId),
            db.Select<Transfer>(transfer => transfer.WalletId == walletId),
            db.Select<PriceMark>(mark => mark.WalletId == null || mark.WalletId == walletId),
            updatedAt);
        if (replay.Oversold)
            throw new HttpError(500, "LedgerCorrupt",
                $"Stored trades oversell {replay.OversellSymbol} in wallet '{wallet.Key}'.");

        db.Delete<Position>(position => position.WalletId == walletId);
        db.Delete<BalanceSnapshot>(snapshot => snapshot.WalletId == walletId);
        foreach (var position in replay.Positions)
            db.Insert(position);
        foreach (var snapshot in replay.Snapshots)
            db.Insert(snapshot);
    }
}
