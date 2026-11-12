using ServiceStack.OrmLite;
using TradeMonitor.Data;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.Migrations;

// Adds sell costing on Trade and the paper order table. Safe to run more than once.
public class Migration1002 : MigrationBase
{
    public override void Up()
    {
        LedgerDialect.Configure(Db.GetDialectProvider());
        if (Db.TableExists<Trade>())
        {
            if (!Db.ColumnExists<Trade>(x => x.Costing))
                Db.AddColumn<Trade>(x => x.Costing);
            if (!Db.ColumnExists<Trade>(x => x.LotId))
                Db.AddColumn<Trade>(x => x.LotId);
        }

        if (!Db.TableExists<PaperOrder>())
            Db.CreateTable<PaperOrder>();
    }

    public override void Down()
    {
        LedgerDialect.Configure(Db.GetDialectProvider());
        if (Db.TableExists<PaperOrder>())
            Db.DropTable<PaperOrder>();
        if (!Db.TableExists<Trade>())
            return;
        if (Db.ColumnExists<Trade>(x => x.LotId))
            Db.DropColumn<Trade>(x => x.LotId);
        if (Db.ColumnExists<Trade>(x => x.Costing))
            Db.DropColumn<Trade>(x => x.Costing);
    }
}
