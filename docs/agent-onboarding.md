# Trading agent onboarding

The app runs only on this PC. The trading agent records wallets, fills, cash, and marks through MCP at `http://127.0.0.1:5050/mcp`. A command on the host or a local MCP client config is the whole path.

The server does not place orders, move money, or call an exchange. Do not put it behind a tunnel, a reverse proxy, or a non-loopback bind.

Tool fields are listed in [mcp.md](mcp.md). This page is the first session on the host.

## Start the app

From the repository root in PowerShell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\run.ps1
```

The script does four things:

1. If `.env` is missing, it copies `.env.example` to `.env`.
2. If `MCP_API_KEY` is empty in both the environment and `.env`, it generates a key and writes it to `.env`. The console shows the first four characters and the length. It does not print the full key.
3. It installs client packages, builds the Vue client and the .NET server in Release, and copies the client build into `TradeMonitor\wwwroot`. `npm ci` runs when `TradeMonitor.Client\node_modules` is missing or when `package-lock.json` has changed since the last install. After a successful `npm ci`, the script stores the SHA-256 of `package-lock.json` in `node_modules\.trademonitor-lock.sha256`. A missing stamp counts as a change, and the console prints one line that says why it is reinstalling. `-SkipBuild` skips the install and both builds.
4. It starts the app with `ASPNETCORE_ENVIRONMENT=Production` and `ASPNETCORE_URLS=http://127.0.0.1:5050`. The host applies pending OrmLite migrations to the existing SQLite file. Rows already stored stay as they are. A trade recorded before the Kind column existed is kept and marked Fill.

`localhost` and `::1` are accepted and rewritten to `127.0.0.1`. Any other host, including `0.0.0.0`, `+`, `*`, or a LAN address, stops the script before it starts the host. The server refuses those URLs as well.

Requires the .NET 10 SDK and Node.js 24 on PATH.

When the host is up:

```powershell
Invoke-RestMethod http://127.0.0.1:5050/api/health
```

A healthy process looks like this:

```json
{
  "status": "ok",
  "database": "ok",
  "lastWriteAt": null,
  "version": "1.0.0"
}
```

`database` is `ok` when SQLite answers. `lastWriteAt` is the latest wallet create time or the latest `RecordedAt` on a trade, transfer, or price mark, as UTC ISO-8601. It is null until the first write. `version` is the running app version. If the database cannot be opened, the status is `unavailable` and the HTTP status is 503.

The health check is `http://127.0.0.1:5050/api/health`. Page URLs are not under `/api`. Open these in a browser on this PC:

- `http://127.0.0.1:5050/` for the overview
- `http://127.0.0.1:5050/trades?trade=<id>` for the trades page with that trade's drawer open
- `http://127.0.0.1:5050/wallets`
- `http://127.0.0.1:5050/wallet/<key>`

Those return the app. Ledger JSON uses the `/api` prefix, for example `http://127.0.0.1:5050/api/summary`, `http://127.0.0.1:5050/api/trades`, and `http://127.0.0.1:5050/api/wallets/<key>`. An unknown `/api` path returns 404. `/mcp` is unchanged.

## API key

The MCP endpoint requires an admin API key. A missing, unknown, cancelled, or expired key returns 401. A key whose user is not an Admin returns 403.

The run script's generated key is registered for `admin@email.com` on startup when it is not already stored. You can use that key and skip the admin UI.

To create a key yourself:

1. Start the app and open `http://127.0.0.1:5050`.
2. Sign in as `admin@email.com`. The sign-in page fills the local seed password.
3. Open `http://127.0.0.1:5050/admin-ui` and choose API Keys.
4. Create a key for the admin user. The full key is shown once. Copy it into `.env` as `MCP_API_KEY`.

The value must be at least 8 characters. `MCP_API_KEY` in the environment wins over `.env` and over `TradeMonitor\appsettings.Local.json`. Restart the host after changing the file.

Do not commit the key. Do not put it in a URL, a command argument, or a tracked config file. `.env` and `appsettings.Local.json` are gitignored.

## Local MCP client

`config/mcp.client.example.json` is a template. `YOUR_MCP_API_KEY` is a placeholder, not a key.

```json
{
  "mcpServers": {
    "trademonitor": {
      "type": "http",
      "url": "http://127.0.0.1:5050/mcp",
      "headers": {
        "Authorization": "Bearer YOUR_MCP_API_KEY"
      }
    }
  }
}
```

If the client reads environment variables, point the header at the variable and do not paste the key into a file:

```json
"Authorization": "Bearer ${MCP_API_KEY}"
```

