using ServiceStack.OrmLite;
using TradeMonitor.Data;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.Migrations;

// Stores candle volume so aggregated intervals can sum it. Safe to run more than once.
public class Migration1005 : MigrationBase
{
    public override void Up()
    {
        LedgerDialect.Configure(Db.GetDialectProvider());
        if (!Db.TableExists<PriceBar>())
            return;
        if (!Db.ColumnExists<PriceBar>(x => x.Volume))
            Db.AddColumn<PriceBar>(x => x.Volume);
    }

    public override void Down()
    {
    }
}
