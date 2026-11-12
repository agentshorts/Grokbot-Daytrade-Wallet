using System.Data;
using System.Globalization;
using NUnit.Framework;
using ServiceStack.Data;
using ServiceStack.OrmLite;
using ServiceStack.OrmLite.Sqlite;
using TradeMonitor.Data;
using TradeMonitor.Migrations;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.Tests;

public class LedgerSchemaTests
{
    static readonly DateTime At = new(2026, 11, 12, 15, 30, 45, 123, DateTimeKind.Utc);

    [Test]
    public void Fresh_database_creates_ledger_tables_and_has_zero_wallets()
    {
        using var ledger = new TempLedger();
        using var db = ledger.Open();

        Assert.That(db.TableExists<Wallet>(), Is.True);
        Assert.That(db.TableExists<Trade>(), Is.True);
        Assert.That(db.TableExists<Transfer>(), Is.True);
        Assert.That(db.TableExists<PriceMark>(), Is.True);
        Assert.That(db.TableExists<Position>(), Is.True);
        Assert.That(db.TableExists<BalanceSnapshot>(), Is.True);
        Assert.That(db.TableExists<PaperOrder>(), Is.True);
        Assert.That(db.Count<Wallet>(), Is.EqualTo(0));
        Assert.That(db.Count<Trade>(), Is.EqualTo(0));
        Assert.That(db.Count<Transfer>(), Is.EqualTo(0));
        Assert.That(db.Count<PriceMark>(), Is.EqualTo(0));

        var wallet = Columns(db, "Wallet");
        Assert.That(wallet["Key"], Does.StartWith("VARCHAR"));
        Assert.That(wallet["BaseCurrency"], Does.Contain("VARCHAR"));
        Assert.That(CreateSql(db, "Wallet"), Does.Contain("USD"));

        AssertDecimalColumns(db, "Trade", "Qty", "Price", "Fee", "TargetPrice");
        Assert.That(Columns(db, "Trade")["Kind"], Does.Contain("VARCHAR"));
        Assert.That(Columns(db, "Trade")["Costing"], Does.Contain("VARCHAR"));
        Assert.That(CreateSql(db, "Trade"), Does.Contain("Fill"));
        Assert.That(CreateSql(db, "Trade"), Does.Contain("Fifo"));
        Assert.That(CreateSql(db, "PaperOrder"), Does.Contain("Open"));
        Assert.That(IndexSql(db, "PaperOrder"), Does.Contain("UNIQUE").And.Contain("ClientId"));
        AssertDecimalColumns(db, "Transfer", "Amount");
        AssertDecimalColumns(db, "PriceMark", "Price");
        AssertDecimalColumns(db, "Position", "Qty", "CostBasis", "AvgPrice", "RealizedPnl", "FeesPaid", "LastMarkPrice");
        AssertDecimalColumns(db, "BalanceSnapshot", "Cash", "PositionsValue", "TotalValue", "Swept");

        Assert.That(IndexSql(db, "Wallet"), Does.Contain("UNIQUE").And.Contain("Key"));
        Assert.That(IndexSql(db, "Trade"), Does.Contain("UNIQUE").And.Contain("WalletId").And.Contain("ClientId"));
        Assert.That(IndexSql(db, "Transfer"), Does.Contain("UNIQUE").And.Contain("WalletId").And.Contain("ClientId"));
        Assert.That(IndexSql(db, "PriceMark"), Does.Contain("UNIQUE").And.Contain("ClientId"));
    }

    [Test]
    public void Migration_can_revert_and_run_again_without_seeding_wallets()
    {
        using var ledger = new TempLedger();
        using var db = ledger.Open();

        var reverted = ledger.Migrator.Revert(Migrator.All);
        Assert.That(reverted.Succeeded, Is.True, () => reverted.Error?.ToString() ?? "revert failed");
        Assert.That(db.TableExists<Wallet>(), Is.False);

        var again = ledger.Migrator.Run();
        Assert.That(again.Succeeded, Is.True, () => again.Error?.ToString() ?? "migration failed");
        Assert.That(db.TableExists<Wallet>(), Is.True);
        Assert.That(db.Count<Wallet>(), Is.EqualTo(0));
    }

