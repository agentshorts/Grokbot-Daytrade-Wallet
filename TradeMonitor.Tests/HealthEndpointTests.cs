using System.Data;
using NUnit.Framework;
using ServiceStack.Data;
using ServiceStack.OrmLite;
using TradeMonitor.Data;
using TradeMonitor.Migrations;
using TradeMonitor.ServiceInterface;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.Tests;

[NonParallelizable]
public class LedgerHealthReportTests
{
    [Test]
    public void Empty_ledger_has_no_last_write()
    {
        using var ledger = new TempLedger();
        var report = LedgerHealthReport.Create(ledger.Factory, "test-version");

        Assert.That(report.Status, Is.EqualTo("ok"));
        Assert.That(report.Database, Is.EqualTo("ok"));
        Assert.That(report.LastWriteAt, Is.Null);
        Assert.That(report.Version, Is.EqualTo("test-version"));
    }

    [Test]
    public void Last_write_is_the_newest_recorded_timestamp()
    {
        using var ledger = new TempLedger();
        var created = new DateTime(2026, 6, 1, 15, 0, 0, DateTimeKind.Utc);
        var traded = new DateTime(2026, 6, 1, 15, 4, 0, DateTimeKind.Utc);
        var marked = new DateTime(2026, 6, 1, 15, 10, 0, DateTimeKind.Utc);
        using (var db = ledger.Open())
        {
            db.Insert(new Wallet
            {
                Key = "paper",
                Name = "Paper",
                Mode = WalletMode.Paper,
                BaseCurrency = "USD",
                CreatedAt = created,
            });
            var walletId = (int)db.LastInsertId();
            db.Insert(new Trade
            {
                WalletId = walletId,
                ClientId = "fill-1",
                Symbol = "BTC-USD",
                Side = TradeSide.Buy,
                Qty = 0.01m,
                Price = 64000m,
                Fee = 0.5m,
                ExecutedAt = traded,
                RecordedAt = traded,
            });
            db.Insert(new Transfer
            {
                WalletId = walletId,
                ClientId = "dep-1",
                Kind = TransferKind.Deposit,
                Amount = 50m,
                Currency = "USD",
                At = created,
                RecordedAt = created,
            });
            db.Insert(new PriceMark
            {
                ClientId = "mark-1",
                Symbol = "BTC-USD",
                Price = 64100m,
                At = marked,
                Source = "agent",
                WalletId = walletId,
                RecordedAt = marked,
            });
        }

        var report = LedgerHealthReport.Create(ledger.Factory, "test-version");

        Assert.That(report.Status, Is.EqualTo("ok"));
        Assert.That(report.LastWriteAt, Is.EqualTo("2026-06-01T15:10:00Z"));
    }

    [Test]
    public void A_database_that_cannot_be_opened_is_unavailable()
    {
        var missing = Path.Combine(Path.GetTempPath(), "trademonitor-health-missing-" + Guid.NewGuid().ToString("N"), "app.db");
        LedgerDialect.Configure(SqliteDialect.Provider);
        var factory = new OrmLiteConnectionFactory("DataSource=" + missing + ";Mode=ReadOnly", SqliteDialect.Provider);

        var report = LedgerHealthReport.Create(factory, "test-version");

        Assert.That(report.Status, Is.EqualTo("unavailable"));
        Assert.That(report.Database, Is.EqualTo("unavailable"));
        Assert.That(report.LastWriteAt, Is.Null);
        Assert.That(report.Version, Is.EqualTo("test-version"));
    }

    sealed class TempLedger : IDisposable
    {
        readonly string path;

        public TempLedger()
        {
            path = Path.Combine(Path.GetTempPath(), "trademonitor-health-" + Guid.NewGuid().ToString("N") + ".db");
            LedgerDialect.Configure(SqliteDialect.Provider);
            Factory = new OrmLiteConnectionFactory(path, SqliteDialect.Provider);
            var migrated = new Migrator(Factory, typeof(Migration1000).Assembly).Run();
            if (!migrated.Succeeded)
                throw new InvalidOperationException(migrated.Error?.ToString() ?? "migration failed");
        }

        public IDbConnectionFactory Factory { get; }

        public IDbConnection Open() => Factory.Open();

        public void Dispose()
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            {
                var file = path + suffix;
                if (File.Exists(file))
                    File.Delete(file);
            }
        }
    }
}