If it does not, copy the example to an untracked file such as `mcp.client.json` (gitignored) and replace the placeholder there. Never commit that copy. Do not add `?apikey=` to the URL. The server accepts `Authorization: Bearer`, `X-Api-Key`, or `?apikey=`, and the query form leaks into logs and shell history, so the scripts refuse it.

## Command line

`scripts/mcp-call.ps1` calls any MCP tool. It reads `MCP_API_KEY` from the environment, or from `.env` when the variable is empty. It sends `Authorization: Bearer` to `http://127.0.0.1:5050/mcp` (or `MCP_URL` when that is also a loopback URL).

From PowerShell, after the app is running:

```powershell
.\scripts\mcp-call.ps1 wallet_balances '{"wallet":"paper"}'
```

Arguments are a JSON object. `-JsonFile` reads them from a file when quoting is awkward:

```powershell
.\scripts\mcp-call.ps1 wallet_create -JsonFile .\fill.json
```

The tool name is the first argument (`wallet_create`, `trade_record`, `transfer_record`, and the rest of the tools in [mcp.md](mcp.md)). A failed tool prints the error and exits non-zero. The key is not written to the output.

## Amounts and time

Prices are quote currency per unit. Cash, fees, deposits, withdrawals, and sweeps are in the wallet base currency. That is USD unless the wallet was created with another code.

Times are UTC ISO-8601, for example `2026-06-01T15:04:00Z`. Omit the time field to use the server clock. Do not send a local time without a zone.

`client_id` is an idempotency key, unique per action. Sending the same wallet and client id with the same values returns the original row. Sending it again with different values is rejected. Reuse the same client id when you retry a call that might have succeeded. Pick a new client id for a new fill, transfer, or mark.

## First session

Run these in order. The four writes are a paper wallet, a deposit, a buy, and a mark.

Create the paper wallet:

```powershell
.\scripts\mcp-call.ps1 wallet_create '{"key":"paper","name":"Paper","mode":"Paper","venue":"Coinbase","base_currency":"USD","note":"Agent paper book"}'
```

Record starting cash. `kind` is `deposit`, `withdrawal`, or `sweep`. `amount` is in the wallet base currency and must be greater than zero.

```powershell
.\scripts\mcp-call.ps1 transfer_record '{"wallet":"paper","client_id":"dep-2026-06-01","kind":"deposit","amount":1000,"at":"2026-06-01T15:00:00Z","note":"Starting cash"}'
```

Record a buy that already happened. `qty` is the instrument quantity. `price` is quote currency per unit and must be greater than zero on a buy. `fee` is in the wallet base currency. Use `0` when there was no fee. A buy is rejected when `qty` times `price`, plus `fee`, is more than the cash in the wallet by more than 0.000000001. A smaller shortfall is accepted and the cash is recorded as 0. The deposit above covers this fill (0.01 times 64000, plus a 0.5 fee).

```powershell
.\scripts\mcp-call.ps1 trade_record '{"wallet":"paper","client_id":"fill-2026-06-01-1","symbol":"BTC-USD","side":"Buy","qty":0.01,"price":64000,"fee":0.5,"executed_at":"2026-06-01T15:04:00Z","title":"First clip","order_ref":"ord-1","target_price":64500,"note":"Paper fill"}'
```

Record a mark. `price` is quote currency per unit. Omit `wallet` for a mark that applies to every wallet.

```powershell
.\scripts\mcp-call.ps1 price_mark_record '{"symbol":"BTC-USD","price":64100,"at":"2026-06-01T15:10:00Z","wallet":"paper","client_id":"mark-btc-1","source":"agent"}'
```

Read the book:

```powershell
.\scripts\mcp-call.ps1 wallet_balances '{"wallet":"paper"}'
```

Cash, fees, realized P&L, and the open BTC-USD position come back. Unrealized P&L uses the mark. It is null when an open position has no mark.

## Wallets

`mode` is `Paper` or `Real` and cannot change later. Paper is for simulated fills. Real is a hand-kept record of a venue account. This call does not open an account.

```powershell
.\scripts\mcp-call.ps1 wallet_create '{"key":"coinbase","name":"Coinbase","mode":"Real","venue":"Coinbase","base_currency":"USD","note":"Hand-kept Coinbase book"}'
```

The same key and the same values returns the existing wallet. The same key with a different name or mode is rejected.

`wallet_update` changes the display name, note, sort order, or archive flag. Omit a field to leave it unchanged.

```powershell
.\scripts\mcp-call.ps1 wallet_update '{"key":"paper","name":"Paper book","sort_order":1,"archived":false}'
```

List wallets:

```powershell
.\scripts\mcp-call.ps1 wallets_list '{"include_archived":false}'
```

## Trades

