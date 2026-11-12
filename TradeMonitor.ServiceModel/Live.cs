using System.ComponentModel;
using ServiceStack;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.ServiceModel;

[Api("USD products from the public Coinbase catalog")]
[Tag("live")]
[ValidateIsAdmin]
[Route("/api/live/products", "GET")]
public class GetLiveProducts : IGet, IReturn<GetLiveProductsResponse>
{
}

public class GetLiveProductsResponse
{
    public List<LiveProduct> Results { get; set; } = [];
    public ResponseStatus? ResponseStatus { get; set; }
}

public class LiveProduct
{
    public string ProductId { get; set; } = "";
    public string Base { get; set; } = "";
    public string Quote { get; set; } = "USD";
    public string Name { get; set; } = "";
    public decimal? Price { get; set; }
}

[Api("Public candles for one Coinbase product")]
[Tag("live")]
[ValidateIsAdmin]
[Route("/api/live/candles", "GET")]
public class GetLiveCandles : IGet, IReturn<GetLiveCandlesResponse>
{
    public string? Product { get; set; }

    [Description("1m, 5m, 15m, 1h, or 1d. Defaults to 5m.")]
    public string? Granularity { get; set; }
}

public class GetLiveCandlesResponse
{
    public string Product { get; set; } = "";
    public string Granularity { get; set; } = "5m";
    public List<LiveCandle> Results { get; set; } = [];
    public ResponseStatus? ResponseStatus { get; set; }
}

public class LiveCandle
{
    public long Start { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
}

[Api("Latest public price for one product")]
[Tag("live")]
[ValidateIsAdmin]
[Route("/api/live/ticker", "GET")]
public class GetLiveTicker : IGet, IReturn<GetLiveTickerResponse>
{
    public string? Product { get; set; }
}

public class GetLiveTickerResponse
{
    public LiveTick? Result { get; set; }
    public ResponseStatus? ResponseStatus { get; set; }
}

public class LiveTick
{
    public string ProductId { get; set; } = "";
    public decimal Price { get; set; }
    public DateTime At { get; set; }
}

[Api("Server-sent public prices for one product")]
[Tag("live")]
[ValidateIsAdmin]
[Route("/api/live/stream", "GET")]
public class StreamLiveTicker : IGet, IReturnVoid
{
    public string? Product { get; set; }
}

[Api("Create a paper wallet and its starting cash")]
[Tag("live")]
[ValidateIsAdmin]
[Route("/api/live/paper-wallets", "POST")]
public class CreatePaperWallet : IPost, IReturn<CreatePaperWalletResponse>
{
    public string? Name { get; set; }
    public decimal StartingCash { get; set; }
}

public class CreatePaperWalletResponse
{
    public Wallet Result { get; set; } = new();
    public decimal Cash { get; set; }
    public ResponseStatus? ResponseStatus { get; set; }
}

[Api("Lots, average quote, cash, and open orders for one wallet and product")]
[Tag("live")]
[ValidateIsAdmin]
[Route("/api/live/book", "GET")]
public class GetLiveBook : IGet, IReturn<LiveBookResponse>
{
    public string? Wallet { get; set; }
    public string? Symbol { get; set; }

