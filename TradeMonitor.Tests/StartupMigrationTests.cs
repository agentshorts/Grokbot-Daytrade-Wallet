using System.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NUnit.Framework;
using ServiceStack.Data;
using ServiceStack.OrmLite;
using TradeMonitor.Data;
using TradeMonitor.Migrations;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.Tests;

// An existing database already has AspNetUsers, so startup must apply later OrmLite migrations
// without going through the empty-database bootstrap.
[NonParallelizable]
public class StartupMigrationTests
{
    static readonly DateTime At = new(2026, 11, 12, 15, 30, 45, DateTimeKind.Utc);
    const string TradeColumns = "ClientId, Symbol, Side, Qty, Price, Fee, Note";

    string? previousMcpKey;

    [SetUp]
    public void SetUp()
    {
        previousMcpKey = Environment.GetEnvironmentVariable(McpConfiguredKey.EnvName);
        Environment.SetEnvironmentVariable(McpConfiguredKey.EnvName, null);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(McpConfiguredKey.EnvName, previousMcpKey);
    }

    [Test]
    public void Existing_migration1000_database_gains_kind_on_startup()
    {
        using var fixture = new LegacyDatabase();
        var before = fixture.ReadRows($"SELECT {TradeColumns} FROM Trade ORDER BY ClientId");
        Assert.That(before, Has.Count.EqualTo(2));
        Assert.That(fixture.HasColumn("Trade", "Kind"), Is.False);
        Assert.That(fixture.HasColumn("Trade", "Costing"), Is.False);
        Assert.That(fixture.HasColumn("Trade", "LotId"), Is.False);
        Assert.That(fixture.ReadRows("SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'PaperOrder'"), Is.Empty);
        Assert.That(fixture.ReadRows("SELECT Name FROM Migration ORDER BY Name"), Is.EqualTo(new[] { "Migration1000" }));
        Assert.That(fixture.ReadRows("SELECT COUNT(*) FROM AspNetUsers"), Is.EqualTo(new[] { "0" }));

        fixture.Boot();

        Assert.That(fixture.HasColumn("Trade", "Kind"), Is.True);
        Assert.That(fixture.HasColumn("Trade", "Costing"), Is.True);
        Assert.That(fixture.HasColumn("Trade", "LotId"), Is.True);
        Assert.That(fixture.ReadRows($"SELECT {TradeColumns} FROM Trade ORDER BY ClientId"), Is.EqualTo(before));
        Assert.That(fixture.ReadRows("SELECT Kind FROM Trade ORDER BY ClientId"), Is.EqualTo(new[] { "Fill", "Fill" }));
        Assert.That(fixture.ReadRows("SELECT Costing, ifnull(LotId, '<null>') FROM Trade ORDER BY ClientId"), Is.EqualTo(new[] { "Fifo|<null>", "Fifo|<null>" }));
        Assert.That(fixture.ReadRows("SELECT COUNT(*) FROM PaperOrder"), Is.EqualTo(new[] { "0" }));
        Assert.That(fixture.ReadRows("SELECT Name FROM Migration ORDER BY Name"), Is.EqualTo(new[] { "Migration1000", "Migration1001", "Migration1002", "Migration1003" }));
        Assert.That(fixture.ReadRows("SELECT ProductId FROM TrackedSymbol ORDER BY SortOrder"), Is.EqualTo(new[]
        {
            "BTC-USD", "ETH-USD", "SOL-USD", "DOGE-USD", "LTC-USD", "POL-USD", "XRP-USD", "ADA-USD", "AVAX-USD", "LINK-USD",
        }));
        Assert.That(fixture.ReadRows("SELECT COUNT(*) FROM PriceBar"), Is.EqualTo(new[] { "0" }));
        Assert.That(fixture.ReadRows("SELECT COUNT(*) FROM AspNetUsers"), Is.EqualTo(new[] { "0" }));
        Assert.That(fixture.ReadRows("SELECT Key, Name FROM Wallet ORDER BY Key"), Is.EqualTo(new[] { "paper|Paper book" }));
    }

    [Test]
    public void Second_startup_leaves_the_migrated_database_unchanged()
    {
        using var fixture = new LegacyDatabase();
        fixture.Boot();
        var afterFirst = fixture.Snapshot();

        Assert.That(afterFirst.Trades, Has.Count.EqualTo(2));
        Assert.That(afterFirst.Trades.All(row => row.Contains("|Fill|Fifo|<null>")), Is.True, string.Join("\n", afterFirst.Trades));
        Assert.That(afterFirst.Migrations.Select(row => row.Split('|')[1]), Is.EqualTo(new[] { "Migration1000", "Migration1001", "Migration1002", "Migration1003" }));
        Assert.That(fixture.ReadRows("SELECT COUNT(*) FROM PaperOrder"), Is.EqualTo(new[] { "0" }));
        Assert.That(fixture.ReadRows("SELECT COUNT(*) FROM TrackedSymbol"), Is.EqualTo(new[] { "10" }));

        fixture.Boot();
        var afterSecond = fixture.Snapshot();

        Assert.That(afterSecond.Trades, Is.EqualTo(afterFirst.Trades));
        Assert.That(afterSecond.Wallets, Is.EqualTo(afterFirst.Wallets));
        Assert.That(afterSecond.Migrations, Is.EqualTo(afterFirst.Migrations));
        Assert.That(afterSecond.Users, Is.EqualTo(afterFirst.Users));
        Assert.That(afterSecond.Users, Is.EqualTo("0"));
        Assert.That(fixture.ReadRows("SELECT COUNT(*) FROM TrackedSymbol"), Is.EqualTo(new[] { "10" }));
    }

