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
        expect(withQuery('/summary', { mode: 'paper', wallet: 'paper', from: '2026-11-01T06:00:00.000Z', to: '2026-11-12T02:18:00.000Z' })).toBe(
            '/summary?mode=paper&wallet=paper&from=2026-11-01T06%3A00%3A00.000Z&to=2026-11-12T02%3A18%3A00.000Z',
        )
        expect(withQuery('/summary', { mode: 'all' })).toBe('/summary?mode=all')
        expect(withQuery('/trades', { take: 200, descending: true, orderBy: 'time' })).toBe('/trades?orderBy=time&take=200&descending=true')
        expect(withQuery('/wallets', { includeArchived: false })).toBe('/wallets')
        expect(withQuery('/wallets', { includeArchived: true })).toBe('/wallets?includeArchived=true')
    })
})

describe('ledger client', () => {
    it('reads with GET and keeps a null unrealized P&L null', async () => {
        const fetchImpl = vi.fn(async () => new Response(JSON.stringify({ results: [] }), { status: 200 }))
        const reader = createLedgerClient(fetchTransport(fetchImpl))
        await reader.wallets()
        expect(fetchImpl).toHaveBeenCalledTimes(1)
        const [path, init] = fetchImpl.mock.calls[0] as unknown as [string, RequestInit]
        expect(path).toBe('/wallets')
        expect(init).toMatchObject({ method: 'GET', credentials: 'same-origin' })
        expect(JSON.stringify(init)).not.toMatch(/POST|PUT|PATCH|DELETE/)

        const { transport, calls } = memory({
            '/summary': {
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
            '/positions': {
                body: {
                    results: [{ wallet: 'paper', symbol: 'ETH-USD', qty: 1, unrealizedPnl: null, lastMarkPrice: null, valuedAtCost: true, status: 'open' }],
                },
            },
            '/trades/4': {
                body: {
                    result: { id: 4, wallet: 'paper', symbol: 'BTC-USD', side: 'Buy', pnl: null, unrealizedPnl: null, executedAt: '2026-11-02T15:04:00Z' },
                    hasPriceMarks: false,
                },
            },
        })
        const client = createLedgerClient(transport)
        const summary = await client.summary({ mode: 'all', wallet: 'paper' })
        expect(calls[0]).toContain('/summary?')
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
            '/summary': {
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

        const omitted = memory({ '/summary': { body: { totals: { totalValue: 3 } } } })
        const missing = await createLedgerClient(omitted.transport).summary()
        expect(missing.totals.unrealizedPnl).toBeNull()
        expect(missing.totals.totalValue).toBe(3)
    })

    it('reports auth and unknown wallet failures', async () => {
        const denied = memory({
            '/summary': { status: 401, body: { responseStatus: { errorCode: 'Unauthorized', message: 'nope' } } },
        })
        await expect(createLedgerClient(denied.transport).summary()).rejects.toMatchObject({
            status: 401,
            message: 'Sign in as an admin to read the ledger.',
        })

        const missing = memory({
            '/equity': { status: 404, body: { responseStatus: { errorCode: 'UnknownWallet', message: "Unknown wallet 'nope'." } } },
        })
        await expect(createLedgerClient(missing.transport).equity({ wallet: 'nope' })).rejects.toMatchObject({
            status: 404,
            code: 'UnknownWallet',
        })
    })

    it('builds wallet, trade, fee, and pnl paths', async () => {
        const { transport, calls } = memory({
            '/pnl/daily': { body: { results: [{ date: '2026-11-02T00:00:00Z', pnl: null, fees: 1 }] } },
            '/pnl/symbols': { body: { results: [{ symbol: 'BTC-USD', pnl: null, realizedPnl: 4 }] } },
            '/fees': { body: { total: 1, days: [{ date: '2026-11-02T00:00:00Z', fees: 1 }] } },
            '/trades': { body: { total: 1, results: [{ id: 9, symbol: 'BTC-USD', side: 'Sell', pnl: 4, wallet: 'paper' }] } },
            '/wallets/paper': { body: { result: { key: 'paper', name: 'Paper', mode: 'Paper' } } },
            '/wallets/paper%20book/balances': {
                body: { result: { key: 'paper book', unrealizedPnl: null, cash: 10, positions: [{ symbol: 'ETH-USD', unrealizedPnl: null }] } },
            },
            '/wallets/paper%20book/history': { body: { results: [{ kind: 'deposit', at: '2026-11-01T00:00:00Z', amount: 50 }] } },
            '/price-marks': { body: { results: [{ id: 1, symbol: 'BTC-USD', price: 64100, at: '2026-11-03T00:00:00Z' }] } },
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
            '/pnl/daily?mode=paper',
            '/pnl/symbols?take=10',
            '/fees',
            '/trades?orderBy=time&take=20&descending=true',
            '/wallets/paper',
            '/wallets/paper%20book/balances',
            '/wallets/paper%20book/history?kind=deposit&take=5',
            '/price-marks?symbol=BTC-USD',
        ])
    })
})
