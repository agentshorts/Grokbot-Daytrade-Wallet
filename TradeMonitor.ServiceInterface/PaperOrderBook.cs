using System.Data;
using ServiceStack;
using ServiceStack.Data;
using ServiceStack.OrmLite;
using TradeMonitor.ServiceModel;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.ServiceInterface;

public sealed record OrderDraft(
    string Wallet,
    string Symbol,
    TradeSide Side,
    LiveOrderType Type,
    decimal Qty,
    decimal? TriggerPrice,
    CostingMethod Costing,
    int? LotId,
    string? ClientId);

// Paper orders live in SQLite and fill through the ledger. Real wallets are rejected before any insert.
public sealed class PaperOrderBook(IDbConnectionFactory dbFactory, ILedgerClock clock)
{
    public LiveBookResponse Place(OrderDraft draft, decimal? price, decimal? sellQty = null)
    {
        var problem = OrderRules.Validate(draft.Side, draft.Type, draft.Qty, draft.TriggerPrice, draft.Costing, draft.LotId);
        if (problem != null)
            throw Fail(400, "InvalidRequest", problem);

        using var db = dbFactory.Open();
        RequireOrders(db);
        var wallet = RequirePaper(db, draft.Wallet);
        var symbol = Symbol(draft.Symbol);
        if (draft.ClientId is { Length: > 0 } clientId)
        {
            var existing = db.Single<PaperOrder>(x => x.WalletId == wallet.Id && x.ClientId == clientId);
            if (existing != null)
            {
                if (!Same(existing, draft, symbol))
                    throw Fail(409, "DifferentValues", $"Order client id '{clientId}' already exists with different values.");
                return Describe(db, wallet, existing.Symbol, price, sellQty, existing.TradeId);
            }
        }

        var order = new PaperOrder
        {
            WalletId = wallet.Id,
            ClientId = string.IsNullOrWhiteSpace(draft.ClientId) ? "live-" + Guid.NewGuid().ToString("N") : draft.ClientId.Trim(),
            Symbol = symbol,
            Side = draft.Side,
            Type = draft.Type,
            Qty = draft.Qty,
            TriggerPrice = draft.Type == LiveOrderType.Market ? null : draft.TriggerPrice,
            Costing = draft.Costing,
            LotId = draft.LotId,
            Status = LiveOrderStatus.Open,
            CreatedAt = clock.UtcNow,
        };
        order.Id = (int)db.Insert(order, selectIdentity: true);
        int? tradeId = null;
        if (order.Type == LiveOrderType.Market)
        {
            if (price is not decimal mark || mark <= 0)
            {
                db.DeleteById<PaperOrder>(order.Id);
                throw Fail(400, "NoPrice", $"No public price yet for {symbol}.");
            }

            tradeId = FillOne(db, wallet, order, mark, dropOnReject: true);
        }
        else if (price is decimal live && OrderRules.IsDue(order.Type, order.Side, live, order.TriggerPrice))
        {
            tradeId = FillOne(db, wallet, order, live, dropOnReject: false);
        }

        return Describe(db, wallet, symbol, price, sellQty, tradeId);
    }

    public LiveBookResponse Cancel(int id, decimal? price)
    {
        using var db = dbFactory.Open();
        RequireOrders(db);
        var order = db.SingleById<PaperOrder>(id) ?? throw Fail(404, "UnknownOrder", $"Unknown order '{id}'.");
        var wallet = RequirePaper(db, OrderWallet(db, order));
        if (order.Status == LiveOrderStatus.Filled)
            throw Fail(409, "AlreadyFilled", "That order has already filled.");
        if (order.Status == LiveOrderStatus.Open)
        {
            order.Status = LiveOrderStatus.Cancelled;
            order.ClosedAt = clock.UtcNow;
            order.LastError = null;
            db.Update(order);
        }

        return Describe(db, wallet, order.Symbol, price, null, null);
    }

    public LiveBookResponse Move(int id, decimal trigger, decimal? price)
    {
        if (trigger <= 0)
            throw Fail(400, "InvalidRequest", "Trigger price must be greater than zero.");
        using var db = dbFactory.Open();
        RequireOrders(db);
        var order = db.SingleById<PaperOrder>(id) ?? throw Fail(404, "UnknownOrder", $"Unknown order '{id}'.");
        var wallet = RequirePaper(db, OrderWallet(db, order));
        if (order.Status != LiveOrderStatus.Open)
            throw Fail(409, "AlreadyFilled", "Only an open order can be moved.");
        if (order.Type == LiveOrderType.Market)
            throw Fail(400, "InvalidRequest", "A market order has no trigger price.");
        order.TriggerPrice = trigger;
        order.LastError = null;
        db.Update(order);
        int? tradeId = null;
        if (price is decimal live && OrderRules.IsDue(order.Type, order.Side, live, order.TriggerPrice))
            tradeId = FillOne(db, wallet, order, live, dropOnReject: false);
        return Describe(db, wallet, order.Symbol, price, null, tradeId);
    }

