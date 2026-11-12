using ServiceStack.OrmLite;
using TradeMonitor.Data;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.Migrations;

// Adds Trade.Kind for databases created before settlements. Existing rows default to Fill.
public class Migration1001 : MigrationBase
{
    public override void Up()
    {
        LedgerDialect.Configure(Db.GetDialectProvider());
        if (Db.ColumnExists<Trade>(x => x.Kind))
            return;

        Db.AddColumn<Trade>(x => x.Kind);
    }

    public override void Down()
    {
        LedgerDialect.Configure(Db.GetDialectProvider());
        if (!Db.ColumnExists<Trade>(x => x.Kind))
            return;

        Db.DropColumn<Trade>(x => x.Kind);
    }
}
