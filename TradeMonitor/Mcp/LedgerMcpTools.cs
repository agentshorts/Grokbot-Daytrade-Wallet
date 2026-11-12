using System.ComponentModel;
using ModelContextProtocol.Server;
using TradeMonitor.ServiceModel;
using TradeMonitor.ServiceModel.Types;

namespace TradeMonitor;

public sealed class PriceMarkInput
{
    [Description("Instrument symbol, for example BTC-USD.")]
    public string? symbol { get; set; }

    [Description("Quote currency per unit.")]
    public decimal price { get; set; }

    [Description("UTC ISO-8601 time of the mark. Omit to use the server time.")]
    public DateTime? at { get; set; }

    [Description("Wallet slug. Omit for a mark that applies to every wallet.")]
    public string? wallet { get; set; }

    [Description("Idempotency key. The same client_id returns the original mark.")]
    public string? client_id { get; set; }

    [Description("Free-text source, for example agent.")]
    public string? source { get; set; }
}

/// <summary>
/// Thin MCP wrappers over the ledger services. Nothing here calls the network or places an order.
/// </summary>
[McpServerToolType]
public class LedgerMcpTools(IHttpContextAccessor httpContextAccessor) : McpServiceBase(httpContextAccessor)
{
    [McpServerTool(Name = "wallet_create", Idempotent = true)]
    [Description(
        "Create a wallet the agent will record into. key is a unique slug. " +
        "mode is Paper or Real and cannot change later. Paper is for simulated fills. Real is a hand-kept record of a venue account. " +
        "base_currency is an ISO currency code and defaults to USD. Amounts later recorded on this wallet use that currency. " +
        "The same key and the same values returns the existing wallet. The same key with different values is rejected. " +
        "This does not open an account or place an order.")]
    public Task<CreateWalletResponse> WalletCreate(
        [Description("Unique wallet slug. Letters, numbers, and hyphens. This is the id other tools use.")] string key,
        [Description("Display name.")] string name,
        [Description("Paper or Real. Immutable after create.")] string mode,
        [Description("Free-text venue, for example Coinbase. Omit when there is no venue.")] string? venue = null,
        [Description("Base currency code. Defaults to USD.")] string? base_currency = null,
        [Description("Optional note.")] string? note = null)
    {
        return SendAsync(new CreateWallet
        {
            Key = key,
            Name = name,
            Mode = ParseEnum<WalletMode>(mode, "Wallet mode", "Paper or Real"),
            Venue = venue,
            BaseCurrency = base_currency,
            Note = note,
        });
    }

    [McpServerTool(Name = "wallet_update", Idempotent = true)]
    [Description(
        "Change a wallet's display name, note, sort order, or archive flag. " +
        "The slug and mode stay as they were. Omit a field to leave it unchanged. " +
        "An empty note clears the note. Archived wallets stay in the ledger and drop out of wallets_list unless include_archived is set.")]
    public Task<UpdateWalletResponse> WalletUpdate(
        [Description("Wallet slug. The key cannot be changed.")] string key,
        [Description("New display name. Omit to leave it unchanged.")] string? name = null,
        [Description("New note. An empty string clears it. Omit to leave it unchanged.")] string? note = null,
        [Description("Sort order. Lower numbers come first. Omit to leave it unchanged.")] int? sort_order = null,
        [Description("True archives the wallet. False restores it. Omit to leave it unchanged.")] bool? archived = null)
    {
        return SendAsync(new UpdateWallet
        {
            Key = key,
            Name = name,
            Note = note,
            SortOrder = sort_order,
            IsArchived = archived,
        });
    }

    [McpServerTool(Name = "trade_record", Idempotent = true)]
    [Description(
        "Record a fill or settlement that already happened. This does not place an order. " +
        "qty is the instrument quantity and must be greater than zero. " +
        "price is quote currency per unit, in the wallet base currency (USD unless the wallet uses another code). " +
        "A buy price must be greater than zero. A sell price may be zero when a contract settles or expires worthless. A negative price is rejected. " +
        "fee is in that same currency and may be zero. " +
        "kind is fill or settlement. Omit it for a fill. Use settlement when the contract settled, including a loss at price 0. " +
        "executed_at is the fill time as UTC ISO-8601. Omit it to use the server time. " +
        "client_id is the idempotency key. The same wallet and client_id returns the original row. " +
        "A repeat with different qty, price, fee, symbol, side, or kind is rejected. " +
        "A buy is rejected when qty times price, plus fee, exceeds cash by more than 0.000000001. A smaller shortfall is accepted and cash is recorded as 0. " +
        "A sell larger than the open quantity is rejected as an oversell. An unknown wallet is rejected.")]
    public Task<RecordTradeResponse> TradeRecord(
        [Description("Wallet slug.")] string wallet,
        [Description("Idempotency key. Reuse it to retry the same fill.")] string client_id,
        [Description("Instrument symbol, for example BTC-USD.")] string symbol,
        [Description("Buy or Sell.")] string side,
        [Description("Quantity of the instrument. Must be greater than zero.")] decimal qty,
        [Description("Quote currency per unit. Zero is allowed on a sell. A buy must be greater than zero.")] decimal price,
        [Description("Fee in the wallet base currency. Use 0 when the fill had no fee.")] decimal fee,
        [Description("UTC ISO-8601 time of the fill. Omit to use the server time.")] DateTime? executed_at = null,
        [Description("Short title for the fill.")] string? title = null,
        [Description("Broker or venue order reference.")] string? order_ref = null,
        [Description("Optional target price, quote currency per unit.")] decimal? target_price = null,
        [Description("Optional note.")] string? note = null,
        [Description("fill or settlement. Omit for a fill.")] string? kind = null)
    {
        return SendAsync(new RecordTrade
        {
            Wallet = wallet,
            ClientId = client_id,
            Symbol = symbol,
            Side = ParseEnum<TradeSide>(side, "Trade side", "Buy or Sell"),
            Qty = qty,
            Price = price,
            Fee = fee,
            ExecutedAt = executed_at,
            Title = title,
            OrderRef = order_ref,
            TargetPrice = target_price,
            Note = note,
            Kind = string.IsNullOrWhiteSpace(kind) ? null : ParseEnum<TradeKind>(kind, "Trade kind", "fill or settlement"),
        });
    }

