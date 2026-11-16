using System.Reflection;
using NUnit.Framework;
using ServiceStack;
using ServiceStack.OrmLite;
using TradeMonitor;
using TradeMonitor.Data;
using TradeMonitor.Migrations;
using TradeMonitor.ServiceInterface;
using TradeMonitor.ServiceModel;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.Tests;

public class PredictionBookingTests
{
    [Test]
    public void No_side_price_comes_from_the_cash_total()
    {
        Assert.That(PredictionBooking.Price(buy: true, 4.50m, 0.50m, 10m), Is.EqualTo(0.40m));
        Assert.That(PredictionBooking.Price(buy: true, 4.50m, 0.50m, 10m), Is.Not.EqualTo(0.60m));
        Assert.That(PredictionBooking.Price(buy: false, 6.80m, 0.20m, 10m), Is.EqualTo(0.70m));
        Assert.That(PredictionBooking.Price(buy: true, 0.50m, 0.50m, 10m), Is.Null);
        Assert.That(PredictionBooking.Price(buy: false, 0m, 0m, 10m), Is.EqualTo(0m));
        Assert.That(PredictionBooking.Price(buy: true, 1m, 0.10m, 0m), Is.Null);
        Assert.That(PredictionBooking.Price(buy: true, 1m, -0.01m, 1m), Is.Null);
    }

    [Test]
    public void Contract_side_accepts_the_prediction_enum_only()
    {
        Assert.That(PredictionBooking.ContractSide("PREDICTION_SIDE_NO"), Is.EqualTo("NO"));
        Assert.That(PredictionBooking.ContractSide("yes"), Is.EqualTo("YES"));
        Assert.That(PredictionBooking.ContractSide("BUY"), Is.Null);
        Assert.That(PredictionBooking.ContractSide(null), Is.Null);
    }

    [Test]
    public void Settlement_trade_type_maps_to_settlement()
    {
        Assert.That(PredictionBooking.KindOf("SETTLEMENT"), Is.EqualTo(TradeKind.Settlement));
        Assert.That(PredictionBooking.KindOf("FILL"), Is.EqualTo(TradeKind.Fill));
    }

    [Test]
    public void Settlement_client_id_is_distinct_from_the_opening_fill()
    {
        Assert.That(PredictionBooking.ClientIdFor("open1"), Is.EqualTo("cbpm-fill-open1"));
        Assert.That(PredictionBooking.ClientIdFor("open1", TradeKind.Settlement), Is.EqualTo("cbpm-settle-open1"));
        Assert.That(PredictionBooking.ClientIdFor("open1", TradeKind.Settlement), Is.Not.EqualTo(PredictionBooking.ClientIdFor("open1")));
    }

    [Test]
    public void No_orders_flip_to_the_ledger_side_before_the_cash_price()
    {
        Assert.That(PredictionBooking.LedgerIsBuy("SELL", "NO"), Is.True);
        Assert.That(PredictionBooking.LedgerIsBuy("BUY", "NO"), Is.False);
        Assert.That(PredictionBooking.LedgerIsBuy("BUY", "YES"), Is.True);
        Assert.That(PredictionBooking.LedgerIsBuy("SELL", "YES"), Is.False);
        Assert.That(PredictionBooking.Price(true, 4.1844m, 0m, 12.68m), Is.EqualTo(0.33m));
        Assert.That(PredictionBooking.Price(true, 3.9204m, 0m, 11.88m), Is.EqualTo(0.33m));
        Assert.That(PredictionBooking.Price(false, 4.158m, 0m, 11.88m), Is.EqualTo(0.35m));
        Assert.That(PredictionBooking.Price(true, 4.0033m, 0m, 8.17m), Is.EqualTo(0.49m));
        Assert.That(PredictionBooking.Price(false, 4.3301m, 0m, 8.17m), Is.EqualTo(0.53m));
    }

