using System.ComponentModel;
using ServiceStack;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor.ServiceModel;

[Api("Create an agent-defined wallet")]
[Tag("ledger")]
[ValidateIsAdmin]
[Route("/wallets", "POST")]
public class CreateWallet : IPost, IReturn<CreateWalletResponse>
{
    [Description("Unique slug chosen by the agent.")]
    public string? Key { get; set; }

    [Description("Display name.")]
    public string? Name { get; set; }

    [Description("Paper or Real. Immutable after create.")]
    public WalletMode? Mode { get; set; }

    [Description("Free-text venue, for example Coinbase.")]
    public string? Venue { get; set; }

    [Description("Base currency code. Defaults to USD.")]
    public string? BaseCurrency { get; set; }

    [Description("Optional note.")]
    public string? Note { get; set; }
}

public class CreateWalletResponse
{
    public Wallet Result { get; set; } = new();
    public bool AlreadyExists { get; set; }
    public ResponseStatus? ResponseStatus { get; set; }
}

[Api("Update wallet name, note, sort order, or archive flag")]
[Tag("ledger")]
[ValidateIsAdmin]
[Route("/wallets/{Key}", "PATCH")]
public class UpdateWallet : IPatch, IReturn<UpdateWalletResponse>
{
    [Description("Wallet slug. Key and mode cannot be changed.")]
    public string? Key { get; set; }

    [Description("New display name. Omit to leave it unchanged.")]
    public string? Name { get; set; }

    [Description("New note. An empty string clears it. Omit to leave it unchanged.")]
    public string? Note { get; set; }

    [Description("Sort order. Omit to leave it unchanged.")]
    public int? SortOrder { get; set; }

    [Description("Archive or restore the wallet. Omit to leave it unchanged.")]
    public bool? IsArchived { get; set; }
}

public class UpdateWalletResponse
{
    public Wallet Result { get; set; } = new();
    public ResponseStatus? ResponseStatus { get; set; }
}

[Api("Get one wallet by slug")]
[Tag("ledger")]
[ValidateIsAdmin]
[Route("/api/wallets/{Key}", "GET")]
public class GetWallet : IGet, IReturn<GetWalletResponse>
{
    public string? Key { get; set; }
}

public class GetWalletResponse
{
    public Wallet Result { get; set; } = new();
    public ResponseStatus? ResponseStatus { get; set; }
}

[Api("List agent-defined wallets")]
[Tag("ledger")]
[ValidateIsAdmin]
[Route("/api/wallets", "GET")]
public class QueryWallets : IGet, IReturn<QueryWalletsResponse>
{
    [Description("Include archived wallets. Defaults to false.")]
    public bool IncludeArchived { get; set; }
}

public class QueryWalletsResponse
{
    public List<Wallet> Results { get; set; } = [];
    public ResponseStatus? ResponseStatus { get; set; }
}

[Api("Cash, swept amount, fees, and open positions for one wallet")]
[Tag("ledger")]
[ValidateIsAdmin]
[Route("/api/wallets/{Wallet}/balances", "GET")]
public class GetWalletBalances : IGet, IReturn<GetWalletBalancesResponse>
{
    [Description("Wallet slug.")]
    public string? Wallet { get; set; }
}

public class GetWalletBalancesResponse
{
    public WalletBalances Result { get; set; } = new();
    public ResponseStatus? ResponseStatus { get; set; }
}

public class WalletBalances
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public WalletMode Mode { get; set; }
    public string BaseCurrency { get; set; } = "USD";
    public bool IsArchived { get; set; }
    public decimal Cash { get; set; }
    public decimal Swept { get; set; }
    public decimal Fees { get; set; }
    public decimal RealizedPnl { get; set; }

    [Description("Null when an open position has no price mark. Zero when nothing is open.")]
    public decimal? UnrealizedPnl { get; set; }

    public decimal PositionsValue { get; set; }
    public decimal TotalValue { get; set; }
    public bool CashIsNegative { get; set; }
    public bool HasDeposits { get; set; }

    [Description("True when an open position is valued at cost because it has no price mark.")]
    public bool ValuedAtCost { get; set; }

    public List<PositionBalance> Positions { get; set; } = [];
}

