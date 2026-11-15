using System.ComponentModel;
using ServiceStack;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.ServiceModel;

[Api("Overview totals per wallet and per mode. Amounts are in each wallet's base currency.")]
[Tag("pnl")]
[ValidateIsAdmin]
[Route("/api/summary", "GET")]
public class GetSummary : IGet, IReturn<GetSummaryResponse>
{
    [Description("all, paper, or real. Defaults to all. Totals and wallet cards use this filter. Modes always lists all three.")]
    public string? Mode { get; set; }

    [Description("Inclusive UTC start. Omit to start at the first recorded event.")]
    public DateTime? From { get; set; }

    [Description("Inclusive UTC end. Point-in-time totals are as of this time. Omit for the latest recorded event.")]
    public DateTime? To { get; set; }

    [Description("Wallet slug. Omit for every wallet in the mode.")]
    public string? Wallet { get; set; }

    [Description("Include archived wallets. A wallet slug always includes that wallet.")]
    public bool IncludeArchived { get; set; }
}

public class GetSummaryResponse
{
    public PnlTotals Totals { get; set; } = new();
    public List<ModePnl> Modes { get; set; } = [];
    public List<WalletCard> Wallets { get; set; } = [];
    public ResponseStatus? ResponseStatus { get; set; }
}

public class ModePnl
{
    [Description("all, paper, or real.")]
    public string Mode { get; set; } = "";

    public PnlTotals Totals { get; set; } = new();
}

public class PnlTotals
{
    public int Wallets { get; set; }
    public string? BaseCurrency { get; set; }
    public bool MixedCurrencies { get; set; }

    [Description("Cash plus open positions. Unmarked positions are included at cost.")]
    public decimal TotalValue { get; set; }

    [Description("Change in total value across the range. Includes deposits, withdrawals, and sweeps.")]
    public decimal ValueChange { get; set; }

    public decimal Cash { get; set; }
    public decimal Swept { get; set; }

    [Description("Open quantity marked at the latest applicable price. Unmarked quantity is not included here.")]
    public decimal OpenExposure { get; set; }

    [Description("Cost of open positions that have no price mark. This is not zero P&L.")]
    public decimal UnmarkedCost { get; set; }

    public decimal PositionsValue { get; set; }
    public bool ValuedAtCost { get; set; }

    [Description("Realized P&L in the wallet base currency for sells in the range, after fees.")]
    public decimal RealizedPnl { get; set; }

    [Description("Unrealized P&L as of the end of the range. Null when an open position has no price mark.")]
    public decimal? UnrealizedPnl { get; set; }

    [Description("Change in unrealized P&L across the range. Null when either end is unknown.")]
    public decimal? UnrealizedChange { get; set; }

    [Description("Realized P&L plus unrealized change. Null when unrealized change is unknown. Transfers are not included.")]
    public decimal? Pnl { get; set; }

    [Description("Fees in the wallet base currency for fills in the range.")]
    public decimal Fees { get; set; }

    public decimal Deposits { get; set; }
    public decimal Withdrawals { get; set; }
    public decimal Sweeps { get; set; }

    [Description("Percent of sell fills in the range with positive realized P&L. Breakeven sells are excluded. Null when no sell has a gain or a loss.")]
    public decimal? WinRate { get; set; }

    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Scratches { get; set; }
    public int TradeCount { get; set; }
    public int OpenPositions { get; set; }
}

