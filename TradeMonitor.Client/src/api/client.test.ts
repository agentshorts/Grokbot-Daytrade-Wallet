import { describe, expect, it, vi } from 'vite-plus/test'
import { createLedgerClient, fetchTransport, withQuery, type LedgerTransport } from '@/api/client'
import { LedgerReadError } from '@/api/errors'

function memory(routes: Record<string, { status?: number; body: unknown }>) {
    const calls: string[] = []
    const transport: LedgerTransport = {
        async get(path) {
            calls.push(path)
            const url = new URL(path, 'http://localhost')
            const match = routes[url.pathname]
            if (!match) {
                return { status: 404, body: { responseStatus: { errorCode: 'NotFound', message: url.pathname } } }
            }
            return { status: match.status ?? 200, body: match.body }
        },
    }
    return { transport, calls }
}

describe('ledger query strings', () => {
    it('writes only populated read filters', () => {
        expect(withQuery('/api/summary', { mode: 'paper', wallet: 'paper', from: '2026-11-01T06:00:00.000Z', to: '2026-11-12T02:18:00.000Z' })).toBe(
            '/api/summary?mode=paper&wallet=paper&from=2026-11-01T06%3A00%3A00.000Z&to=2026-11-12T02%3A18%3A00.000Z',
        )
        expect(withQuery('/api/summary', { mode: 'all' })).toBe('/api/summary?mode=all')
        expect(withQuery('/api/trades', { take: 200, descending: true, orderBy: 'time' })).toBe('/api/trades?orderBy=time&take=200&descending=true')
        expect(withQuery('/api/wallets', { includeArchived: false })).toBe('/api/wallets')
        expect(withQuery('/api/wallets', { includeArchived: true })).toBe('/api/wallets?includeArchived=true')
    })
})