    [McpServerTool(Name = "transfer_record", Idempotent = true)]
    [Description(
        "Record cash moving in the wallet base currency (USD unless the wallet uses another code). " +
        "kind is deposit, withdrawal, or sweep. amount must be greater than zero and is in that currency. " +
        "at is the transfer time as UTC ISO-8601. Omit it to use the server time. " +
        "client_id is the idempotency key. The same wallet and client_id returns the original transfer. " +
        "A repeat with a different amount or kind is rejected. " +
        "A withdrawal or sweep larger than cash is rejected. An unknown wallet is rejected. " +
        "This does not send or receive money at a venue.")]
    public Task<RecordTransferResponse> TransferRecord(
        [Description("Wallet slug.")] string wallet,
        [Description("Idempotency key. Reuse it to retry the same transfer.")] string client_id,
        [Description("deposit, withdrawal, or sweep.")] string kind,
        [Description("Amount in the wallet base currency. Must be greater than zero.")] decimal amount,
        [Description("UTC ISO-8601 time of the transfer. Omit to use the server time.")] DateTime? at = null,
        [Description("Optional note.")] string? note = null)
    {
        return SendAsync(new RecordTransfer
        {
            Wallet = wallet,
            ClientId = client_id,
            Kind = ParseEnum<TransferKind>(kind, "Transfer kind", "deposit, withdrawal, or sweep"),
            Amount = amount,
            At = at,
            Note = note,
        });
    }

    [McpServerTool(Name = "price_mark_record", Idempotent = true)]
    [Description(
        "Record one mark. price is quote currency per unit. " +
        "at is the mark time as UTC ISO-8601. Omit it to use the server time. " +
        "wallet is optional. Omit it for a mark that applies to every wallet. " +
        "client_id is an optional idempotency key. The same client_id returns the original mark. " +
        "A repeat with a different symbol, price, or wallet is rejected. " +
        "This does not fetch a price from a venue.")]
    public Task<RecordPriceMarkResponse> PriceMarkRecord(
        [Description("Instrument symbol, for example BTC-USD.")] string symbol,
        [Description("Quote currency per unit.")] decimal price,
        [Description("UTC ISO-8601 time of the mark. Omit to use the server time.")] DateTime? at = null,
        [Description("Wallet slug. Omit for a mark that applies to every wallet.")] string? wallet = null,
        [Description("Optional idempotency key.")] string? client_id = null,
        [Description("Free-text source, for example agent.")] string? source = null)
    {
        return SendAsync(new RecordPriceMark
        {
            Symbol = symbol,
            Price = price,
            At = at,
            Wallet = wallet,
            ClientId = client_id,
            Source = source,
        });
    }

    [McpServerTool(Name = "price_marks_record", Idempotent = true)]
    [Description(
        "Record several marks in one call. Each mark uses the same fields as price_mark_record: " +
        "symbol, price (quote currency per unit), at (UTC ISO-8601), wallet, client_id, and source. " +
        "A repeated client_id returns the original mark. One invalid mark rejects the batch. " +
        "This does not fetch prices from a venue.")]
    public Task<RecordPriceMarksResponse> PriceMarksRecord(
        [Description("Marks to record. At least one. Each client_id is an idempotency key.")] List<PriceMarkInput> marks)
    {
        return SendAsync(new RecordPriceMarks
        {
            Marks = marks?.Select(mark => new PriceMarkWrite
            {
                Symbol = mark.symbol,
                Price = mark.price,
                At = mark.at,
                Wallet = mark.wallet,
                ClientId = mark.client_id,
                Source = mark.source,
            }).ToList(),
        });
    }