public class PositionBalance
{
    public string Symbol { get; set; } = "";
    public decimal Qty { get; set; }
    public decimal CostBasis { get; set; }
    public decimal AvgPrice { get; set; }
    public decimal? LastMarkPrice { get; set; }
    public DateTime? LastMarkAt { get; set; }
    public decimal? UnrealizedPnl { get; set; }

    [Description("Unrealized P&L as a percent of cost basis. Null when unmarked or cost is zero.")]
    public decimal? UnrealizedPnlPercent { get; set; }

    public decimal MarketValue { get; set; }
    public bool ValuedAtCost { get; set; }
    public decimal RealizedPnl { get; set; }
    public decimal FeesPaid { get; set; }
}

[Api("Trades and transfers for one wallet, newest first")]
[Tag("ledger")]
[ValidateIsAdmin]
[Route("/api/wallets/{Wallet}/history", "GET")]
public class QueryWalletHistory : IGet, IReturn<QueryWalletHistoryResponse>
{
    public string? Wallet { get; set; }

    [Description("Inclusive UTC start.")]
    public DateTime? From { get; set; }

    [Description("Inclusive UTC end.")]
    public DateTime? To { get; set; }

    [Description("buy, sell, deposit, withdrawal, or sweep.")]
    public string? Kind { get; set; }

    [Description("Page size. Defaults to 100. Maximum 1000.")]
    public int? Take { get; set; }

    public int? Skip { get; set; }
}

public class QueryWalletHistoryResponse
{
    public int Total { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
    public List<WalletHistoryEntry> Results { get; set; } = [];
    public ResponseStatus? ResponseStatus { get; set; }
}

public class WalletHistoryEntry
{
    public string Wallet { get; set; } = "";
    public string Kind { get; set; } = "";
    public int Id { get; set; }
    public string ClientId { get; set; } = "";
    public DateTime At { get; set; }
    public string? Symbol { get; set; }
    public TradeSide? Side { get; set; }
    public decimal? Qty { get; set; }
    public decimal? Price { get; set; }
    public decimal? Fee { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public string? Title { get; set; }
    public string? Note { get; set; }
    public string? OrderRef { get; set; }
    public decimal? TargetPrice { get; set; }
    public DateTime RecordedAt { get; set; }
}

[Api("Record a fill. The same wallet and client id returns the original fill.")]
[Tag("ledger")]
[ValidateIsAdmin]
[Route("/wallets/{Wallet}/trades", "POST")]
public class RecordTrade : IPost, IReturn<RecordTradeResponse>
{
    [Description("Wallet slug.")]
    public string? Wallet { get; set; }

    [Description("Idempotency key from the agent.")]
    public string? ClientId { get; set; }

    [Description("Instrument symbol, for example BTC-USD.")]
    public string? Symbol { get; set; }

    [Description("Buy or Sell.")]
    public TradeSide? Side { get; set; }

    [Description("Quantity of the instrument. Must be greater than zero.")]
    public decimal Qty { get; set; }

    [Description("Quote currency per unit, in the wallet base currency.")]
    public decimal Price { get; set; }

    [Description("Fee in the wallet base currency. Zero when the fill had no fee.")]
    public decimal Fee { get; set; }

    [Description("Fee currency. Defaults to the wallet base currency and must match it.")]
    public string? FeeCurrency { get; set; }

    [Description("UTC time of the fill. Defaults to now.")]
    public DateTime? ExecutedAt { get; set; }

    public string? Title { get; set; }
    public string? OrderRef { get; set; }