describe('ledger client', () => {
    it('reads with GET and keeps a null unrealized P&L null', async () => {
        const fetchImpl = vi.fn(async () => new Response(JSON.stringify({ results: [] }), { status: 200 }))
        const reader = createLedgerClient(fetchTransport(fetchImpl))
        await reader.wallets()
        expect(fetchImpl).toHaveBeenCalledTimes(1)
        const [path, init] = fetchImpl.mock.calls[0] as unknown as [string, RequestInit]
        expect(path).toBe('/api/wallets')
        expect(init).toMatchObject({ method: 'GET', credentials: 'same-origin', redirect: 'manual' })
        expect(JSON.stringify(init)).not.toMatch(/POST|PUT|PATCH|DELETE/)

        const { transport, calls } = memory({
            '/api/summary': {
                body: {
                    totals: {
                        totalValue: 1000,
                        unrealizedPnl: null,
                        realizedPnl: '12.50',
                        fees: 0,
                        winRate: null,
                    },
                    wallets: [
                        {
                            key: 'paper',
                            name: 'Paper',
                            mode: 'Paper',
                            unrealizedPnl: null,
                            totalValue: 1000,
                            lastActivityAt: '2026-11-02T15:04:00.0000000Z',
                        },
                    ],
                },
            },
            '/api/positions': {
                body: {
                    results: [{ wallet: 'paper', symbol: 'ETH-USD', qty: 1, unrealizedPnl: null, lastMarkPrice: null, valuedAtCost: true, status: 'open' }],
                },
            },
            '/api/trades/4': {
                body: {
                    result: { id: 4, wallet: 'paper', symbol: 'BTC-USD', side: 'Buy', pnl: null, unrealizedPnl: null, executedAt: '2026-11-02T15:04:00Z' },
                    hasPriceMarks: false,
                },
            },
        })
        const client = createLedgerClient(transport)
        const summary = await client.summary({ mode: 'all', wallet: 'paper' })
        expect(calls[0]).toContain('/api/summary?')
        expect(summary.totals.totalValue).toBe(1000)
        expect(summary.totals.unrealizedPnl).toBeNull()
        expect(summary.totals.realizedPnl).toBe(12.5)
        expect(summary.totals.fees).toBe(0)
        expect(summary.totals.winRate).toBeNull()
        expect(summary.wallets[0]?.mode).toBe('paper')
        expect(summary.wallets[0]?.unrealizedPnl).toBeNull()
        expect(summary.wallets[0]?.lastActivityAt).toBe('2026-11-02T15:04:00.000Z')

        const positions = await client.positions({ status: 'open' })
        expect(positions.results[0]?.unrealizedPnl).toBeNull()
        expect(positions.results[0]?.lastMarkPrice).toBeNull()

        const detail = await client.trade(4)
        expect(detail.result.pnl).toBeNull()
        expect(detail.result.side).toBe('buy')
        await expect(client.trade(0)).rejects.toBeInstanceOf(LedgerReadError)
    })

    it('accepts PascalCase and does not turn a missing or explicit zero into the other', async () => {
        const { transport } = memory({
            '/api/summary': {
                body: {
                    Totals: { TotalValue: 10, UnrealizedPnl: null, RealizedPnl: 0 },
                    Wallets: [{ Key: 'coinbase', Name: 'Coinbase', Mode: 'Real', UnrealizedPnl: 0, TotalValue: 10 }],
                },
            },
        })
        const summary = await createLedgerClient(transport).summary()
        expect(summary.totals.unrealizedPnl).toBeNull()
        expect(summary.totals.realizedPnl).toBe(0)
        expect(summary.totals.totalValue).toBe(10)
        expect(summary.wallets[0]?.mode).toBe('real')
        expect(summary.wallets[0]?.unrealizedPnl).toBe(0)

        const omitted = memory({ '/api/summary': { body: { totals: { totalValue: 3 } } } })
        const missing = await createLedgerClient(omitted.transport).summary()
        expect(missing.totals.unrealizedPnl).toBeNull()
        expect(missing.totals.totalValue).toBe(3)
    })

    it('treats a login redirect as signed out', async () => {
        const fetchImpl = vi.fn(async () => new Response(null, { status: 302, headers: { location: '/Account/Login' } }))
        await expect(createLedgerClient(fetchTransport(fetchImpl)).wallets()).rejects.toMatchObject({
            status: 401,
            message: 'Sign in as an admin to read the ledger.',
        })
    })

    it('reports auth and unknown wallet failures', async () => {
        const denied = memory({
            '/api/summary': { status: 401, body: { responseStatus: { errorCode: 'Unauthorized', message: 'nope' } } },
        })
        await expect(createLedgerClient(denied.transport).summary()).rejects.toMatchObject({
            status: 401,
            message: 'Sign in as an admin to read the ledger.',
        })

        const missing = memory({
            '/api/equity': { status: 404, body: { responseStatus: { errorCode: 'UnknownWallet', message: "Unknown wallet 'nope'." } } },
        })
        await expect(createLedgerClient(missing.transport).equity({ wallet: 'nope' })).rejects.toMatchObject({
            status: 404,
            code: 'UnknownWallet',
        })
    })

    it('builds wallet, trade, fee, and pnl paths', async () => {
        const { transport, calls } = memory({
            '/api/pnl/daily': { body: { results: [{ date: '2026-11-02T00:00:00Z', pnl: null, fees: 1 }] } },
            '/api/pnl/symbols': { body: { results: [{ symbol: 'BTC-USD', pnl: null, realizedPnl: 4 }] } },
            '/api/fees': { body: { total: 1, days: [{ date: '2026-11-02T00:00:00Z', fees: 1 }] } },
            '/api/trades': { body: { total: 1, results: [{ id: 9, symbol: 'BTC-USD', side: 'Sell', pnl: 4, wallet: 'paper' }] } },
            '/api/wallets/paper': { body: { result: { key: 'paper', name: 'Paper', mode: 'Paper' } } },
            '/api/wallets/paper%20book/balances': {
                body: { result: { key: 'paper book', unrealizedPnl: null, cash: 10, positions: [{ symbol: 'ETH-USD', unrealizedPnl: null }] } },
            },
            '/api/wallets/paper%20book/history': { body: { results: [{ kind: 'deposit', at: '2026-11-01T00:00:00Z', amount: 50 }] } },
            '/api/price-marks': { body: { results: [{ id: 1, symbol: 'BTC-USD', price: 64100, at: '2026-11-03T00:00:00Z' }] } },
        })
        const client = createLedgerClient(transport)
        const daily = await client.pnlDaily({ mode: 'paper' })
        expect(daily.results[0]?.pnl).toBeNull()
        expect(daily.results[0]?.fees).toBe(1)
        const symbols = await client.pnlBySymbol({ take: 10 })
        expect(symbols.results[0]?.pnl).toBeNull()
        expect(symbols.results[0]?.realizedPnl).toBe(4)
        expect(await client.fees()).toMatchObject({ total: 1 })
        const trades = await client.trades({ take: 20, orderBy: 'time', descending: true })
        expect(trades.results[0]?.side).toBe('sell')
        expect(trades.results[0]?.pnl).toBe(4)
        expect((await client.wallet('paper')).result.key).toBe('paper')
        const balances = await client.walletBalances('paper book')
        expect(balances.result.unrealizedPnl).toBeNull()
        expect(balances.result.positions[0]?.unrealizedPnl).toBeNull()
        const history = await client.walletHistory('paper book', { kind: 'deposit', take: 5 })
        expect(history.results[0]?.kind).toBe('deposit')
        expect(history.results[0]?.amount).toBe(50)
        expect((await client.priceMarks({ symbol: 'BTC-USD' })).results[0]?.price).toBe(64100)

        expect(calls).toEqual([
            '/api/pnl/daily?mode=paper',
            '/api/pnl/symbols?take=10',
            '/api/fees',
            '/api/trades?orderBy=time&take=20&descending=true',
            '/api/wallets/paper',
            '/api/wallets/paper%20book/balances',
            '/api/wallets/paper%20book/history?kind=deposit&take=5',
            '/api/price-marks?symbol=BTC-USD',
        ])
    })
})