    [McpServerTool(Name = "wallets_list", ReadOnly = true)]
    [Description("List wallets. Archived wallets are omitted unless include_archived is true. Ordered by sort order, then name.")]
    public Task<QueryWalletsResponse> WalletsList(
        [Description("Include archived wallets. Defaults to false.")] bool include_archived = false)
    {
        return SendAsync(new QueryWallets { IncludeArchived = include_archived });
    }

    [McpServerTool(Name = "wallet_balances", ReadOnly = true)]
    [Description(
        "Cash, swept amount, fees, and open positions for one wallet, in the wallet base currency (USD unless the wallet uses another code). " +
        "Each open position includes quantity, cost basis, average price, last mark (quote currency per unit) and its UTC time, " +
        "unrealized P&L, and realized P&L. Unrealized P&L is null when an open position has no mark. " +
        "An unknown wallet is rejected.")]
    public Task<GetWalletBalancesResponse> WalletBalances(
        [Description("Wallet slug.")] string wallet)
    {
        return SendAsync(new GetWalletBalances { Wallet = wallet });
    }

    [McpServerTool(Name = "wallet_history", ReadOnly = true)]
    [Description(
        "Trades and transfers for one wallet, newest first. " +
        "from and to are inclusive UTC ISO-8601 times. " +
        "kind is buy, sell, deposit, withdrawal, or sweep. Omit kind for every row. " +
        "take defaults to 100 and cannot exceed 1000. Amounts are in the wallet base currency. " +
        "Trade prices are quote currency per unit. An unknown wallet is rejected.")]
    public Task<QueryWalletHistoryResponse> WalletHistory(
        [Description("Wallet slug.")] string wallet,
        [Description("Inclusive UTC ISO-8601 start. Omit for no start bound.")] DateTime? from = null,
        [Description("Inclusive UTC ISO-8601 end. Omit for no end bound.")] DateTime? to = null,
        [Description("buy, sell, deposit, withdrawal, or sweep. Omit for every kind.")] string? kind = null,
        [Description("Page size. Defaults to 100. Maximum 1000.")] int? take = null)
    {
        return SendAsync(new QueryWalletHistory
        {
            Wallet = wallet,
            From = from,
            To = to,
            Kind = kind,
            Take = take,
        });
    }

    [McpServerTool(Name = "trades_query", ReadOnly = true)]
    [Description(
        "Recorded trades, newest first. Filter by wallet slug, symbol, side (Buy or Sell), and an inclusive UTC ISO-8601 range. " +
        "qty is the instrument quantity. price is quote currency per unit. fee is in the wallet base currency. " +
        "take defaults to 100 and cannot exceed 1000. An unknown wallet is rejected.")]
    public Task<QueryTradesResponse> TradesQuery(
        [Description("Wallet slug. Omit to include every wallet.")] string? wallet = null,
        [Description("Instrument symbol, for example BTC-USD. Omit for every symbol.")] string? symbol = null,
        [Description("Buy or Sell. Omit for both.")] string? side = null,
        [Description("Inclusive UTC ISO-8601 start. Omit for no start bound.")] DateTime? from = null,
        [Description("Inclusive UTC ISO-8601 end. Omit for no end bound.")] DateTime? to = null,
        [Description("Page size. Defaults to 100. Maximum 1000.")] int? take = null)
    {
        return SendAsync(new QueryTrades
        {
            Wallet = wallet,
            Symbol = symbol,
            Side = string.IsNullOrWhiteSpace(side) ? null : ParseEnum<TradeSide>(side, "Trade side", "Buy or Sell"),
            From = from,
            To = to,
            Take = take,
        });
    }

    [McpServerTool(Name = "pnl_by_day", ReadOnly = true)]
    [Description(
        "Daily P&L from recorded trades, fees, and price marks, in the wallet base currency (USD unless the wallet uses another code). " +
        "wallet is optional. Omit it for every wallet. " +
        "from and to are inclusive UTC ISO-8601 bounds. Omit either for no bound. " +
        "Each day has realized P&L, the change in unrealized P&L, fees, deposits, withdrawals, sweeps, and end-of-day total value. " +
        "Unrealized change and P&L are null when an open position has no mark. Transfers are not P&L. " +
        "An unknown wallet is rejected.")]
    public Task<PnlByDayResponse> PnlByDay(
        [Description("Wallet slug. Omit for every wallet.")] string? wallet = null,
        [Description("Inclusive UTC ISO-8601 start. Omit for no start bound.")] DateTime? from = null,
        [Description("Inclusive UTC ISO-8601 end. Omit for no end bound.")] DateTime? to = null)
    {
        return SendAsync(new PnlByDay
        {
            Wallet = wallet,
            From = from,
            To = to,
        });
    }

    static TEnum ParseEnum<TEnum>(string? value, string label, string expected) where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value) || !Enum.TryParse<TEnum>(value.Trim(), ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            throw new ModelContextProtocol.McpException($"{label} is required. Use {expected}.");
        return parsed;
    }
}
