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
- Charts (planned): vue-echarts with ECharts 6, TradingView lightweight-charts v5
- Layout pieces (planned): TailAdmin Vue (MIT)

## Run locally

Requires the .NET 10 SDK and Node.js 24.

```bash
cd TradeMonitor.Client
npm ci

cd ../TradeMonitor
dotnet watch
```

`dotnet watch` starts the .NET host and the Vite dev server. The app listens on `https://localhost:5051` and `http://localhost:5050`, on localhost only.

Open `https://localhost:5051`. Sign in from the header. The first run creates the SQLite database and the local seed users shown on the sign-in page.

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

- Keep API keys in `dotnet user-secrets` (project `TradeMonitor`) or in a gitignored `appsettings.Local.json`.
- `App_Data/` (the SQLite database) is gitignored.
- Every push and pull request runs a gitleaks secret scan (`.github/workflows/gitleaks.yml`, config in `.gitleaks.toml`).

## Branches

- `main`: released state. Changes land only through reviewed merges from `staging`.
- `staging`: integration branch. One feature branch per issue, branched from `staging`, with a PR back into `staging` that includes its tests.