    [Test]
    public void Already_booked_matches_order_id_trade_id_and_product_settlement()
    {
        const string product = "KXBTCD-26NOV1213-T71299.99-KALSHI";
        var fillTrade = new PredictionTradeMark("cbpm-fill-trade-1", "order-1", TradeKind.Fill);
        var fillOrder = new PredictionTradeMark("cbpm-fill-order-9", null, TradeKind.Fill);
        var fillRef = new PredictionTradeMark("hand-231", "order-9", TradeKind.Fill);
        var settleProduct = new PredictionTradeMark("cbpm-settle-" + product, "", TradeKind.Settlement);
        var settleOrder = new PredictionTradeMark("cbpm-settle-order-9", null, TradeKind.Fill);
        var settleTrade = new PredictionTradeMark("cbpm-settle-trade-1", "order-1", TradeKind.Settlement);
        var anySettle = new PredictionTradeMark("manual-settle", null, TradeKind.Settlement);

        Assert.That(PredictionBooking.IsAlreadyBooked(fillTrade, TradeKind.Fill, "trade-1", "other", product), Is.True);
        Assert.That(PredictionBooking.IsAlreadyBooked(fillOrder, TradeKind.Fill, "new-trade", "order-9", product), Is.True);
        Assert.That(PredictionBooking.IsAlreadyBooked(fillRef, TradeKind.Fill, "new-trade", "order-9", product), Is.True);
        Assert.That(PredictionBooking.IsAlreadyBooked(fillTrade, TradeKind.Settlement, "trade-1", "order-1", product), Is.False);
        Assert.That(PredictionBooking.IsAlreadyBooked(settleProduct, TradeKind.Settlement, "other", "other", product), Is.True);
        Assert.That(PredictionBooking.IsAlreadyBooked(settleOrder, TradeKind.Settlement, "other", "order-9", product), Is.True);
        Assert.That(PredictionBooking.IsAlreadyBooked(settleTrade, TradeKind.Settlement, "trade-1", "different", product), Is.True);
        Assert.That(PredictionBooking.IsAlreadyBooked(anySettle, TradeKind.Settlement, "fresh", "fresh", product), Is.True);
        Assert.That(PredictionBooking.IsAlreadyBooked(settleProduct, TradeKind.Fill, "fresh", "fresh", product), Is.False);
        Assert.That(PredictionBooking.IsAlreadyBooked(fillRef, TradeKind.Fill, "fresh", "fresh", product), Is.False);
    }

    [Test]
    public void Booking_mode_defaults_to_dry_run()
    {
        Assert.That(PredictionBooking.ParseMode(null), Is.EqualTo(PredictionBookingMode.DryRun));
        Assert.That(PredictionBooking.ParseMode("dryrun"), Is.EqualTo(PredictionBookingMode.DryRun));
        Assert.That(PredictionBooking.ParseMode("On"), Is.EqualTo(PredictionBookingMode.On));
        Assert.That(PredictionBooking.ParseMode("off"), Is.EqualTo(PredictionBookingMode.Off));
        Assert.That(new PredictionBookingStore().Mode, Is.EqualTo(PredictionBookingMode.DryRun));
    }

    [Test]
    public void Would_book_stays_visible_when_the_wallet_already_exists()
    {
        var store = new PredictionBookingStore();
        var at = new DateTime(2026, 11, 16, 17, 0, 0, DateTimeKind.Utc);
        store.WouldBook("PROD", at, "cbpm-fill-x NO Buy 12.68 @ 0.33 fee 0 order o");
        store.Booked("PROD", at.AddMinutes(1), "pm-PROD");
        var row = PredictionBookingReport.List(store.Snapshot(), ["pm-PROD"]).Single();
        Assert.That(row.Status, Is.EqualTo("would-book"));
        Assert.That(row.Detail, Does.Contain("cbpm-fill-x"));
        Assert.That(row.Wallet, Is.EqualTo("pm-PROD"));
    }

    [Test]
    public void Copy_suffix_counts_as_the_same_contract()
    {
        const string product = "KXBTCD-26NOV1617-T63749.99-KALSHI";
        Assert.That(PredictionMarkets.KeyBooks("pm-" + product, product), Is.True);
        Assert.That(PredictionMarkets.KeyBooks("pm-" + product + "-2", product), Is.True);
        Assert.That(PredictionMarkets.KeyBooks("PM-" + product + "-10", product), Is.True);
        Assert.That(PredictionMarkets.KeyBooks("pm-" + product + "-02", product), Is.False);
        Assert.That(PredictionMarkets.KeyBooks("pm-" + product + "-2a", product), Is.False);
        Assert.That(PredictionMarkets.MatchingKey(product, ["pm-" + product + "-2", "other"]), Is.EqualTo("pm-" + product + "-2"));
        Assert.That(PredictionMarkets.MatchingKey(product, ["pm-" + product + "-2", "pm-" + product]), Is.EqualTo("pm-" + product));
    }