    [Description("Optional target price in quote currency per unit.")]
    public decimal? TargetPrice { get; set; }

    public string? Note { get; set; }
}

public class RecordTradeResponse
{
    public Trade Result { get; set; } = new();
    public bool AlreadyRecorded { get; set; }
    public WalletBalances Balance { get; set; } = new();
    public ResponseStatus? ResponseStatus { get; set; }
}

[Api("Record a deposit, withdrawal, or sweep")]
[Tag("ledger")]
[ValidateIsAdmin]
[Route("/wallets/{Wallet}/transfers", "POST")]
public class RecordTransfer : IPost, IReturn<RecordTransferResponse>
{
    public string? Wallet { get; set; }

    [Description("Idempotency key from the agent.")]
    public string? ClientId { get; set; }

    [Description("Deposit, Withdrawal, or Sweep.")]
    public TransferKind? Kind { get; set; }

    [Description("Amount in the wallet base currency. Must be greater than zero.")]
    public decimal Amount { get; set; }

    [Description("Currency code. Defaults to the wallet base currency and must match it.")]
    public string? Currency { get; set; }

    [Description("UTC time of the transfer. Defaults to now.")]
    public DateTime? At { get; set; }

    public string? Note { get; set; }
}

public class RecordTransferResponse
{
    public Transfer Result { get; set; } = new();
    public bool AlreadyRecorded { get; set; }
    public WalletBalances Balance { get; set; } = new();
    public ResponseStatus? ResponseStatus { get; set; }
}

[Api("Record one price mark")]
[Tag("ledger")]
[ValidateIsAdmin]
[Route("/price-marks", "POST")]
public class RecordPriceMark : IPost, IReturn<RecordPriceMarkResponse>
{
    [Description("Instrument symbol, for example BTC-USD.")]
    public string? Symbol { get; set; }

    [Description("Quote currency per unit.")]
    public decimal Price { get; set; }

    [Description("UTC time of the mark. Defaults to now.")]
    public DateTime? At { get; set; }

    [Description("Wallet slug. Omit for a mark that applies to every wallet.")]
    public string? Wallet { get; set; }

    [Description("Optional idempotency key. A repeat with the same values returns the original mark.")]
    public string? ClientId { get; set; }

    [Description("Free-text source, for example agent.")]
    public string? Source { get; set; }
}

public class RecordPriceMarkResponse
{
    public PriceMark Result { get; set; } = new();
    public bool AlreadyRecorded { get; set; }
    public ResponseStatus? ResponseStatus { get; set; }
}

[Api("Record several price marks in one call")]
[Tag("ledger")]
[ValidateIsAdmin]
[Route("/price-marks/batch", "POST")]
public class RecordPriceMarks : IPost, IReturn<RecordPriceMarksResponse>
{
    public List<PriceMarkWrite>? Marks { get; set; }
}

public class PriceMarkWrite
{
    public string? Symbol { get; set; }
    public decimal Price { get; set; }
    public DateTime? At { get; set; }
    public string? Wallet { get; set; }
    public string? ClientId { get; set; }
    public string? Source { get; set; }
}

public class RecordPriceMarksResponse
{
    public List<RecordedPriceMark> Results { get; set; } = [];
    public ResponseStatus? ResponseStatus { get; set; }
}

public class RecordedPriceMark
{
    public PriceMark Result { get; set; } = new();
    public bool AlreadyRecorded { get; set; }
}

[Api("Query recorded price marks, newest first")]
[Tag("ledger")]
[ValidateIsAdmin]
[Route("/api/price-marks", "GET")]
public class QueryPriceMarks : IGet, IReturn<QueryPriceMarksResponse>
{
    public string? Symbol { get; set; }

