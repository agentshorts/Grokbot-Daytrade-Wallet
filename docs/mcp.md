# MCP ledger tools

Host setup, the API key, and a first recorded session are in [agent onboarding](agent-onboarding.md).

External writers, including the Kalshi bot, talk to this app over MCP on the same PC. The server listens on `127.0.0.1` and the tools call the ledger services in process. They do not place orders, move money, or call a venue. When Coinbase sync is enabled, that job also writes Coinbase wallets in process. Run only one Coinbase writer at a time. See the README.

Endpoint: `http://127.0.0.1:5050/mcp`

Send the API key in one of these places:

- `Authorization: Bearer <key>`
- `X-Api-Key: <key>`
- `?apikey=<key>`

A missing, unknown, cancelled, or expired key returns 401. A key whose user is not in the Admin role returns 403.

## API key

Create a key in the admin UI, then give that same value to the agent.

1. Start the app and open `https://127.0.0.1:5051`.
2. Sign in as an Admin user. A fresh database seeds `admin@email.com`.
3. Open `https://127.0.0.1:5051/admin-ui` and choose API Keys.
4. Create a key for the admin user. The full key is shown once. Copy it.

Put the key in a gitignored file, never in the repository. Either:

`.env` in the repository root (copy `.env.example`):

```
MCP_API_KEY=paste-the-admin-key
MCP_URL=http://127.0.0.1:5050/mcp
```

or `TradeMonitor/appsettings.Local.json`:

```json
{
  "Mcp": {
    "ApiKey": "paste-the-admin-key"
  }
}
```

`MCP_API_KEY` wins when both are set. The value must be at least 8 characters. On startup the host registers that key for `admin@email.com` when it is not already stored. A key you created in the admin UI works without that step. Restart the host after changing the file.

Example MCP client entry:

```json
{
  "mcpServers": {
    "trademonitor": {
      "type": "http",
      "url": "http://127.0.0.1:5050/mcp",
      "headers": {
        "Authorization": "Bearer paste-the-admin-key"
      }
    }
  }
}
```

## Amounts and time

Prices are quote currency per unit. Cash, fees, deposits, withdrawals, and sweeps are in the wallet base currency. That is USD unless the wallet was created with another code. Times are UTC ISO-8601, for example `2026-06-01T15:04:00Z`.

`client_id` is an idempotency key. Sending the same wallet and client id with the same values returns the original row. Sending it again with different values is rejected. Pick a new client id for a new fill.

## Tools

### wallet_create

Create a wallet. `mode` is `Paper` or `Real` and cannot change later. `base_currency` defaults to USD.

```json
{
  "key": "paper",
  "name": "Paper",
  "mode": "Paper",
  "venue": "Coinbase",
  "base_currency": "USD",
  "note": "Agent paper book"
}
```

The same key and the same values returns the existing wallet. The same key with a different name, mode, or kind is rejected.

`kind` is `Standard` or `PredictionMarket`. Omit it for a standard wallet. A key that starts with `pm-` is stored as `PredictionMarket` even when `kind` is omitted. Passing `Standard` for a `pm-` key is rejected.

### Prediction contracts

Coinbase prediction contracts are one wallet per contract. Create the wallet by hand, then record the buy, the sell or the settlement, and the fee with `trade_record`. The key is `pm-` plus the product id.

```json
{
  "key": "pm-KXBTCD-26NOV1210-T71199.99-KALSHI",
  "name": "BTC 71199.99",
  "mode": "Real",
  "venue": "Coinbase",
  "kind": "PredictionMarket"
}
```

A settlement is a sell at `1` or `0` with `kind` `settlement`. Coinbase sync does not create or write these wallets, and it does not post marks for them. Open contracts have no live mark.

### wallet_update

Change the display name, note, sort order, or archive flag. Omit a field to leave it unchanged. An empty note clears the note.

```json
{
  "key": "paper",
  "name": "Paper book",
  "sort_order": 1,
  "archived": false
}
```

### transfer_record

Record cash that already moved. `kind` is `deposit`, `withdrawal`, or `sweep`. `amount` is in the wallet base currency and must be greater than zero.