    [Test]
    public void Seen_does_not_downgrade_booked_or_problem()
    {
        var store = new PredictionBookingStore();
        var first = new DateTime(2026, 11, 16, 17, 0, 0, DateTimeKind.Utc);
        var later = first.AddMinutes(4);
        store.Seen("KX", later);
        store.Seen("KX", first);
        var row = store.Snapshot().Single();
        Assert.That(row.FirstSeenUtc, Is.EqualTo(first));
        Assert.That(row.LastSeenUtc, Is.EqualTo(later));
        Assert.That(row.Status, Is.EqualTo("seen"));

        store.Booked("KX", later, "pm-KX");
        store.Seen("KX", later.AddMinutes(1));
        row = store.Snapshot().Single();
        Assert.That(row.Status, Is.EqualTo("booked"));
        Assert.That(row.Detail, Is.Null);
        Assert.That(row.Wallet, Is.EqualTo("pm-KX"));

        store.Problem("KX", later.AddMinutes(2), "missing cash total or YES/NO side");
        store.Seen("KX", later.AddMinutes(3));
        row = store.Snapshot().Single();
        Assert.That(row.Status, Is.EqualTo("problem"));
        Assert.That(row.Detail, Does.Contain("missing cash total"));
        Assert.That(row.LastSeenUtc, Is.EqualTo(later.AddMinutes(3)));
        Assert.That(row.FirstSeenUtc, Is.EqualTo(first));
    }

    [Test]
    public void Booking_read_is_an_admin_get()
    {
        var route = typeof(GetPredictionBookings).GetCustomAttributes<RouteAttribute>().Single();
        Assert.That(route.Path, Is.EqualTo("/api/predictions/booking"));
        Assert.That(route.Verbs, Is.EqualTo("GET"));
        Assert.That(typeof(GetPredictionBookings).GetCustomAttributes<ValidateIsAdminAttribute>(), Is.Not.Empty);
    }

    [Test]
    public void Report_counts_an_archived_copy_and_keeps_a_problem()
    {
        var path = Path.Combine(Path.GetTempPath(), "trademonitor-pmbook-" + Guid.NewGuid().ToString("N") + ".db");
        AppHost.RegisterKey();
        LedgerDialect.Configure(SqliteDialect.Provider);
        var factory = new OrmLiteConnectionFactory(path, SqliteDialect.Provider);
        try
        {
            var migrated = new Migrator(factory, typeof(Migration1000).Assembly).Run();
            Assert.That(migrated.Succeeded, Is.True, migrated.Error?.ToString() ?? "migration failed");
            var at = new DateTime(2026, 11, 16, 17, 0, 0, DateTimeKind.Utc);
            using (var db = factory.Open())
            {
                db.Insert(WalletRow("pm-PROD-2", "copy", archived: true, at));
                db.Insert(WalletRow("pm-BARE", "bare", archived: false, at));
            }

            var store = new PredictionBookingStore();
            store.Seen("PROD", at);
            store.Seen("PROD", at.AddMinutes(5));
            store.Problem("BARE", at.AddMinutes(10), "missing cash total or YES/NO side");
            store.Seen("BARE", at.AddMinutes(11));
            store.Seen("OPEN", at.AddMinutes(1));
            store.Booked("DONE", at.AddMinutes(20), "pm-DONE");

            var response = (GetPredictionBookingsResponse)new PredictionBookingServices(factory, store).Any(new GetPredictionBookings());
            Assert.That(response.Mode, Is.EqualTo("DryRun"));
            Assert.That(response.Results.Select(row => row.ProductId), Is.EqualTo(new[] { "BARE", "OPEN", "DONE", "PROD" }));

            var bare = response.Results[0];
            Assert.That(bare.Status, Is.EqualTo("problem"));
            Assert.That(bare.Wallet, Is.EqualTo("pm-BARE"));
            Assert.That(bare.Detail, Does.Contain("missing cash total"));

            var open = response.Results[1];
            Assert.That(open.Status, Is.EqualTo("seen"));
            Assert.That(open.Wallet, Is.Null);

            var done = response.Results[2];
            Assert.That(done.Status, Is.EqualTo("booked"));
            Assert.That(done.Detail, Is.Null);
            Assert.That(done.Wallet, Is.EqualTo("pm-DONE"));

            var prod = response.Results[3];
            Assert.That(prod.Status, Is.EqualTo("booked"));
            Assert.That(prod.Wallet, Is.EqualTo("pm-PROD-2"));
            Assert.That(prod.Detail, Is.Null);
            Assert.That(prod.FirstSeen, Is.EqualTo(at));
            Assert.That(prod.LastSeen, Is.EqualTo(at.AddMinutes(5)));
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            {
                var file = path + suffix;
                if (File.Exists(file))
                    File.Delete(file);
            }
        }
    }

    static Wallet WalletRow(string key, string name, bool archived, DateTime createdAt) => new()
    {
        Key = key,
        Name = name,
        Mode = WalletMode.Real,
        Venue = "Coinbase",
        BaseCurrency = "USD",
        Kind = WalletKind.PredictionMarket,
        IsArchived = archived,
        CreatedAt = createdAt,
    };
}