public class WalletCard
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public WalletMode Mode { get; set; }
    public string BaseCurrency { get; set; } = "USD";
    public bool IsArchived { get; set; }
    public int SortOrder { get; set; }
    public decimal TotalValue { get; set; }
    public decimal ValueChange { get; set; }
    public decimal Cash { get; set; }
    public decimal Swept { get; set; }
    public decimal OpenExposure { get; set; }
    public decimal UnmarkedCost { get; set; }
    public decimal PositionsValue { get; set; }
    public bool ValuedAtCost { get; set; }
    public decimal RealizedPnl { get; set; }
    public decimal? UnrealizedPnl { get; set; }
    public decimal? UnrealizedChange { get; set; }
    public decimal? Pnl { get; set; }
    public decimal Fees { get; set; }
    public decimal Deposits { get; set; }
    public decimal Withdrawals { get; set; }
    public decimal Sweeps { get; set; }
    public decimal? WinRate { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Scratches { get; set; }
    public int TradeCount { get; set; }
    public int OpenPositions { get; set; }
    public DateTime? LastActivityAt { get; set; }
    public List<EquityPoint> Sparkline { get; set; } = [];

    [Description("PredictionMarket for a pm- contract wallet. Empty for other wallets.")]
    public string Kind { get; set; } = "";

    [Description("open, closed, settled-0, settled-1, settled, or none. Empty when this is not a prediction wallet.")]
    public string Settlement { get; set; } = "";

    [Description("Lifetime realized P&L for a prediction wallet. The range figure stays on RealizedPnl.")]
    public decimal LifetimeRealizedPnl { get; set; }

    [Description("Confirmed contract close, UTC. Null when the market lookup did not return one.")]
    public DateTime? CloseAt { get; set; }
}

[Api("Equity curve from recorded trades, transfers, and price marks")]
[Tag("pnl")]
[ValidateIsAdmin]
[Route("/api/equity", "GET")]
public class GetEquity : IGet, IReturn<GetEquityResponse>
{
    [Description("Wallet slug. Omit for every wallet.")]
    public string? Wallet { get; set; }

    [Description("all, paper, or real. Defaults to all.")]
    public string? Mode { get; set; }

    [Description("Inclusive UTC start.")]
    public DateTime? From { get; set; }

    [Description("Inclusive UTC end.")]
    public DateTime? To { get; set; }

    public bool IncludeArchived { get; set; }
}

public class GetEquityResponse
{
    public List<WalletEquity> Wallets { get; set; } = [];
    public List<EquityPoint> Total { get; set; } = [];
    public ResponseStatus? ResponseStatus { get; set; }
}

public class WalletEquity
{
    public string Wallet { get; set; } = "";
    public string Name { get; set; } = "";
    public WalletMode Mode { get; set; }
    public List<EquityPoint> Points { get; set; } = [];
}

public class EquityPoint
{
    public DateTime At { get; set; }
    public decimal TotalValue { get; set; }
    public decimal Cash { get; set; }
    public decimal PositionsValue { get; set; }
    public decimal OpenExposure { get; set; }
    public decimal UnmarkedCost { get; set; }
    public decimal Swept { get; set; }
    public decimal? UnrealizedPnl { get; set; }
    public bool ValuedAtCost { get; set; }
}

[Api("Daily P&L from recorded trades, fees, and price marks. The pnl_by_day tool calls this request.")]
[Tag("pnl")]
[ValidateIsAdmin]
[Route("/api/pnl/daily", "GET")]
public class PnlByDay : IGet, IReturn<PnlByDayResponse>
{
    [Description("Wallet slug. Omit for every wallet.")]
    public string? Wallet { get; set; }

    [Description("Inclusive UTC start.")]
    public DateTime? From { get; set; }

    [Description("Inclusive UTC end.")]
    public DateTime? To { get; set; }

    [Description("all, paper, or real. Defaults to all.")]
    public string? Mode { get; set; }

    [Description("Include archived wallets. A wallet slug always includes that wallet.")]
    public bool IncludeArchived { get; set; }
}

public class PnlByDayResponse
{
    [Description("One row per UTC day, summed across the selected wallets.")]
    public List<PnlDayResult> Results { get; set; } = [];

    public List<WalletPnlDays> Wallets { get; set; } = [];
    public ResponseStatus? ResponseStatus { get; set; }
}

public class WalletPnlDays
{
    public string Wallet { get; set; } = "";
    public string Name { get; set; } = "";
    public WalletMode Mode { get; set; }
    public List<PnlDayResult> Days { get; set; } = [];
}

public class PnlDayResult
{
    [Description("UTC midnight of the day.")]
    public DateTime Date { get; set; }

    [Description("Realized P&L in the wallet base currency for sells on this day, after fees.")]
    public decimal RealizedPnl { get; set; }

    [Description("Change in unrealized P&L during the day. Null when either end is unknown.")]
    public decimal? UnrealizedChange { get; set; }

    [Description("Realized P&L plus unrealized change. Null when unrealized change is unknown. Transfers are not included.")]
    public decimal? Pnl { get; set; }