    [Description("Quantity used to preview a sell at average. Defaults to the open quantity.")]
    public decimal? SellQty { get; set; }
}

public class LiveBookResponse
{
    public string Wallet { get; set; } = "";
    public string WalletName { get; set; } = "";
    public WalletMode Mode { get; set; }
    public bool Simulated { get; set; }
    public string? SimulationId { get; set; }
    public string Symbol { get; set; } = "";
    public decimal? Price { get; set; }
    public decimal Cash { get; set; }
    public decimal RealizedPnl { get; set; }
    public List<LiveLot> Lots { get; set; } = [];
    public LiveAverageQuote Average { get; set; } = new();
    public List<LiveOrder> Orders { get; set; } = [];
    public int? TradeId { get; set; }
    public ResponseStatus? ResponseStatus { get; set; }
}

public class LiveLot
{
    public int TradeId { get; set; }
    public string Symbol { get; set; } = "";
    public decimal Qty { get; set; }
    public decimal BuyPrice { get; set; }
    public decimal Cost { get; set; }
    public decimal? Price { get; set; }
    public decimal? UnrealizedPnl { get; set; }
    public decimal? UnrealizedPnlPercent { get; set; }
    public DateTime ExecutedAt { get; set; }
}

public class LiveAverageQuote
{
    public decimal OpenQty { get; set; }
    public decimal OpenCost { get; set; }
    public decimal AvgCost { get; set; }
    public decimal SellQty { get; set; }
    public decimal? Price { get; set; }
    public decimal MatchedCost { get; set; }
    public decimal? Profit { get; set; }
}

public class LiveOrder
{
    public int Id { get; set; }
    public string Symbol { get; set; } = "";
    public TradeSide Side { get; set; }
    public LiveOrderType Type { get; set; }
    public decimal Qty { get; set; }
    public decimal? TriggerPrice { get; set; }
    public CostingMethod Costing { get; set; }
    public int? LotId { get; set; }
    public LiveOrderStatus Status { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; }
}

[Api("Place a paper order. A real wallet is rejected.")]
[Tag("live")]
[ValidateIsAdmin]
[Route("/api/live/paper-orders", "POST")]
public class PlacePaperOrder : IPost, IReturn<LiveBookResponse>
{
    public string? Wallet { get; set; }
    public string? Symbol { get; set; }
    public TradeSide? Side { get; set; }
    public LiveOrderType? Type { get; set; }
    public decimal Qty { get; set; }
    public decimal? TriggerPrice { get; set; }
    public CostingMethod? Costing { get; set; }
    public int? LotId { get; set; }
    public string? ClientId { get; set; }
}

[Api("Cancel a resting paper order")]
[Tag("live")]
[ValidateIsAdmin]
[Route("/api/live/paper-orders/{Id}/cancel", "POST")]
public class CancelPaperOrder : IPost, IReturn<LiveBookResponse>
{
    public int Id { get; set; }
}

[Api("Move a resting paper order to a new trigger price")]
[Tag("live")]
[ValidateIsAdmin]
[Route("/api/live/paper-orders/{Id}/move", "POST")]
public class MovePaperOrder : IPost, IReturn<LiveBookResponse>
{
    public int Id { get; set; }
    public decimal TriggerPrice { get; set; }
}

[Api("Start an in-memory simulation seeded from a real wallet")]
[Tag("live")]
[ValidateIsAdmin]
[Route("/api/live/simulations", "POST")]
public class OpenLiveSimulation : IPost, IReturn<LiveBookResponse>
{
    public string? Wallet { get; set; }
    public string? Symbol { get; set; }
}

[Api("Read a simulation book")]
[Tag("live")]
[ValidateIsAdmin]
[Route("/api/live/simulations/{Id}", "GET")]
public class GetLiveSimulation : IGet, IReturn<LiveBookResponse>
{
    public string? Id { get; set; }
    public string? Symbol { get; set; }
    public decimal? SellQty { get; set; }
}

[Api("Place a simulated order. Nothing is written to the ledger.")]
[Tag("live")]
[ValidateIsAdmin]
[Route("/api/live/simulations/{Id}/orders", "POST")]
public class PlaceSimulationOrder : IPost, IReturn<LiveBookResponse>
{
    public string? Id { get; set; }
    public string? Symbol { get; set; }
    public TradeSide? Side { get; set; }
    public LiveOrderType? Type { get; set; }
    public decimal Qty { get; set; }
    public decimal? TriggerPrice { get; set; }
    public CostingMethod? Costing { get; set; }
    public int? LotId { get; set; }
}

[Api("Cancel a simulated order")]
[Tag("live")]
[ValidateIsAdmin]
[Route("/api/live/simulations/{Id}/orders/{OrderId}/cancel", "POST")]
public class CancelSimulationOrder : IPost, IReturn<LiveBookResponse>
{
    public string? Id { get; set; }
    public int OrderId { get; set; }
}

[Api("Move a simulated order to a new trigger price")]
[Tag("live")]
[ValidateIsAdmin]
[Route("/api/live/simulations/{Id}/orders/{OrderId}/move", "POST")]
public class MoveSimulationOrder : IPost, IReturn<LiveBookResponse>
{
    public string? Id { get; set; }
    public int OrderId { get; set; }
    public decimal TriggerPrice { get; set; }
}

[Api("Reset a simulation back to the wallet's saved holdings")]
[Tag("live")]
[ValidateIsAdmin]
[Route("/api/live/simulations/{Id}/reset", "POST")]
public class ResetLiveSimulation : IPost, IReturn<LiveBookResponse>
{
    public string? Id { get; set; }
    public string? Symbol { get; set; }
}