    [Test]
    public void Kind_column_migration_keeps_existing_trades()
    {
        using var ledger = new TempLedger();
        using (var db = ledger.Open())
        {
            var walletId = InsertWallet(db, "alpha");
            db.Insert(NewTrade(walletId, "kept", qty: 1.25m, price: 3.5m));

            var names = new List<string>();
            using (var list = db.CreateCommand())
            {
                list.CommandText = "SELECT Name FROM Migration";
                using var reader = list.ExecuteReader();
                while (reader.Read())
                    names.Add(reader.GetString(0));
            }

            Assert.That(names, Does.Contain("Migration1001"));
            db.ExecuteSql("ALTER TABLE Trade DROP COLUMN Kind");
            db.ExecuteSql("DELETE FROM Migration WHERE Name IN ('Migration1001', 'Migration1002', 'Migration1003')");
        }

        var again = ledger.Migrator.Run();
        Assert.That(again.Succeeded, Is.True, () => again.Error?.ToString() ?? "migration failed");

        using var read = ledger.Open();
        var saved = read.Single<Trade>(x => x.ClientId == "kept");
        Assert.That(read.Count<Trade>(), Is.EqualTo(1));
        Assert.That(saved.Qty, Is.EqualTo(1.25m));
        Assert.That(saved.Price, Is.EqualTo(3.5m));
        Assert.That(saved.Fee, Is.EqualTo(0.01m));
        Assert.That(saved.Kind, Is.EqualTo(TradeKind.Fill));
        Assert.That(saved.Symbol, Is.EqualTo("BTC-USD"));
    }

    [Test]
    public void Costing_migration_keeps_existing_trades_and_orders()
    {
        using var ledger = new TempLedger();
        using (var db = ledger.Open())
        {
            var walletId = InsertWallet(db, "alpha");
            db.Insert(NewTrade(walletId, "kept", qty: 2m, price: 3m));
            db.ExecuteSql("ALTER TABLE Trade DROP COLUMN LotId");
            db.ExecuteSql("ALTER TABLE Trade DROP COLUMN Costing");
            db.ExecuteSql("DROP TABLE PaperOrder");
            db.ExecuteSql("DELETE FROM Migration WHERE Name IN ('Migration1002', 'Migration1003')");
        }

        var again = ledger.Migrator.Run();
        Assert.That(again.Succeeded, Is.True, () => again.Error?.ToString() ?? "migration failed");

        using var read = ledger.Open();
        var saved = read.Single<Trade>(x => x.ClientId == "kept");
        Assert.That(saved.Qty, Is.EqualTo(2m));
        Assert.That(saved.Price, Is.EqualTo(3m));
        Assert.That(saved.Costing, Is.EqualTo(CostingMethod.Fifo));
        Assert.That(saved.LotId, Is.Null);
        Assert.That(read.TableExists<PaperOrder>(), Is.True);
        Assert.That(read.Count<PaperOrder>(), Is.EqualTo(0));

        var second = ledger.Migrator.Run();
        Assert.That(second.Succeeded, Is.True, () => second.Error?.ToString() ?? "second run failed");
        Assert.That(read.Count<Trade>(), Is.EqualTo(1));
    }

    [Test]
    public void Wallet_key_is_unique()
    {
        using var ledger = new TempLedger();
        using var db = ledger.Open();

        db.Insert(NewWallet("alpha"));
        AssertConstraint(() => db.Insert(NewWallet("alpha")), "UNIQUE constraint failed");
        Assert.That(db.Count<Wallet>(), Is.EqualTo(1));

        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT Mode FROM Wallet";
        Assert.That(cmd.ExecuteScalar(), Is.EqualTo(nameof(WalletMode.Paper)));
    }

