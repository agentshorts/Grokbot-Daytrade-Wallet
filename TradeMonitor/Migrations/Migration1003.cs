using ServiceStack.OrmLite;
using TradeMonitor.Data;
using TradeMonitor.ServiceModel;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.Migrations;

// Price history and the tracked symbol list. Safe to run more than once.
public class Migration1003 : MigrationBase
{
    static readonly DateTime SeedAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public override void Up()
    {
        LedgerDialect.Configure(Db.GetDialectProvider());
        if (!Db.TableExists<PriceBar>())
            Db.CreateTable<PriceBar>();
        if (!Db.TableExists<TrackedSymbol>())
            Db.CreateTable<TrackedSymbol>();
        if (Db.Count<TrackedSymbol>() > 0)
            return;

        var order = 0;
        foreach (var product in TrackedSymbolDefaults.Products)
        {
            Db.Insert(new TrackedSymbol
            {
                ProductId = product,
                SortOrder = order++,
                CreatedAt = SeedAt,
            });
        }
    }

    public override void Down()
    {
        LedgerDialect.Configure(Db.GetDialectProvider());
        if (Db.TableExists<PriceBar>())
            Db.DropTable<PriceBar>();
        if (Db.TableExists<TrackedSymbol>())
            Db.DropTable<TrackedSymbol>();
    }
}