```json
{
  "wallet": "paper",
  "client_id": "dep-2026-06-01",
  "kind": "deposit",
  "amount": 50,
  "at": "2026-06-01T15:00:00Z",
  "note": "Starting cash"
}
```

A withdrawal or sweep larger than cash is rejected.

### trade_record

Record a fill or a settlement that already happened. `qty` is the instrument quantity and must be greater than zero. `price` is quote currency per unit. `fee` is in the wallet base currency. Use `0` when there was no fee.

A buy price must be greater than zero. A sell price may be zero when a contract settles or expires worthless. A negative price is rejected.

`kind` is `fill` or `settlement`. Omit it to record a fill. Use `settlement` when the contract settled, including a loss at price 0. The trade drawer shows this kind. A repeat with a different kind is rejected.

A buy is rejected when `qty` times `price`, plus `fee`, is more than the cash in the wallet by more than `0.000000001` of the base currency. A shortfall of that size or smaller is accepted, and the recorded cash is `0` rather than a negative dust balance.

```json
{
  "wallet": "paper",
  "client_id": "fill-2026-06-01-1",
  "symbol": "BTC-USD",
  "side": "Buy",
  "qty": 0.01,
  "price": 64000,
  "fee": 0.5,
  "executed_at": "2026-06-01T15:04:00Z",
  "title": "First clip",
  "order_ref": "ord-1",
  "target_price": 64500,
  "note": "Paper fill"
}
```

A settlement at price 0:

```json
{
  "wallet": "paper",
  "client_id": "settle-2026-06-02-1",
  "symbol": "CONTRACT",
  "side": "Sell",
  "qty": 1,
  "price": 0,
  "fee": 0,
  "kind": "settlement",
  "executed_at": "2026-06-02T20:00:00Z",
  "title": "Settled at zero"
}
```

A sell larger than the open quantity is rejected as an oversell. An unknown wallet is rejected.

### price_mark_record

Record one mark. `price` is quote currency per unit. Omit `wallet` for a mark that applies to every wallet.

```json
{
  "symbol": "BTC-USD",
  "price": 64100,
  "at": "2026-06-01T15:10:00Z",
  "wallet": "paper",
  "client_id": "mark-btc-1",
  "source": "agent"
}
```

### price_marks_record

Record several marks. Each item uses the same fields as `price_mark_record`.

```json
{
  "marks": [
    {
      "symbol": "BTC-USD",
      "price": 64100,
      "at": "2026-06-01T15:10:00Z",
      "wallet": "paper",
      "client_id": "mark-btc-1",
      "source": "agent"
    },
    {
      "symbol": "ETH-USD",
      "price": 3200,
      "client_id": "mark-eth-1",
      "source": "agent"
    }
  ]
}
```

### wallets_list

```json
{ "include_archived": false }
```

### wallet_balances

Cash, swept amount, fees, realized P&L, and open positions. Each open position includes quantity, cost basis, last mark, and unrealized P&L. Unrealized P&L is null when an open position has no mark.

```json
{ "wallet": "paper" }
```

### wallet_history

Trades and transfers, newest first. `kind` is `buy`, `sell`, `deposit`, `withdrawal`, or `sweep`. `take` defaults to 100 and cannot exceed 1000.

```json
{
  "wallet": "paper",
  "from": "2026-06-01T00:00:00Z",
  "to": "2026-06-01T23:59:59Z",
  "kind": "buy",
  "take": 50
}
```

### trades_query

Recorded trades, newest first. Omit a filter to leave it open.

```json
{
  "wallet": "paper",
  "symbol": "BTC-USD",
  "side": "Buy",
  "from": "2026-06-01T00:00:00Z",
  "to": "2026-06-02T00:00:00Z",
  "take": 50
}
```

### pnl_by_day

Daily P&L from recorded trades, fees, and price marks. `wallet` is optional. `from` and `to` are inclusive UTC times. Each day includes realized P&L, the change in unrealized P&L, fees, deposits, withdrawals, sweeps, and end-of-day total value. Unrealized change and total P&L are null when an open position has no mark. Transfers are not P&L.

```json
{
  "wallet": "paper",
  "from": "2026-06-01T00:00:00Z",
  "to": "2026-06-30T23:59:59Z"
}
```