    public int FillDue(string symbol, decimal price)
    {
        if (price <= 0)
            return 0;
        symbol = Symbol(symbol);
        using var db = dbFactory.Open();
        if (!db.TableExists<PaperOrder>())
            return 0;
        var open = db.Select<PaperOrder>(x => x.Status == LiveOrderStatus.Open && x.Symbol == symbol)
            .OrderBy(x => x.Id)
            .ToList();
        var filled = 0;
        foreach (var order in open)
        {
            if (!OrderRules.IsDue(order.Type, order.Side, price, order.TriggerPrice))
                continue;
            var wallet = db.SingleById<Wallet>(order.WalletId);
            if (wallet == null || wallet.Mode != WalletMode.Paper)
                continue;
            if (FillOne(db, wallet, order, price, dropOnReject: false) != null)
                filled++;
        }

        return filled;
    }

    public LiveBookResponse Read(string walletKey, string symbol, decimal? price, decimal? sellQty)
    {
        using var db = dbFactory.Open();
        var wallet = RequireWallet(db, walletKey);
        return Describe(db, wallet, Symbol(symbol), price, sellQty, null);
    }

    int? FillOne(IDbConnection db, Wallet wallet, PaperOrder order, decimal price, bool dropOnReject)
    {
        var stored = db.SingleById<PaperOrder>(order.Id);
        if (stored == null || stored.Status != LiveOrderStatus.Open)
            return stored?.TradeId;
        try
        {
            var ledger = new LedgerServices(dbFactory, clock);
            var response = (RecordTradeResponse)ledger.Any(new RecordTrade
            {
                Wallet = wallet.Key,
                ClientId = "order-" + order.Id,
                Symbol = order.Symbol,
                Side = order.Side,
                Qty = order.Qty,
                Price = price,
                Fee = 0,
                Costing = order.Costing == CostingMethod.Fifo ? null : order.Costing,
                LotId = order.LotId,
                Title = OrderRules.TitleFor(order.Type, order.Costing, order.LotId),
                Note = "Live paper order",
                ExecutedAt = clock.UtcNow,
            });
            stored.Status = LiveOrderStatus.Filled;
            stored.FilledPrice = price;
            stored.TradeId = response.Result.Id;
            stored.ClosedAt = clock.UtcNow;
            stored.LastError = null;
            db.Update(stored);
            return response.Result.Id;
        }
        catch (HttpError ex)
        {
            if (dropOnReject)
            {
                db.DeleteById<PaperOrder>(order.Id);
                throw;
            }

            stored.LastError = ex.Message;
            db.Update(stored);
            return null;
        }
    }

    LiveBookResponse Describe(IDbConnection db, Wallet wallet, string symbol, decimal? price, decimal? sellQty, int? tradeId)
    {
        var replay = LedgerBook.Replay(
            wallet.Id,
            db.Select<Trade>(x => x.WalletId == wallet.Id),
            db.Select<Transfer>(x => x.WalletId == wallet.Id),
            db.Select<PriceMark>(x => x.WalletId == null || x.WalletId == wallet.Id),
            clock.UtcNow);
        if (replay.Oversold)
            throw Fail(500, "LedgerCorrupt", $"Stored trades oversell {replay.OversellSymbol} in wallet '{wallet.Key}'.");
        var orders = db.TableExists<PaperOrder>()
            ? db.Select<PaperOrder>(x => x.WalletId == wallet.Id).Select(LiveBooks.ToOrder).ToList()
            : [];
        return LiveBooks.Describe(wallet, false, null, symbol, price, replay, orders, sellQty, tradeId);
    }

    static bool Same(PaperOrder row, OrderDraft draft, string symbol) =>
        row.Symbol == symbol
        && row.Side == draft.Side
        && row.Type == draft.Type
        && row.Qty == draft.Qty
        && row.TriggerPrice == (draft.Type == LiveOrderType.Market ? null : draft.TriggerPrice)
        && row.Costing == draft.Costing
        && row.LotId == draft.LotId;

    Wallet RequirePaper(IDbConnection db, string key)
    {
        var wallet = RequireWallet(db, key);
        if (wallet.Mode != WalletMode.Paper)
            throw Fail(400, "RealWallet", $"Wallet '{wallet.Key}' is a real wallet. Live orders are not saved.");
        if (!wallet.BaseCurrency.Equals("USD", StringComparison.OrdinalIgnoreCase))
            throw Fail(400, "InvalidRequest", $"Wallet '{wallet.Key}' is not a USD wallet.");
        return wallet;
    }

    static Wallet RequireWallet(IDbConnection db, string key)
    {
        var name = key.Trim();
        return db.Single<Wallet>(x => x.Key == name)
            ?? throw Fail(404, "UnknownWallet", $"Unknown wallet '{name}'.");
    }

    static string OrderWallet(IDbConnection db, PaperOrder order) =>
        db.SingleById<Wallet>(order.WalletId)?.Key
        ?? throw Fail(404, "UnknownWallet", "Unknown wallet.");

    static void RequireOrders(IDbConnection db)
    {
        if (!db.TableExists<PaperOrder>())
            throw Fail(500, "MigrationRequired", "Run migrations before using paper orders.");
    }

    static string Symbol(string symbol)
    {
        var text = symbol.Trim().ToUpperInvariant();
        if (text.Length == 0 || text.Length > 64)
            throw Fail(400, "InvalidRequest", "Symbol is required.");
        return text;
    }

    static HttpError Fail(int status, string code, string message) => new(status, code, message);
}