    [Description("Fees in the wallet base currency for fills on this day.")]
    public decimal Fees { get; set; }

    public decimal Deposits { get; set; }
    public decimal Withdrawals { get; set; }
    public decimal Sweeps { get; set; }

    [Description("Total value at the end of the day. Unmarked positions are included at cost.")]
    public decimal TotalValue { get; set; }

    public decimal ValueChange { get; set; }
    public decimal Cash { get; set; }
    public decimal PositionsValue { get; set; }
    public decimal OpenExposure { get; set; }
    public decimal UnmarkedCost { get; set; }
    public bool ValuedAtCost { get; set; }
    public int Trades { get; set; }
}

[Api("P&L by symbol from recorded trades, fees, and price marks")]
[Tag("pnl")]
[ValidateIsAdmin]
[Route("/api/pnl/symbols", "GET")]
public class GetPnlBySymbol : IGet, IReturn<GetPnlBySymbolResponse>
{
    public string? Wallet { get; set; }
    public string? Mode { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public bool IncludeArchived { get; set; }

    [Description("Maximum rows after sorting by P&L. Omit for every symbol.")]
    public int? Take { get; set; }
}

public class GetPnlBySymbolResponse
{
    public List<SymbolPnl> Results { get; set; } = [];
    public ResponseStatus? ResponseStatus { get; set; }
}

public class SymbolPnl
{
    public string Symbol { get; set; } = "";
    public decimal RealizedPnl { get; set; }

    [Description("Unrealized P&L as of the end of the range. Null when an open position has no price mark.")]
    public decimal? UnrealizedPnl { get; set; }

    public decimal? UnrealizedChange { get; set; }

    [Description("Realized P&L plus unrealized change. Null when unrealized change is unknown.")]
    public decimal? Pnl { get; set; }

    public decimal Fees { get; set; }
    public int Trades { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public decimal Qty { get; set; }
    public decimal MarketValue { get; set; }
    public bool ValuedAtCost { get; set; }
}

[Api("Fees over time from recorded fills")]
[Tag("pnl")]
[ValidateIsAdmin]
[Route("/api/fees", "GET")]
public class GetFees : IGet, IReturn<GetFeesResponse>
{
    public string? Wallet { get; set; }
    public string? Mode { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public bool IncludeArchived { get; set; }
}

public class GetFeesResponse
{
    [Description("Fees in the wallet base currency for fills in the range.")]
    public decimal Total { get; set; }

    public List<FeeDay> Days { get; set; } = [];
    public ResponseStatus? ResponseStatus { get; set; }
}

public class FeeDay
{
    public DateTime Date { get; set; }
    public decimal Fees { get; set; }
}

[Api("Open and closed positions from the ledger")]
[Tag("pnl")]
[ValidateIsAdmin]
[Route("/api/positions", "GET")]
public class QueryPositions : IGet, IReturn<QueryPositionsResponse>
{
    public string? Wallet { get; set; }
    public string? Mode { get; set; }
    public string? Symbol { get; set; }

    [Description("open, closed, or omit for both.")]
    public string? Status { get; set; }

    public bool IncludeArchived { get; set; }
}

public class QueryPositionsResponse
{
    public int MarkStaleAfterMinutes { get; set; }
    public List<PositionView> Results { get; set; } = [];
    public ResponseStatus? ResponseStatus { get; set; }
}

public class PositionView
{
    public string Wallet { get; set; } = "";
    public string WalletName { get; set; } = "";
    public WalletMode Mode { get; set; }
    public string Symbol { get; set; } = "";

    [Description("open or closed.")]
    public string Status { get; set; } = "";

    public decimal Qty { get; set; }

    [Description("Average cost per unit, including buy fees. Quote currency per unit.")]
    public decimal AvgPrice { get; set; }

    public decimal CostBasis { get; set; }
    public decimal? LastMarkPrice { get; set; }
    public DateTime? LastMarkAt { get; set; }
    public bool MarkIsStale { get; set; }
    public int MarkStaleAfterMinutes { get; set; }

    [Description("Null when the position is open and has no price mark.")]
    public decimal? UnrealizedPnl { get; set; }

    public decimal? UnrealizedPnlPercent { get; set; }
    public decimal RealizedPnl { get; set; }
    public decimal FeesPaid { get; set; }
    public decimal MarketValue { get; set; }
    public bool ValuedAtCost { get; set; }
    public decimal? TargetPrice { get; set; }

