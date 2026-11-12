import { describe, expect, it } from 'vite-plus/test'
import { applyFilters, type DashboardSnapshot, type PositionRecord } from './book'
import { buildSample } from './sampleData'

const now = new Date('2026-11-12T02:18:00.000Z')

function position(partial: Partial<PositionRecord>): PositionRecord {
    return {
        walletKey: 'coinbase',
        symbol: 'BTC-USD',
        title: 'Bitcoin',
        qty: 1,
        avgPrice: 100,
        lastMark: 110,
        lastMarkAt: now.toISOString(),
        unrealizedPnl: 10,
        unmarked: false,
        ...partial,
    }
}

function snapshot(overrides: Partial<DashboardSnapshot> = {}): DashboardSnapshot {
    return {
        asOf: now.toISOString(),
        lastRecordedAt: now.toISOString(),
        wallets: [
            {
                id: 1,
                key: 'coinbase',
                name: 'Coinbase',
                mode: 'real',
                venue: 'Coinbase',
                baseCurrency: 'USD',
                note: '',
                isArchived: false,
                sortOrder: 1,
            },
            {
                id: 2,
                key: 'paper-swing',
                name: 'Paper Swing',
                mode: 'paper',
                venue: 'Paper',
                baseCurrency: 'USD',
                note: '',
                isArchived: false,
                sortOrder: 2,
            },
        ],
        trades: [],
        marks: [],
        equity: [],
        pnlBars: [],
        positions: [],
        cashByWallet: { coinbase: 1000, 'paper-swing': 500 },
        ...overrides,
    }
}

describe('applyFilters', () => {
    it('keeps only the selected mode', () => {
        const view = applyFilters(buildSample(now), { mode: 'paper', range: 'all', wallet: null }, now)
        expect(view.wallets.every((wallet) => wallet.mode === 'paper')).toBe(true)
        expect(view.trades.every((trade) => trade.mode === 'paper')).toBe(true)
        expect(view.wallets.map((wallet) => wallet.key)).toEqual(['paper-swing'])
    })

    it('limits trades to the selected range', () => {
        const book = buildSample(now)
        const today = applyFilters(book, { mode: 'all', range: 'today', wallet: null }, now)
        const all = applyFilters(book, { mode: 'all', range: 'all', wallet: null }, now)
        expect(today.trades.length).toBeGreaterThan(0)
        expect(today.trades.length).toBeLessThan(all.trades.length)
    })

    it('does not count a missing price mark as zero', () => {
        const book = snapshot({
            positions: [
                position({ symbol: 'BTC-USD', qty: 2, lastMark: 50, unmarked: false, unrealizedPnl: 20 }),
                position({ symbol: 'ETH-USD', qty: 4, lastMark: null, lastMarkAt: null, unmarked: true, unrealizedPnl: null }),
            ],
        })
        const view = applyFilters(book, { mode: 'all', range: 'all', wallet: 'coinbase' }, now)
        const exposure = view.kpis.find((kpi) => kpi.id === 'exposure')
        const unrealized = view.kpis.find((kpi) => kpi.id === 'unrealized')
        expect(exposure?.value).toBe(100)
        expect(exposure?.hint).toBe('1 without a price mark')
        expect(unrealized?.value).toBe(20)

        const unmarkedOnly = applyFilters(
            snapshot({ positions: [position({ lastMark: null, lastMarkAt: null, unmarked: true, unrealizedPnl: null })] }),
            { mode: 'all', range: 'all', wallet: null },
            now,
        )
        expect(unmarkedOnly.kpis.find((kpi) => kpi.id === 'exposure')?.value).toBeNull()
        expect(unmarkedOnly.kpis.find((kpi) => kpi.id === 'unrealized')?.value).toBeNull()
    })
})

describe('buildSample', () => {
    it('builds a mixed book with non-negative cash and both pnl signs', () => {
        const book = buildSample(now)
        expect(book.wallets).toHaveLength(3)
        expect(book.wallets.map((wallet) => wallet.mode).sort()).toEqual(['paper', 'real', 'real'])
        for (const cash of Object.values(book.cashByWallet)) expect(cash).toBeGreaterThanOrEqual(0)
        const bars = book.pnlBars.filter((bar) => bar.grain === 'day')
        expect(bars.some((bar) => bar.pnl > 0)).toBe(true)
        expect(bars.some((bar) => bar.pnl < 0)).toBe(true)
        expect(book.trades.some((trade) => trade.side === 'sell' && (trade.pnl ?? 0) < 0)).toBe(true)
        expect(new Date(book.lastRecordedAt).getTime()).toBeLessThanOrEqual(now.getTime())
    })
})
