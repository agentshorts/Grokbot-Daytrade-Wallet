# TradeMonitor (Grokbot Daytrade Wallet)

A local dashboard to watch trading wallets, real and paper, with transaction history, buy price, current price, P&L and fees, plus an MCP server so a trading agent can record paper trades and read balances.

Status: template demo content has been removed. Feature work is tracked in the issues, in build order.

## Wallets

| Wallet | Mode | How data gets in |
|---|---|---|
| Agent paper wallet | paper | Recorded by the trading agent through `/mcp` tools (trades, deposits, sweeps) |
| Coinbase | real | Read-only sync from the Coinbase Advanced Trade API (view-only key) |
| Kalshi | real, secondary | Read-only sync from the Kalshi API |

The app never places orders or moves money. Upstream clients are GET-only.

## Stack

- .NET 10, ServiceStack 10, SQLite (OrmLite + EF Core Identity)
- Vue 3, Vite, TypeScript, Tailwind CSS 4, `@servicestack/vue`
- MCP: `ModelContextProtocol.AspNetCore`, served at `/mcp`, API key auth
- Charts: vue-echarts 8 with ECharts 6, TradingView lightweight-charts v5
- Layout: TailAdmin Vue (MIT), adapted into the client

## UI shell

Overview, Trades and Wallets share one read-only shell. The mode (All, Real, Paper), the date range (Today, 7D, 30D, All) and the selected wallet live in the URL query, so a view can be reloaded or shared. The page refreshes every 15 seconds. Until the ledger read API is connected, the shell renders a built-in sample book.

Times are shown in America/Chicago with a CT label.

Layout, sidebar, header, card and table styles are adapted from [TailAdmin Vue](https://github.com/TailAdmin/vue-tailwind-admin-dashboard) (Vue 3, Tailwind CSS 4, TypeScript). TailAdmin Vue is MIT licensed, Copyright (c) 2026 TailAdmin.

Analytics charts use [vue-echarts](https://github.com/ecomfe/vue-echarts) 8 and [Apache ECharts](https://echarts.apache.org/) 6. Price charts use [TradingView Lightweight Charts](https://github.com/tradingview/lightweight-charts) v5, with buy and sell markers. Those libraries load only on the pages that draw them.

## Run locally

Requires the .NET 10 SDK and Node.js 24.

```bash
cd TradeMonitor.Client
npm ci

cd ../TradeMonitor
dotnet watch
```

`dotnet watch` starts the .NET host and the Vite dev server. The app listens on `https://127.0.0.1:5051` and `http://127.0.0.1:5050`.

Open `https://127.0.0.1:5051`. Sign in from the header. The first run creates the SQLite database and the local seed users shown on the sign-in page.

The trading agent writes through MCP at `http://127.0.0.1:5050/mcp`. Tool names, example calls, and API key setup are in [docs/mcp.md](docs/mcp.md). Copy `.env.example` to `.env` and set `MCP_API_KEY` there, or put the same value in gitignored `appsettings.Local.json`. Do not commit the key.

## Host PC (Windows)

From the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\run.ps1
```

The script copies `.env.example` to `.env` when `.env` is missing, generates `MCP_API_KEY` when that value is empty, builds the client and the server in Release, and starts the app at `http://127.0.0.1:5050` with `ASPNETCORE_ENVIRONMENT=Production`. It refuses any URL that is not a loopback address. The full key stays in `.env` and is not printed.

`http://127.0.0.1:5050/api/health` reports database status, the last ledger write time, and the app version.

Trading agent setup, example calls, backups, and the logon task are in [docs/agent-onboarding.md](docs/agent-onboarding.md).

## Checks

```bash
dotnet build TradeMonitor.slnx
dotnet test TradeMonitor.slnx

cd TradeMonitor.Client
npm ci
npm run test:run
npm run build
```

`.github/workflows/build.yml` runs those checks. `.github/workflows/gitleaks.yml` scans for secrets.

## Secrets

This repository is public. Never commit keys, `.env` files, `*.pem` files, databases or local appsettings.

- Keep API keys in `MCP_API_KEY`, `dotnet user-secrets` (project `TradeMonitor`), or a gitignored `appsettings.Local.json`. `.env.example` shows the variable names with empty values.
- `App_Data/` (the SQLite database) is gitignored.
- Every push and pull request runs a gitleaks secret scan (`.github/workflows/gitleaks.yml`, config in `.gitleaks.toml`).

## Branches

- `main`: released state. Changes land only through reviewed merges from `staging`.
- `staging`: integration branch. One feature branch per issue, branched from `staging`, with a PR back into `staging` that includes its tests.