    [Description("Target minus the latest mark, in quote currency per unit. Null when either price is missing.")]
    public decimal? DistanceToTarget { get; set; }

    public decimal? DistanceToTargetPercent { get; set; }
}

[Api("One trade with its matched fills and the price marks used to chart it")]
[Tag("pnl")]
[ValidateIsAdmin]
[Route("/api/trades/{Id}", "GET")]
public class GetTrade : IGet, IReturn<GetTradeResponse>
{
    public int Id { get; set; }
}

public class GetTradeResponse
{
    public TradeView Result { get; set; } = new();
    public List<TradeFillView> Fills { get; set; } = [];
    public List<ChartMarker> Markers { get; set; } = [];
    public List<PriceMarkView> PriceMarks { get; set; } = [];
    public bool HasPriceMarks { get; set; }
    public DateTime ChartFrom { get; set; }
    public DateTime ChartTo { get; set; }
    public ResponseStatus? ResponseStatus { get; set; }
}

public class TradeFillView
{
    public int TradeId { get; set; }
    public TradeSide Side { get; set; }
    public string Symbol { get; set; } = "";
    public decimal Qty { get; set; }

    [Description("Counterparty fill price in quote currency per unit.")]
    public decimal Price { get; set; }

    [Description("Allocated buy fee in the wallet base currency.")]
    public decimal BuyFee { get; set; }

    [Description("Allocated sell fee in the wallet base currency.")]
    public decimal SellFee { get; set; }

    [Description("Matched cost in the wallet base currency, including allocated buy fees.")]
    public decimal Cost { get; set; }

    [Description("Sale proceeds in the wallet base currency, after the allocated sell fee.")]
    public decimal Proceeds { get; set; }

    public decimal RealizedPnl { get; set; }
    public DateTime ExecutedAt { get; set; }
}

public class ChartMarker
{
    public int TradeId { get; set; }
    public TradeSide Side { get; set; }
    public DateTime At { get; set; }

    [Description("Recorded fill price in quote currency per unit.")]
    public decimal Price { get; set; }

    public decimal Qty { get; set; }
}

[Api("Recompute positions and balance snapshots from recorded trades, transfers, and price marks")]
[Tag("pnl")]
[ValidateIsAdmin]
[Route("/projections/rebuild", "POST")]
public class RebuildProjections : IPost, IReturn<RebuildProjectionsResponse>
{
    [Description("Wallet slug. Omit to rebuild every wallet.")]
    public string? Wallet { get; set; }
}

public class RebuildProjectionsResponse
{
    public int Wallets { get; set; }
    public int Positions { get; set; }
    public int Snapshots { get; set; }
    public ResponseStatus? ResponseStatus { get; set; }
}

[Api("Realized P&L for prediction-market wallets, per contract and in total.")]
[Tag("pnl")]
[ValidateIsAdmin]
[Route("/api/predictions", "GET")]
public class GetPredictionPnl : IGet, IReturn<GetPredictionPnlResponse>
{
    [Description("Include archived prediction wallets. Defaults to false.")]
    public bool IncludeArchived { get; set; }
}

public class GetPredictionPnlResponse
{
    public List<PredictionPnlRow> Wallets { get; set; } = [];
    public PredictionPnlTotal Total { get; set; } = new();
    public ResponseStatus? ResponseStatus { get; set; }
}

public class PredictionPnlRow
{
    public string Wallet { get; set; } = "";
    public string Name { get; set; } = "";
    public string Symbol { get; set; } = "";
    public decimal Bought { get; set; }
    public decimal Sold { get; set; }
    public decimal Fees { get; set; }
    public decimal RealizedPnl { get; set; }

    [Description("open, closed, settled-0, settled-1, settled, or none.")]
    public string Settlement { get; set; } = "";

    [Description("Confirmed contract close, UTC. Null when the market lookup did not return one.")]
    public DateTime? CloseAt { get; set; }
}

public class PredictionPnlTotal
{
    public int Wallets { get; set; }
    public decimal Bought { get; set; }
    public decimal Sold { get; set; }
    public decimal Fees { get; set; }
    public decimal RealizedPnl { get; set; }
}
