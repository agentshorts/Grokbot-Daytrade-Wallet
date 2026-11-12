using ServiceStack;
using ServiceStack.Data;
using ServiceStack.OrmLite;
using TradeMonitor.ServiceModel;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.ServiceInterface;

// In-memory book for a real wallet. Nothing here is inserted into the ledger.
public sealed class LiveSimulationStore(IDbConnectionFactory dbFactory, ILedgerClock clock)
{
    const int MaxSessions = 200;
    readonly object gate = new();
    readonly Dictionary<string, Session> sessions = new(StringComparer.Ordinal);
    readonly Queue<string> order = new();

    public LiveBookResponse Open(string walletKey, string symbol, decimal? price)
    {
        using var db = dbFactory.Open();
        var wallet = db.Single<Wallet>(x => x.Key == walletKey.Trim())
            ?? throw Fail(404, "UnknownWallet", $"Unknown wallet '{walletKey.Trim()}'.");
        if (wallet.Mode != WalletMode.Real)
            throw Fail(400, "PaperWallet", $"Wallet '{wallet.Key}' is a paper wallet. Orders are saved in the ledger.");
        var session = new Session
        {
            Id = Guid.NewGuid().ToString("N"),
            Wallet = wallet,
            Symbol = Clean(symbol),
            Trades = db.Select<Trade>(x => x.WalletId == wallet.Id).Select(Copy).ToList(),
            Transfers = db.Select<Transfer>(x => x.WalletId == wallet.Id).Select(Copy).ToList(),
            Marks = db.Select<PriceMark>(x => x.WalletId == null || x.WalletId == wallet.Id).Select(Copy).ToList(),
        };
        session.NextTradeId = session.Trades.Count == 0 ? 1 : session.Trades.Max(x => x.Id) + 1;
        if (price is decimal mark && mark > 0)
            session.Prices[session.Symbol] = mark;
        lock (gate)
        {
            sessions[session.Id] = session;
            order.Enqueue(session.Id);
            while (sessions.Count > MaxSessions && order.TryDequeue(out var old))
                sessions.Remove(old);
        }

        return Describe(session, null);
    }

    public LiveBookResponse Read(string id, string? symbol, decimal? price, decimal? sellQty)
    {
        var session = Find(id);
        lock (session.Gate)
        {
            if (!string.IsNullOrWhiteSpace(symbol))
                session.Symbol = Clean(symbol);
            if (price is decimal mark && mark > 0)
            {
                session.Prices[session.Symbol] = mark;
                FillSymbol(session, session.Symbol, mark);
            }

            return Describe(session, null, sellQty);
        }
    }