A sell uses the same tool. `side` is `Buy` or `Sell`. A sell price may be zero. Set `kind` to `settlement` when the contract settled, or omit `kind` for a fill. A sell larger than the open quantity is rejected. An unknown wallet is rejected.

```powershell
.\scripts\mcp-call.ps1 trade_record '{"wallet":"paper","client_id":"fill-2026-06-01-2","symbol":"BTC-USD","side":"Sell","qty":0.004,"price":65000,"fee":0.25,"executed_at":"2026-06-01T16:00:00Z","title":"Trim","note":"Paper sell"}'
```

`fee` is always in the wallet base currency, including on a sell. `price` stays quote currency per unit. A second sell of the same fill reuses `fill-2026-06-01-2`. A different fill needs a new `client_id`.

Recorded trades, newest first:

```powershell
.\scripts\mcp-call.ps1 trades_query '{"wallet":"paper","symbol":"BTC-USD","side":"Buy","from":"2026-06-01T00:00:00Z","to":"2026-06-02T00:00:00Z","take":50}'
```

## Transfers

A withdrawal or a sweep uses `transfer_record`. Amount is in the wallet base currency. A withdrawal or sweep larger than cash is rejected.

```powershell
.\scripts\mcp-call.ps1 transfer_record '{"wallet":"paper","client_id":"wd-2026-06-02","kind":"withdrawal","amount":10,"at":"2026-06-02T15:00:00Z","note":"Cash out"}'
```

```powershell
.\scripts\mcp-call.ps1 transfer_record '{"wallet":"paper","client_id":"sw-2026-06-02","kind":"sweep","amount":5,"at":"2026-06-02T15:05:00Z","note":"Sweep to cold"}'
```

History mixes trades and transfers, newest first. `kind` is `buy`, `sell`, `deposit`, `withdrawal`, or `sweep`.

```powershell
.\scripts\mcp-call.ps1 wallet_history '{"wallet":"paper","from":"2026-06-01T00:00:00Z","to":"2026-06-02T23:59:59Z","take":50}'
```

## Price marks

One mark is `price_mark_record`, shown in the first session. Several marks are `price_marks_record`. Each item uses the same fields. One invalid item rejects the batch.

```powershell
.\scripts\mcp-call.ps1 price_marks_record '{"marks":[{"symbol":"BTC-USD","price":64100,"at":"2026-06-01T15:10:00Z","wallet":"paper","client_id":"mark-btc-1","source":"agent"},{"symbol":"ETH-USD","price":3200,"client_id":"mark-eth-1","source":"agent"}]}'
```

The same `client_id` returns the original mark. A repeat with a different symbol, price, or wallet is rejected.

## See the rows in the UI

Sign in and open `http://127.0.0.1:5050/admin-ui`, then Database. The first session shows one row in each of these tables:

| Table | What you recorded |
|---|---|
| Wallet | `paper` |
| Transfer | deposit `dep-2026-06-01` |
| Trade | buy `fill-2026-06-01-1` |
| PriceMark | `mark-btc-1` |

`wallet_balances` and `wallet_history` show the same rows from the command line. `/api/health` then reports a `lastWriteAt` at or after the last of those writes.

## Not reachable from another machine

The app binds `127.0.0.1` only. Check that by hand:

1. On this PC, `Invoke-RestMethod http://127.0.0.1:5050/api/health` returns `status` `ok`.
2. On this PC, `Invoke-RestMethod http://<lan-ip>:5050/api/health` fails to connect. Replace `<lan-ip>` with an address from `ipconfig`, for example `192.168.1.20`.
3. From another machine on the same network, the same LAN URL fails to connect.

A start command that tries to bind `0.0.0.0` or a LAN address exits before the process listens. There is no tunnel and no public hostname.

## Backups

While the app is running it copies every `TradeMonitor\App_Data\*.db` file into `TradeMonitor\backups` at startup and then once a day. The file name is `<database>-<utc-date>.db`, for example `app-2026-11-12.db`. A later run on the same UTC date replaces that file. The job keeps 14 dates and deletes older dated copies. Files that do not match that name are left alone.

`TradeMonitor\backups` is gitignored. Do not commit database files.

Set `Backup:Keep` in configuration to change the number of dates. The default is 14.

## Start at logon

Optional. On Windows, this registers a scheduled task named `TradeMonitor` that runs `scripts\run.ps1` when you sign in. The task does not stop the process on a timer.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\install-startup-task.ps1
```

Remove it:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\install-startup-task.ps1 -Remove
```

Run `scripts\run.ps1` once yourself before relying on the task, so the first build and the API key exist. If the app is already running, a second start fails because port 5050 is taken.