    sealed class LegacyDatabase : IDisposable
    {
        readonly string path;
        readonly string logDir;

        public LegacyDatabase()
        {
            var id = Guid.NewGuid().ToString("N");
            path = Path.Combine(Path.GetTempPath(), "trademonitor-startup-" + id + ".db");
            logDir = Path.Combine(Path.GetTempPath(), "trademonitor-startup-logs-" + id);
            Directory.CreateDirectory(logDir);

            var connectionString = "DataSource=" + path + ";Cache=Shared";
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connectionString)
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
                .Options;
            using (var identity = new ApplicationDbContext(options))
                identity.Database.EnsureCreated();

            LedgerDialect.Configure(SqliteDialect.Provider);
            var factory = new OrmLiteConnectionFactory(connectionString, SqliteDialect.Provider);
            var migrated = new Migrator(factory, typeof(Migration1000)).Run();
            if (!migrated.Succeeded)
                throw new InvalidOperationException(migrated.Error?.Message ?? "Migration1000 failed", migrated.Error);

            using (var db = factory.Open())
            {
                db.Insert(new Wallet
                {
                    Key = "paper",
                    Name = "Paper book",
                    Mode = WalletMode.Paper,
                    BaseCurrency = "USD",
                    CreatedAt = At,
                });
                var walletId = (int)db.LastInsertId();
                db.Insert(NewTrade(walletId, "kept-a", TradeSide.Buy, 1.25m, 3.5m, 0.01m, "keep-a"));
                db.Insert(NewTrade(walletId, "kept-b", TradeSide.Sell, 2m, 4.25m, 0.02m, "keep-b"));
                // Migration1000 creates Kind from the current Trade model. Databases created
                // before that column existed only have Migration1000 recorded and no Kind column.
                db.ExecuteSql("ALTER TABLE Trade DROP COLUMN Kind");
                // The current Trade model creates Costing and LotId. A database from before
                // Migration1002 has neither column and no PaperOrder table.
                db.ExecuteSql("ALTER TABLE Trade DROP COLUMN LotId");
                db.ExecuteSql("ALTER TABLE Trade DROP COLUMN Costing");
            }

            SqliteConnection.ClearAllPools();
        }

        public void Boot()
        {
            SqliteConnection.ClearAllPools();
            using var factory = new MigrationAppFactory(path, logDir);
            using var client = factory.CreateClient();
            SqliteConnection.ClearAllPools();
        }

        public bool HasColumn(string table, string column)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info(@table) WHERE name = @column";
            var tableParameter = cmd.CreateParameter();
            tableParameter.ParameterName = "@table";
            tableParameter.Value = table;
            cmd.Parameters.Add(tableParameter);
            var columnParameter = cmd.CreateParameter();
            columnParameter.ParameterName = "@column";
            columnParameter.Value = column;
            cmd.Parameters.Add(columnParameter);
            return Convert.ToInt32(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) > 0;
        }

        public List<string> ReadRows(string sql)
        {
            using var db = Open();
            return Rows(db, sql);
        }

        public DatabaseSnapshot Snapshot()
        {
            using var db = Open();
            return new DatabaseSnapshot(
                Rows(db, $"SELECT {TradeColumns}, Kind, Costing, ifnull(LotId, '<null>') FROM Trade ORDER BY ClientId"),
                Rows(db, "SELECT Key, Name, BaseCurrency, Mode FROM Wallet ORDER BY Key"),
                Rows(db, "SELECT Id, Name, CompletedDate, ErrorCode, ErrorMessage FROM Migration ORDER BY Id"),
                Rows(db, "SELECT COUNT(*) FROM AspNetUsers").Single());
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            {
                var file = path + suffix;
                if (File.Exists(file))
                    File.Delete(file);
            }

            if (Directory.Exists(logDir))
                Directory.Delete(logDir, recursive: true);
        }

        SqliteConnection Open()
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false,
            }.ConnectionString);
            connection.Open();
            return connection;
        }

        static List<string> Rows(IDbConnection db, string sql)
        {
            var rows = new List<string>();
            using var cmd = db.CreateCommand();
            cmd.CommandText = sql;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var parts = new string[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++)
                    parts[i] = reader.IsDBNull(i) ? "<null>" : Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture) ?? "";
                rows.Add(string.Join("|", parts));
            }

            return rows;
        }

        static Trade NewTrade(int walletId, string clientId, TradeSide side, decimal qty, decimal price, decimal fee, string note) => new()
        {
            WalletId = walletId,
            ClientId = clientId,
            Symbol = side == TradeSide.Buy ? "BTC-USD" : "ETH-USD",
            Side = side,
            Qty = qty,
            Price = price,
            Fee = fee,
            Note = note,
            ExecutedAt = At,
            RecordedAt = At,
        };
    }

    sealed record DatabaseSnapshot(List<string> Trades, List<string> Wallets, List<string> Migrations, string Users);

    sealed class MigrationAppFactory(string dbPath, string logDir) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:DefaultConnection", "DataSource=" + dbPath + ";Cache=Shared");
            builder.UseSetting("RequestLogs:Dir", logDir);
            builder.UseSetting(McpConfiguredKey.ConfigPath, "");
        }
    }
}