    public LiveBookResponse Place(string id, OrderDraft draft, decimal? price)
    {
        var problem = OrderRules.Validate(draft.Side, draft.Type, draft.Qty, draft.TriggerPrice, draft.Costing, draft.LotId);
        if (problem != null)
            throw Fail(400, "InvalidRequest", problem);
        var session = Find(id);
        lock (session.Gate)
        {
            var symbol = Clean(draft.Symbol);
            session.Symbol = symbol;
            if (price is decimal mark && mark > 0)
                session.Prices[symbol] = mark;
            var order = new PaperOrder
            {
                Id = ++session.NextOrderId,
                WalletId = session.Wallet.Id,
                ClientId = "sim-" + orderId(session),
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
            session.Orders.Add(order);
            int? tradeId = null;
            var live = session.Prices.TryGetValue(symbol, out var known) ? known : price;
            if (order.Type == LiveOrderType.Market)
            {
                if (live is not decimal fill || fill <= 0)
                {
                    session.Orders.Remove(order);
                    throw Fail(400, "NoPrice", $"No public price yet for {symbol}.");
                }

                tradeId = FillOrder(session, order, fill, dropOnReject: true);
            }
            else if (live is decimal crossed && OrderRules.IsDue(order.Type, order.Side, crossed, order.TriggerPrice))
            {
                tradeId = FillOrder(session, order, crossed, dropOnReject: false);
            }

            return Describe(session, tradeId);
        }
    }

    public LiveBookResponse Cancel(string id, int orderId)
    {
        var session = Find(id);
        lock (session.Gate)
        {
            var order = session.Orders.FirstOrDefault(x => x.Id == orderId)
                ?? throw Fail(404, "UnknownOrder", $"Unknown order '{orderId}'.");
            if (order.Status == LiveOrderStatus.Filled)
                throw Fail(409, "AlreadyFilled", "That order has already filled.");
            if (order.Status == LiveOrderStatus.Open)
            {
                order.Status = LiveOrderStatus.Cancelled;
                order.ClosedAt = clock.UtcNow;
            }

            session.Symbol = order.Symbol;
            return Describe(session, null);
        }
    }

    public LiveBookResponse Move(string id, int orderId, decimal trigger, decimal? price)
    {
        if (trigger <= 0)
            throw Fail(400, "InvalidRequest", "Trigger price must be greater than zero.");
        var session = Find(id);
        lock (session.Gate)
        {
            var order = session.Orders.FirstOrDefault(x => x.Id == orderId)
                ?? throw Fail(404, "UnknownOrder", $"Unknown order '{orderId}'.");
            if (order.Status != LiveOrderStatus.Open)
                throw Fail(409, "AlreadyFilled", "Only an open order can be moved.");
            if (order.Type == LiveOrderType.Market)
                throw Fail(400, "InvalidRequest", "A market order has no trigger price.");
            order.TriggerPrice = trigger;
            session.Symbol = order.Symbol;
            if (price is decimal mark && mark > 0)
                session.Prices[order.Symbol] = mark;
            int? tradeId = null;
            if (session.Prices.TryGetValue(order.Symbol, out var live) && OrderRules.IsDue(order.Type, order.Side, live, trigger))
                tradeId = FillOrder(session, order, live, dropOnReject: false);
            return Describe(session, tradeId);
        }
    }

    public LiveBookResponse Reset(string id, string? symbol)
    {
        var session = Find(id);
        lock (session.Gate)
        {
            using var db = dbFactory.Open();
            var wallet = db.SingleById<Wallet>(session.Wallet.Id)
                ?? throw Fail(404, "UnknownWallet", $"Unknown wallet '{session.Wallet.Key}'.");
            session.Wallet = wallet;
            session.Trades = db.Select<Trade>(x => x.WalletId == wallet.Id).Select(Copy).ToList();
            session.Transfers = db.Select<Transfer>(x => x.WalletId == wallet.Id).Select(Copy).ToList();
            session.Marks = db.Select<PriceMark>(x => x.WalletId == null || x.WalletId == wallet.Id).Select(Copy).ToList();
            session.Orders.Clear();
            session.NextTradeId = session.Trades.Count == 0 ? 1 : session.Trades.Max(x => x.Id) + 1;
            session.NextOrderId = 0;
            if (!string.IsNullOrWhiteSpace(symbol))
                session.Symbol = Clean(symbol);
            return Describe(session, null);
        }
    }

    public void FillDue(string symbol, decimal price)
    {
        if (price <= 0)
            return;
        symbol = Clean(symbol);
        List<Session> copy;
        lock (gate)
            copy = sessions.Values.ToList();
        foreach (var session in copy)
        {
            lock (session.Gate)
            {
                session.Prices[symbol] = price;
                FillSymbol(session, symbol, price);
            }
        }
    }

    static void FillSymbol(Session session, string symbol, decimal price)
    {
        foreach (var order in session.Orders.Where(x => x.Status == LiveOrderStatus.Open && x.Symbol == symbol).OrderBy(x => x.Id).ToList())
        {
            if (OrderRules.IsDue(order.Type, order.Side, price, order.TriggerPrice))
                FillOrder(session, order, price, dropOnReject: false);
        }
    }

    static int? FillOrder(Session session, PaperOrder order, decimal price, bool dropOnReject)
    {
        if (order.Status != LiveOrderStatus.Open)
            return order.TradeId;
        var candidate = new Trade
        {
            Id = session.NextTradeId,
            WalletId = session.Wallet.Id,
            ClientId = "sim-order-" + order.Id,
            Symbol = order.Symbol,
            Title = OrderRules.TitleFor(order.Type, order.Costing, order.LotId),
            Side = order.Side,
            Qty = order.Qty,
            Price = price,
            Fee = 0,
            ExecutedAt = DateTime.UtcNow,
            Costing = order.Costing,
            LotId = order.LotId,
            Note = "Simulation",
        };
        var before = Replay(session);
        var after = LedgerBook.Replay(
            session.Wallet.Id,
            session.Trades.Append(candidate),
            session.Transfers,
            session.Marks,
            DateTime.UtcNow);
        var reason = Reject(before, after, candidate, session.Wallet.Key);
        if (reason != null)
        {
            if (dropOnReject)
            {
                session.Orders.Remove(order);
                var code = after.Oversold ? "Oversell" : "InsufficientCash";
                throw Fail(400, code, reason);
            }

            order.LastError = reason;
            return null;
        }

        session.Trades.Add(candidate);
        session.NextTradeId++;
        order.Status = LiveOrderStatus.Filled;
        order.FilledPrice = price;
        order.TradeId = candidate.Id;
        order.ClosedAt = DateTime.UtcNow;
        order.LastError = null;
        return candidate.Id;
    }

    static string? Reject(LedgerReplay before, LedgerReplay after, Trade candidate, string walletKey)
    {
        if (after.Oversold)
        {
            var where = candidate.Costing == CostingMethod.Lot
                ? $"the remaining quantity of {after.OpenQty} on lot {candidate.LotId}"
                : $"the open quantity of {after.OpenQty}";
            return $"Sell of {candidate.Qty} {candidate.Symbol} exceeds {where} in wallet '{walletKey}'.";
        }

        if (candidate.Side == TradeSide.Buy && after.HasDeposits && after.Cash < -LedgerBook.CashDust)
            return $"Buy costs more than the cash of {before.Cash} in wallet '{walletKey}'.";
        if (candidate.Side == TradeSide.Sell && after.HasDeposits && after.Cash < -LedgerBook.CashDust && after.Cash < before.Cash)
            return $"This sell would leave wallet '{walletKey}' with negative cash.";
        return null;
    }

    static LedgerReplay Replay(Session session) =>
        LedgerBook.Replay(session.Wallet.Id, session.Trades, session.Transfers, session.Marks, DateTime.UtcNow);

    LiveBookResponse Describe(Session session, int? tradeId, decimal? sellQty = null)
    {
        var price = session.Prices.TryGetValue(session.Symbol, out var mark) ? mark : (decimal?)null;
        var replay = Replay(session);
        var orders = session.Orders.Select(LiveBooks.ToOrder).ToList();
        return LiveBooks.Describe(session.Wallet, true, session.Id, session.Symbol, price, replay, orders, sellQty, tradeId);
    }

    Session Find(string? id)
    {
        var key = id?.Trim() ?? "";
        lock (gate)
        {
            if (sessions.TryGetValue(key, out var session))
                return session;
        }

        throw Fail(404, "UnknownSimulation", "Unknown simulation.");
    }

    static string Clean(string symbol)
    {
        var text = symbol.Trim().ToUpperInvariant();
        if (text.Length == 0)
            throw Fail(400, "InvalidRequest", "Symbol is required.");
        return text;
    }

    static string orderId(Session session) => session.NextOrderId.ToString();

    static HttpError Fail(int status, string code, string message) => new(status, code, message);

    static Trade Copy(Trade row) => new()
    {
        Id = row.Id,
        WalletId = row.WalletId,
        ClientId = row.ClientId,
        Symbol = row.Symbol,
        Title = row.Title,
        Side = row.Side,
        Kind = row.Kind,
        Qty = row.Qty,
        Price = row.Price,
        Fee = row.Fee,
        FeeCurrency = row.FeeCurrency,
        ExecutedAt = row.ExecutedAt,
        OrderRef = row.OrderRef,
        TargetPrice = row.TargetPrice,
        Note = row.Note,
        Costing = row.Costing,
        LotId = row.LotId,
        RecordedAt = row.RecordedAt,
    };

    static Transfer Copy(Transfer row) => new()
    {
        Id = row.Id,
        WalletId = row.WalletId,
        ClientId = row.ClientId,
        Kind = row.Kind,
        Amount = row.Amount,
        Currency = row.Currency,
        At = row.At,
        Note = row.Note,
        RecordedAt = row.RecordedAt,
    };

    static PriceMark Copy(PriceMark row) => new()
    {
        Id = row.Id,
        ClientId = row.ClientId,
        Symbol = row.Symbol,
        Price = row.Price,
        At = row.At,
        Source = row.Source,
        WalletId = row.WalletId,
        RecordedAt = row.RecordedAt,
    };

    sealed class Session
    {
        public object Gate { get; } = new();
        public required string Id { get; init; }
        public required Wallet Wallet { get; set; }
        public string Symbol { get; set; } = "";
        public List<Trade> Trades { get; set; } = [];
        public List<Transfer> Transfers { get; set; } = [];
        public List<PriceMark> Marks { get; set; } = [];
        public List<PaperOrder> Orders { get; } = [];
        public Dictionary<string, decimal> Prices { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int NextTradeId { get; set; } = 1;
        public int NextOrderId { get; set; }
    }
}