    [Test]
    public void Same_trade_client_id_stores_one_row()
    {
        using var ledger = new TempLedger();
        using var db = ledger.Open();
        var walletId = InsertWallet(db, "alpha");

        db.Insert(NewTrade(walletId, "order-1", qty: 1.5m, price: 10m));
        AssertConstraint(() => db.Insert(NewTrade(walletId, "order-1", qty: 9m, price: 99m)), "UNIQUE constraint failed");

        var rows = db.Select<Trade>();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].Qty, Is.EqualTo(1.5m));
        Assert.That(rows[0].Price, Is.EqualTo(10m));
        Assert.That(rows[0].ClientId, Is.EqualTo("order-1"));
    }

    [Test]
    public void Same_client_id_on_another_wallet_stores_a_second_trade()
    {
        using var ledger = new TempLedger();
        using var db = ledger.Open();
        var first = InsertWallet(db, "alpha");
        var second = InsertWallet(db, "beta");

        db.Insert(NewTrade(first, "order-1", qty: 1m, price: 2m));
        db.Insert(NewTrade(second, "order-1", qty: 3m, price: 4m));

        Assert.That(db.Count<Trade>(), Is.EqualTo(2));
    }

    [Test]
    public void Transfer_client_id_is_unique_per_wallet()
    {
        using var ledger = new TempLedger();
        using var db = ledger.Open();
        var walletId = InsertWallet(db, "alpha");

        db.Insert(NewTransfer(walletId, "xfer-1", TransferKind.Deposit, 50m));
        AssertConstraint(() => db.Insert(NewTransfer(walletId, "xfer-1", TransferKind.Sweep, 1m)), "UNIQUE constraint failed");
        Assert.That(db.Count<Transfer>(), Is.EqualTo(1));
        Assert.That(db.Single<Transfer>(x => x.WalletId == walletId).Kind, Is.EqualTo(TransferKind.Deposit));
    }

    [Test]
    public void Price_mark_client_id_is_unique_when_present()
    {
        using var ledger = new TempLedger();
        using var db = ledger.Open();

        db.Insert(Mark(null, 100m));
        db.Insert(Mark(null, 101m));
        db.Insert(Mark("mark-1", 102m));
        AssertConstraint(() => db.Insert(Mark("mark-1", 103m)), "UNIQUE constraint failed");

        Assert.That(db.Count<PriceMark>(), Is.EqualTo(3));
        Assert.That(db.Count<PriceMark>(x => x.ClientId == null), Is.EqualTo(2));
    }

    [Test]
    public void Position_is_unique_per_wallet_and_symbol()
    {
        using var ledger = new TempLedger();
        using var db = ledger.Open();
        var walletId = InsertWallet(db, "alpha");

        db.Insert(new Position
        {
            WalletId = walletId,
            Symbol = "BTC-USD",
            Qty = 1m,
            UpdatedAt = At,
        });
        AssertConstraint(() => db.Insert(new Position
        {
            WalletId = walletId,
            Symbol = "BTC-USD",
            Qty = 2m,
            UpdatedAt = At,
        }), "UNIQUE constraint failed");
        Assert.That(db.Count<Position>(), Is.EqualTo(1));

        db.Insert(new Position
        {
            WalletId = walletId,
            Symbol = "ETH-USD",
            Qty = 4m,
            UpdatedAt = At,
        });
        Assert.That(db.Count<Position>(), Is.EqualTo(2));
    }

    [Test]
    public void Decimal_qty_and_price_keep_at_least_eight_places()
    {
        const decimal qty = 0.12345678m;
        const decimal price = 123456789.12345678m;
        const decimal fee = 0.00000001m;

        using var ledger = new TempLedger();
        int tradeId;
        using (var db = ledger.Open())
        {
            var walletId = InsertWallet(db, "alpha");
            tradeId = (int)db.Insert(new Trade
            {
                WalletId = walletId,
                ClientId = "order-8",
                Symbol = "BTC-USD",
                Side = TradeSide.Buy,
                Qty = qty,
                Price = price,
                Fee = fee,
                FeeCurrency = "USD",
                ExecutedAt = At,
                TargetPrice = 0.12345678m,
                RecordedAt = At,
            }, selectIdentity: true);
        }

        using var read = ledger.Open();
        var saved = read.SingleById<Trade>(tradeId);
        Assert.That(saved.Qty, Is.EqualTo(qty));
        Assert.That(saved.Price, Is.EqualTo(price));
        Assert.That(saved.Fee, Is.EqualTo(fee));
        Assert.That(saved.TargetPrice, Is.EqualTo(0.12345678m));
        Assert.That(saved.Qty.ToString(CultureInfo.InvariantCulture), Does.Contain("12345678"));
        Assert.That(saved.Price.ToString(CultureInfo.InvariantCulture), Does.Contain("12345678"));

        using var cmd = read.CreateCommand();
        cmd.CommandText = "SELECT typeof(Qty), typeof(Price) FROM Trade";
        using var reader = cmd.ExecuteReader();
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("text"));
        Assert.That(reader.GetString(1), Is.EqualTo("text"));
    }

    [Test]
    public void Timestamps_round_trip_as_utc()
    {
        using var ledger = new TempLedger();
        using var db = ledger.Open();
        var walletId = InsertWallet(db, "alpha");
        db.Insert(NewTrade(walletId, "order-utc", qty: 1m, price: 2m));

        var saved = db.Single<Trade>(x => x.ClientId == "order-utc");
        Assert.That(saved.ExecutedAt, Is.EqualTo(At));
        Assert.That(saved.ExecutedAt.Kind, Is.EqualTo(DateTimeKind.Utc));
        Assert.That(saved.RecordedAt.Kind, Is.EqualTo(DateTimeKind.Utc));

        var wallet = db.SingleById<Wallet>(walletId);
        Assert.That(wallet.CreatedAt, Is.EqualTo(At));
        Assert.That(wallet.CreatedAt.Kind, Is.EqualTo(DateTimeKind.Utc));
    }

    [Test]
    public void Trade_without_a_wallet_is_rejected()
    {
        using var ledger = new TempLedger();
        using var db = ledger.Open();

        using (var pragma = db.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys";
            Assert.That(Convert.ToInt32(pragma.ExecuteScalar(), CultureInfo.InvariantCulture), Is.EqualTo(1));
        }

        AssertConstraint(() => db.Insert(NewTrade(walletId: 99, "missing", qty: 1m, price: 1m)), "FOREIGN KEY constraint failed");
        Assert.That(db.Count<Trade>(), Is.EqualTo(0));
    }

    static Wallet NewWallet(string key) => new()
    {
        Key = key,
        Name = key,
        Mode = WalletMode.Paper,
        Venue = "Coinbase",
        BaseCurrency = "USD",
        CreatedAt = At,
    };

    static int InsertWallet(IDbConnection db, string key)
    {
        db.Insert(NewWallet(key));
        return (int)db.LastInsertId();
    }

    static Trade NewTrade(int walletId, string clientId, decimal qty, decimal price) => new()
    {
        WalletId = walletId,
        ClientId = clientId,
        Symbol = "BTC-USD",
        Side = TradeSide.Buy,
        Qty = qty,
        Price = price,
        Fee = 0.01m,
        FeeCurrency = "USD",
        ExecutedAt = At,
        RecordedAt = At,
    };

    static Transfer NewTransfer(int walletId, string clientId, TransferKind kind, decimal amount) => new()
    {
        WalletId = walletId,
        ClientId = clientId,
        Kind = kind,
        Amount = amount,
        Currency = "USD",
        At = At,
        RecordedAt = At,
    };

    static PriceMark Mark(string? clientId, decimal price) => new()
    {
        ClientId = clientId,
        Symbol = "BTC-USD",
        Price = price,
        At = At,
        Source = "agent",
        RecordedAt = At,
    };

    static void AssertConstraint(Action action, string fragment)
    {
        Exception? caught = null;
        try
        {
            action();
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        Assert.That(caught, Is.Not.Null);
        Assert.That(caught!.ToString(), Does.Contain(fragment));
    }

    static void AssertDecimalColumns(IDbConnection db, string table, params string[] names)
    {
        var columns = Columns(db, table);
        foreach (var name in names)
            Assert.That(columns[name], Is.EqualTo("TEXT"), $"{table}.{name}");
    }

    static Dictionary<string, string> Columns(IDbConnection db, string table)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = db.CreateCommand();
        cmd.CommandText = $"SELECT name, type FROM pragma_table_info('{table}')";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            map[reader.GetString(0)] = reader.GetString(1);
        return map;
    }

    static string CreateSql(IDbConnection db, string table)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = @name";
        var p = cmd.CreateParameter();
        p.ParameterName = "@name";
        p.Value = table;
        cmd.Parameters.Add(p);
        return (string)cmd.ExecuteScalar()!;
    }

    static string IndexSql(IDbConnection db, string table)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT group_concat(sql, '\n') FROM sqlite_master WHERE type = 'index' AND tbl_name = @name";
        var p = cmd.CreateParameter();
        p.ParameterName = "@name";
        p.Value = table;
        cmd.Parameters.Add(p);
        return cmd.ExecuteScalar() as string ?? "";
    }

    sealed class TempLedger : IDisposable
    {
        readonly string path;

        public TempLedger()
        {
            path = Path.Combine(Path.GetTempPath(), "trademonitor-ledger-" + Guid.NewGuid().ToString("N") + ".db");
            LedgerDialect.Configure(SqliteDialect.Provider);
            Factory = new OrmLiteConnectionFactory(path, SqliteDialect.Provider);
            Migrator = new Migrator(Factory, typeof(Migration1000).Assembly);
            var result = Migrator.Run();
            if (!result.Succeeded)
                throw new InvalidOperationException(result.Error?.ToString() ?? "migration failed");
        }

        public IDbConnectionFactory Factory { get; }

        public Migrator Migrator { get; }

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
