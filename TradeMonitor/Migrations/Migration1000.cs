using ServiceStack.OrmLite;
using TradeMonitor.Data;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.Migrations;

public class Migration1000 : MigrationBase
{
    public override void Up()
    {
        LedgerDialect.Configure(Db.GetDialectProvider());

        Db.CreateTable<Wallet>();
        Db.CreateTable<Trade>();
        Db.CreateTable<Transfer>();
        Db.CreateTable<PriceMark>();
        Db.CreateTable<Position>();
        Db.CreateTable<BalanceSnapshot>();
    }

    public override void Down()
    {
        Db.DropTable<BalanceSnapshot>();
        Db.DropTable<Position>();
        Db.DropTable<PriceMark>();
        Db.DropTable<Transfer>();
        Db.DropTable<Trade>();
        Db.DropTable<Wallet>();
    }
}
