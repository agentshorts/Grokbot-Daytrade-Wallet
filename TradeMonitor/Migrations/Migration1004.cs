using ServiceStack.OrmLite;
using TradeMonitor.Data;
using TradeMonitor.ServiceInterface;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.Migrations;

// Tags existing pm-* wallets as prediction markets and archives sync-created contract wallets.
// Nothing is deleted. Safe to run more than once.
public class Migration1004 : MigrationBase
{
    public override void Up()
    {
        LedgerDialect.Configure(Db.GetDialectProvider());
        if (Db.TableExists<Wallet>() && !Db.ColumnExists<Wallet>(x => x.Kind))
            Db.AddColumn<Wallet>(x => x.Kind);
        if (Db.TableExists<Transfer>() && !Db.ColumnExists<Transfer>(x => x.Excluded))
            Db.AddColumn<Transfer>(x => x.Excluded);
        if (!Db.TableExists<Wallet>())
            return;
        if (Db.ColumnExists<Wallet>(x => x.Kind))
            Db.ExecuteSql("UPDATE Wallet SET Kind = 'Standard' WHERE Kind IS NULL OR Kind = ''");
        if (Db.TableExists<Transfer>() && Db.ColumnExists<Transfer>(x => x.Excluded))
            Db.ExecuteSql("UPDATE Transfer SET Excluded = 0 WHERE Excluded IS NULL");

        if (Db == null)
            return;
        var result = PredictionMarketArchive.Apply(Db);
        foreach (var line in result.Lines)
            Console.WriteLine(line);
    }

    public override void Down()
    {
        LedgerDialect.Configure(Db.GetDialectProvider());
        // Archived wallets stay archived. Dropping Excluded puts those cash rows back into totals.
        if (Db.TableExists<Transfer>() && Db.ColumnExists<Transfer>(x => x.Excluded))
            Db.DropColumn<Transfer>(x => x.Excluded);
        if (Db.TableExists<Wallet>() && Db.ColumnExists<Wallet>(x => x.Kind))
            Db.DropColumn<Wallet>(x => x.Kind);
    }
}
