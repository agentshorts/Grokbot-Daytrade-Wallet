using ServiceStack;
using ServiceStack.Data;
using ServiceStack.OrmLite;
using TradeMonitor.ServiceModel;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.ServiceInterface;

public class LiveServices(
    IDbConnectionFactory dbFactory,
    ILedgerClock clock,
    ILiveMarket market,
    PaperOrderBook orders,
    LiveSimulationStore simulations,
    PriceBars bars,
    TrackedSymbolBook symbols) : Service
{
    public object Any(GetLiveProducts request) =>
        new GetLiveProductsResponse { Results = market.Products().ToList() };

    public object Any(GetLiveCandles request)
    {
        var product = RequireSymbol(request.Product);
        var key = CoinbaseMessages.GranularityKey(request.Granularity);
        market.Watch(product, key);
        return new GetLiveCandlesResponse
        {
            Product = product,
            Granularity = key,
            Results = market.Candles(product, key).ToList(),
        };
    }

    public object Any(GetLiveTicker request)
    {
        var product = RequireSymbol(request.Product);
        market.Watch(product, "5m");
        return new GetLiveTickerResponse { Result = market.Ticker(product) };
    }

    public object Any(GetLiveBars request)
    {
        var product = RequireSymbol(request.Product);
        var key = CoinbaseMessages.GranularityKey(request.Granularity);
        market.Watch(product, key);
        return new GetLiveBarsResponse
        {
            Product = product,
            Granularity = key,
            Results = bars.Candles(product, CoinbaseMessages.GranularitySeconds(key)),
        };
    }

    public object Any(GetLiveTrades request)
    {
        var symbol = RequireSymbol(request.Symbol);
        if (!string.IsNullOrWhiteSpace(request.SimulationId))
            return new GetLiveTradesResponse { Results = simulations.Marks(request.SimulationId, symbol) };
        return new GetLiveTradesResponse { Results = LedgerMarks(RequireKey(request.Wallet), symbol) };
    }

    public object Any(GetTrackedSymbols request) =>
        new GetTrackedSymbolsResponse { CatalogLoaded = symbols.CatalogLoaded, Results = Rows(symbols.List()) };

    public object Any(AddTrackedSymbol request) =>
        new GetTrackedSymbolsResponse { CatalogLoaded = symbols.CatalogLoaded, Results = Rows(symbols.Add(request.Product)) };

    public object Any(RemoveTrackedSymbol request) =>
        new GetTrackedSymbolsResponse { CatalogLoaded = symbols.CatalogLoaded, Results = Rows(symbols.Remove(request.Product)) };

    public object Any(ReorderTrackedSymbols request) =>
        new GetTrackedSymbolsResponse { CatalogLoaded = symbols.CatalogLoaded, Results = Rows(symbols.Reorder(request.Products)) };

    List<LiveTradeMark> LedgerMarks(string wallet, string symbol)
    {
        var ledger = new LedgerServices(dbFactory, clock);
        var marks = new List<LiveTradeMark>();
        var skip = 0;
        while (marks.Count < 5000)
        {
            var page = (QueryTradesResponse)ledger.Any(new QueryTrades
            {
                Wallet = wallet,
                Symbol = symbol,
                Take = 1000,
                Skip = skip,
                OrderBy = "time",
                Descending = false,
            });
            if (page.Results.Count == 0)
                break;
            marks.AddRange(page.Results.Select(trade => new LiveTradeMark
            {
                TradeId = trade.Id,
                Side = trade.Side,
                Kind = trade.Kind,
                Qty = trade.Qty,
                Price = trade.Price,
                Fee = trade.Fee,
                ExecutedAt = trade.ExecutedAt,
                Simulated = false,
            }));
            if (page.Results.Count < 1000)
                break;
            skip += page.Results.Count;
        }

        return marks;
    }

    static List<TrackedSymbolRow> Rows(IEnumerable<TrackedRow> rows) =>
        rows.Select(row => new TrackedSymbolRow
        {
            ProductId = row.ProductId,
            Name = row.Name,
            SortOrder = row.SortOrder,
            Listed = row.Listed,
        }).ToList();

    public object Any(CreatePaperWallet request)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            throw Fail(400, "InvalidRequest", "Wallet name is required.");
        if (name.Length > 200)
            throw Fail(400, "InvalidRequest", "Wallet name cannot be longer than 200 characters.");
        if (request.StartingCash <= 0)
            throw Fail(400, "InvalidRequest", "Starting cash must be greater than zero.");
        var root = Slug(name);
        if (root.Length == 0)
            throw Fail(400, "InvalidRequest", "Wallet name needs a letter or number.");

        var ledger = new LedgerServices(dbFactory, clock);
        string key;
        using (var db = dbFactory.Open())
        {
            key = root;
            for (var n = 2; db.Exists<Wallet>(x => x.Key == key); n++)
            {
                if (n > 1000)
                    throw Fail(409, "InvalidRequest", "Could not choose a wallet key.");
                key = root + "-" + n;
            }
        }

        var created = (CreateWalletResponse)ledger.Any(new CreateWallet
        {
            Key = key,
            Name = name,
            Mode = WalletMode.Paper,
            Venue = "Coinbase",
            BaseCurrency = "USD",
            Note = "Opened from Live",
        });
        var deposit = (RecordTransferResponse)ledger.Any(new RecordTransfer
        {
            Wallet = created.Result.Key,
            ClientId = "open:" + created.Result.Key,
            Kind = TransferKind.Deposit,
            Amount = request.StartingCash,
            Note = "Starting cash",
        });
        return new CreatePaperWalletResponse { Result = created.Result, Cash = deposit.Balance.Cash };
    }

    public object Any(GetLiveBook request)
    {
        var symbol = RequireSymbol(request.Symbol);
        var price = Price(symbol);
        if (price is decimal mark)
            orders.FillDue(symbol, mark);
        return orders.Read(RequireKey(request.Wallet), symbol, price, request.SellQty);
    }

    public object Any(PlacePaperOrder request)
    {
        var symbol = RequireSymbol(request.Symbol);
        RequireListed(symbol);
        return orders.Place(Draft(RequireKey(request.Wallet), symbol, request.Side, request.Type, request.Qty, request.TriggerPrice, request.Costing, request.LotId, request.ClientId), Price(symbol));
    }

    public object Any(CancelPaperOrder request)
    {
        var cancelled = orders.Cancel(request.Id, null);
        return orders.Read(cancelled.Wallet, cancelled.Symbol, Price(cancelled.Symbol), null);
    }

    public object Any(MovePaperOrder request)
    {
        var moved = orders.Move(request.Id, request.TriggerPrice, null);
        var price = Price(moved.Symbol);
        if (price is decimal mark)
            orders.FillDue(moved.Symbol, mark);
        return orders.Read(moved.Wallet, moved.Symbol, price, null);
    }

    public object Any(OpenLiveSimulation request)
    {
        var symbol = RequireSymbol(request.Symbol);
        return simulations.Open(RequireKey(request.Wallet), symbol, Price(symbol));
    }

    public object Any(GetLiveSimulation request)
    {
        var symbol = string.IsNullOrWhiteSpace(request.Symbol) ? null : RequireSymbol(request.Symbol);
        var price = symbol == null ? null : Price(symbol);
        if (symbol != null && price is decimal mark)
            simulations.FillDue(symbol, mark);
        return simulations.Read(request.Id ?? "", symbol, price, request.SellQty);
    }

    public object Any(PlaceSimulationOrder request)
    {
        var symbol = RequireSymbol(request.Symbol);
        RequireListed(symbol);
        return simulations.Place(
            request.Id ?? "",
            Draft("", symbol, request.Side, request.Type, request.Qty, request.TriggerPrice, request.Costing, request.LotId, null),
            Price(symbol));
    }

    public object Any(CancelSimulationOrder request) =>
        simulations.Cancel(request.Id ?? "", request.OrderId);

    public object Any(MoveSimulationOrder request)
    {
        var moved = simulations.Move(request.Id ?? "", request.OrderId, request.TriggerPrice, null);
        var price = Price(moved.Symbol);
        if (price is decimal mark)
            simulations.FillDue(moved.Symbol, mark);
        return simulations.Read(request.Id ?? "", moved.Symbol, price, null);
    }

    public object Any(ResetLiveSimulation request)
    {
        var symbol = string.IsNullOrWhiteSpace(request.Symbol) ? null : RequireSymbol(request.Symbol);
        return simulations.Reset(request.Id ?? "", symbol);
    }

    decimal? Price(string symbol)
    {
        market.Watch(symbol, "5m");
        var tick = market.Ticker(symbol);
        return tick is { Price: > 0 } ? tick.Price : null;
    }

    void RequireListed(string symbol)
    {
        if (market.Products().Count == 0)
            throw Fail(400, "NoProducts", "The public product list is not loaded yet.");
        if (!market.IsListed(symbol))
            throw Fail(400, "UnknownProduct", $"Unknown USD product '{symbol}'.");
    }

    static OrderDraft Draft(string wallet, string symbol, TradeSide? side, LiveOrderType? type, decimal qty, decimal? trigger, CostingMethod? costing, int? lotId, string? clientId)
    {
        if (side is not TradeSide knownSide || !Enum.IsDefined(knownSide))
            throw Fail(400, "InvalidRequest", "Order side must be Buy or Sell.");
        if (type is not LiveOrderType knownType || !Enum.IsDefined(knownType))
            throw Fail(400, "InvalidRequest", "Order type must be market, limit, stop-loss, or take-profit.");
        var method = costing ?? CostingMethod.Fifo;
        return new OrderDraft(wallet, symbol, knownSide, knownType, qty, trigger, method, lotId, clientId);
    }

    static string RequireKey(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
            throw Fail(400, "InvalidRequest", "Wallet key is required.");
        return text;
    }

    static string RequireSymbol(string? value)
    {
        var text = value?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(text))
            throw Fail(400, "InvalidRequest", "Symbol is required.");
        return text;
    }

    static string Slug(string name)
    {
        var chars = name.Trim().ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray();
        var slug = new string(chars);
        while (slug.Contains("--", StringComparison.Ordinal))
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        slug = slug.Trim('-');
        return slug.Length <= 110 ? slug : slug[..110].Trim('-');
    }

    static HttpError Fail(int status, string code, string message) => new(status, code, message);
}