    [Description("When set, returns that wallet's marks and global marks.")]
    public string? Wallet { get; set; }

    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int? Take { get; set; }
    public int? Skip { get; set; }
}

public class QueryPriceMarksResponse
{
    public int Total { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
    public List<PriceMarkView> Results { get; set; } = [];
    public ResponseStatus? ResponseStatus { get; set; }
}

public class PriceMarkView
{
    public int Id { get; set; }
    public string? ClientId { get; set; }
    public string Symbol { get; set; } = "";
    public decimal Price { get; set; }
    public DateTime At { get; set; }
    public string? Source { get; set; }
    public string? Wallet { get; set; }
    public int? WalletId { get; set; }
    public DateTime RecordedAt { get; set; }
}

[Api("Query recorded trades, newest first")]
[Tag("ledger")]
[ValidateIsAdmin]
[Route("/api/trades", "GET")]
public class QueryTrades : IGet, IReturn<QueryTradesResponse>
{
    public string? Wallet { get; set; }
    public string? Symbol { get; set; }
    public TradeSide? Side { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    [Description("all, paper, or real. Defaults to all.")]
    public string? Mode { get; set; }

    [Description("won, lost, open, or closed.")]
    public string? Outcome { get; set; }

    [Description("Case-insensitive match on symbol, title, note, wallet, client id, or order ref.")]
    public string? Q { get; set; }

    [Description("time, symbol, wallet, pnl, fee, qty, or price. Defaults to time.")]
    public string? OrderBy { get; set; }

    [Description("Defaults to true.")]
    public bool? Descending { get; set; }

    public int? Take { get; set; }
    public int? Skip { get; set; }
}

public class QueryTradesResponse
{
    public int Total { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
    public List<TradeView> Results { get; set; } = [];
    public ResponseStatus? ResponseStatus { get; set; }
}

public class TradeView
{
    public int Id { get; set; }
    public string Wallet { get; set; } = "";
    public string WalletName { get; set; } = "";
    public WalletMode Mode { get; set; }
    public int WalletId { get; set; }
    public string ClientId { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string? Title { get; set; }
    public TradeSide Side { get; set; }
    public decimal Qty { get; set; }
    public decimal Price { get; set; }
    public decimal Fee { get; set; }
    public string? FeeCurrency { get; set; }
    public DateTime ExecutedAt { get; set; }
    public string? OrderRef { get; set; }
    public decimal? TargetPrice { get; set; }
    public string? Note { get; set; }
    public DateTime RecordedAt { get; set; }

    [Description("Fill price for a buy. Quantity-weighted buy price for a sell, before fees. Quote currency per unit.")]
    public decimal BuyPrice { get; set; }

    [Description("Exit price when the fill is closed, latest mark when it is open, null when an open fill has no mark. Quote currency per unit.")]
    public decimal? CurrentOrExitPrice { get; set; }

    [Description("exit, mark, or unknown.")]
    public string PriceKind { get; set; } = "";

    [Description("open or closed.")]
    public string Status { get; set; } = "";

    [Description("Weighted exit price of the closed quantity. Null when none of the fill has been sold.")]
    public decimal? ExitPrice { get; set; }

    public decimal? LastMarkPrice { get; set; }
    public DateTime? LastMarkAt { get; set; }

    [Description("Matched cost of closed quantity in the wallet base currency, including allocated buy fees.")]
    public decimal MatchedCost { get; set; }

    [Description("Proceeds of closed quantity in the wallet base currency, after allocated sell fees.")]
    public decimal Proceeds { get; set; }

    public decimal RealizedPnl { get; set; }

    [Description("Null when quantity is still open and the symbol has no price mark.")]
    public decimal? UnrealizedPnl { get; set; }

    [Description("Realized plus unrealized P&L. Null when unrealized P&L is unknown.")]
    public decimal? Pnl { get; set; }

    [Description("P&L as a percent of cost. Null when P&L is unknown or cost is zero.")]
    public decimal? PnlPercent { get; set; }

    public decimal RemainingQty { get; set; }
    public bool ValuedAtCost { get; set; }
}
