import { describe, expect, it } from 'vite-plus/test'
import { toDashboardView, type OverviewInput } from '@/api/overview'
import type { PnlTotals, WalletCardModel, WalletInfo } from '@/api/types'
import { UNKNOWN_AMOUNT } from '@/lib/format'

const now = new Date('2026-11-12T02:18:00.000Z')

function totals(partial: Partial<PnlTotals> = {}): PnlTotals {
    return {
        wallets: 1,
        baseCurrency: 'USD',
        mixedCurrencies: false,
        totalValue: 0,
        valueChange: 0,
        cash: 0,
        swept: 0,
        openExposure: 0,
        unmarkedCost: 0,
        positionsValue: 0,
        valuedAtCost: false,
        realizedPnl: 0,
        unrealizedPnl: 0,
        unrealizedChange: 0,
        pnl: 0,
        fees: 0,
        deposits: 0,
        withdrawals: 0,
        sweeps: 0,
        winRate: null,
        wins: 0,
        losses: 0,
        scratches: 0,
        tradeCount: 0,
        openPositions: 0,
        ...partial,
    }
}

function card(partial: Partial<WalletCardModel> = {}): WalletCardModel {
    return {
        id: 1,
        key: 'paper',
        name: 'Paper',
        mode: 'paper',
        baseCurrency: 'USD',
        isArchived: false,
        sortOrder: 1,
        totalValue: 1000,
        valueChange: 40,
        cash: 360,
        swept: 0,
        openExposure: 640,
        unmarkedCost: 0,
        positionsValue: 640,
        valuedAtCost: false,
        realizedPnl: 12.5,
        unrealizedPnl: 4,
        unrealizedChange: 4,
        pnl: 16.5,
        fees: 0.75,
        deposits: 1000,
        withdrawals: 0,
        sweeps: 0,
        winRate: 100,
        wins: 1,
        losses: 0,
        scratches: 0,
        tradeCount: 2,
        openPositions: 1,
        lastActivityAt: '2026-11-04T16:00:00.000Z',
        sparkline: [
            {
                at: '2026-11-02T15:04:00.000Z',
                totalValue: 960,
                cash: 360,
                positionsValue: 600,
                openExposure: 600,
                unmarkedCost: 0,
                swept: 0,
                unrealizedPnl: 0,
                valuedAtCost: false,
            },
            {
                at: '2026-11-04T16:00:00.000Z',
                totalValue: 1000,
                cash: 360,
                positionsValue: 640,
                openExposure: 640,
                unmarkedCost: 0,
                swept: 0,
                unrealizedPnl: 4,
                valuedAtCost: false,
            },
        ],
        ...partial,
    }
}

function wallet(partial: Partial<WalletInfo> = {}): WalletInfo {
    return {
        id: 1,
        key: 'paper',
        name: 'Paper',
        mode: 'paper',
        venue: 'Coinbase',
        baseCurrency: 'USD',
        note: '',
        isArchived: false,
        sortOrder: 1,
        createdAt: '2026-11-01T15:00:00.000Z',
        ...partial,
    }
}

function input(partial: Partial<OverviewInput> = {}): OverviewInput {
    return {
        catalog: [wallet()],
        summary: {
            totals: totals({
                totalValue: 1000,
                cash: 360,
                openExposure: 640,
                realizedPnl: 12.5,
                unrealizedPnl: 4,
                fees: 0.75,
                winRate: 100,
                tradeCount: 2,
                openPositions: 1,
            }),
            modes: [],
            wallets: [card()],
        },
        equity: { wallets: [], total: [] },
        daily: { results: [], wallets: [] },
        symbols: { results: [] },
        fees: { total: 0, days: [] },
        positions: { markStaleAfterMinutes: 15, results: [] },
        trades: { total: 0, skip: 0, take: 0, results: [] },
        marks: { total: 0, skip: 0, take: 0, results: [] },
        ...partial,
    }
}

describe('overview mapping', () => {
    it('treats an empty catalog as an empty ledger', () => {
        const view = toDashboardView(input({ catalog: [], summary: { totals: totals(), modes: [], wallets: [] } }), { range: '30d', now })
        expect(view.emptyBook).toBe(true)
        expect(view.noMatches).toBe(false)
    })

    it('keeps a null unrealized P&L null and a real zero at zero', () => {
        const unmarked = toDashboardView(
            input({
                summary: {
                    totals: totals({
                        totalValue: 500,
                        cash: 100,
                        openExposure: 0,
                        unmarkedCost: 400,
                        unrealizedPnl: null,
                        openPositions: 1,
                        realizedPnl: 0,
                        fees: 0.5,
                        tradeCount: 1,
                    }),
                    modes: [],
                    wallets: [
                        card({
                            totalValue: 500,
                            valueChange: 0,
                            openExposure: 0,
                            unmarkedCost: 400,
                            unrealizedPnl: null,
                            valuedAtCost: true,
                            openPositions: 1,
                        }),
                    ],
                },
                positions: {
                    markStaleAfterMinutes: 15,
                    results: [
                        {
                            wallet: 'paper',
                            walletName: 'Paper',
                            mode: 'paper',
                            symbol: 'ETH-USD',
                            status: 'open',
                            qty: 1,
                            avgPrice: 400,
                            costBasis: 400,
                            lastMarkPrice: null,
                            lastMarkAt: null,
                            markIsStale: false,
                            markStaleAfterMinutes: 15,
                            unrealizedPnl: null,
                            unrealizedPnlPercent: null,
                            realizedPnl: 0,
                            feesPaid: 0.5,
                            marketValue: 400,
                            valuedAtCost: true,
                            targetPrice: null,
                            distanceToTarget: null,
                            distanceToTargetPercent: null,
                        },
                    ],
                },
                daily: {
                    results: [
                        {
                            date: '2026-11-02T00:00:00.000Z',
                            realizedPnl: 0,
                            unrealizedChange: null,
                            pnl: null,
                            fees: 0.5,
                            deposits: 0,
                            withdrawals: 0,
                            sweeps: 0,
                            totalValue: 500,
                            valueChange: 0,
                            cash: 100,
                            positionsValue: 400,
                            openExposure: 0,
                            unmarkedCost: 400,
                            valuedAtCost: true,
                            trades: 1,
                        },
                    ],
                    wallets: [],
                },
                symbols: {
                    results: [
                        {
                            symbol: 'ETH-USD',
                            realizedPnl: 0,
                            unrealizedPnl: null,
                            unrealizedChange: null,
                            pnl: null,
                            fees: 0.5,
                            trades: 1,
                            wins: 0,
                            losses: 0,
                            qty: 1,
                            marketValue: 400,
                            valuedAtCost: true,
                        },
                        {
                            symbol: 'BTC-USD',
                            realizedPnl: 8,
                            unrealizedPnl: 2,
                            unrealizedChange: 2,
                            pnl: 10,
                            fees: 0,
                            trades: 2,
                            wins: 1,
                            losses: 0,
                            qty: 1,
                            marketValue: 10,
                            valuedAtCost: false,
                        },
                    ],
                },
            }),
            { range: '30d', now },
        )
        expect(unmarked.kpis.find((kpi) => kpi.id === 'unrealized')).toMatchObject({ value: null, emptyLabel: UNKNOWN_AMOUNT, hint: 'No price mark' })
        expect(unmarked.kpis.find((kpi) => kpi.id === 'exposure')?.value).toBeNull()
        expect(unmarked.positions[0]?.unrealizedPnl).toBeNull()
        expect(unmarked.positions[0]?.unmarked).toBe(true)
        expect(unmarked.pnlBars).toEqual([{ label: 'Nov 2', pnl: null }])
        expect(unmarked.pnlBySymbol.map((row) => row.symbol)).toEqual(['BTC-USD', 'ETH-USD'])
        expect(unmarked.pnlBySymbol[1]?.pnl).toBeNull()
        expect(unmarked.wallets[0]?.unmarked).toBe(true)

        const flat = toDashboardView(
            input({
                summary: {
                    totals: totals({ totalValue: 1000, unrealizedPnl: 0, openExposure: 640, openPositions: 1, realizedPnl: 12.5 }),
                    modes: [],
                    wallets: [card({ unrealizedPnl: 0 })],
                },
            }),
            { range: 'all', now },
        )
        expect(flat.kpis.find((kpi) => kpi.id === 'unrealized')?.value).toBe(0)
        expect(flat.kpis.find((kpi) => kpi.id === 'exposure')?.value).toBe(640)
    })

    it('keeps wallet order, server value change, and aligned equity', () => {
        const second = card({
            id: 2,
            key: 'coinbase',
            name: 'Coinbase',
            mode: 'real',
            sortOrder: 2,
            totalValue: 200,
            valueChange: 20,
            sparkline: [],
        })
        const view = toDashboardView(
            input({
                catalog: [wallet(), wallet({ id: 2, key: 'coinbase', name: 'Coinbase', mode: 'real', venue: 'Coinbase', sortOrder: 2 })],
                summary: {
                    totals: totals(),
                    modes: [],
                    wallets: [card(), second],
                },
                equity: {
                    wallets: [
                        {
                            wallet: 'paper',
                            name: 'Paper',
                            mode: 'paper',
                            points: [
                                {
                                    at: '2026-11-04T16:00:00.000Z',
                                    totalValue: 1000,
                                    cash: 0,
                                    positionsValue: 0,
                                    openExposure: 0,
                                    unmarkedCost: 0,
                                    swept: 0,
                                    unrealizedPnl: 4,
                                    valuedAtCost: false,
                                },
                            ],
                        },
                        {
                            wallet: 'coinbase',
                            name: 'Coinbase',
                            mode: 'real',
                            points: [
                                {
                                    at: '2026-11-02T15:00:00.000Z',
                                    totalValue: 180,
                                    cash: 0,
                                    positionsValue: 0,
                                    openExposure: 0,
                                    unmarkedCost: 0,
                                    swept: 0,
                                    unrealizedPnl: 0,
                                    valuedAtCost: false,
                                },
                                {
                                    at: '2026-11-04T16:00:00.000Z',
                                    totalValue: 200,
                                    cash: 0,
                                    positionsValue: 0,
                                    openExposure: 0,
                                    unmarkedCost: 0,
                                    swept: 0,
                                    unrealizedPnl: 0,
                                    valuedAtCost: false,
                                },
                            ],
                        },
                    ],
                    total: [],
                },
            }),
            { range: '30d', now },
        )
        expect(view.wallets.map((item) => item.key)).toEqual(['paper', 'coinbase'])
        expect(view.wallets[0]).toMatchObject({ venue: 'Coinbase', value: 1000, change: 40, openPositions: 1 })
        expect(view.equity[0]?.points.map((point) => point.value)).toEqual([0, 1000])
        expect(view.equity[1]?.points.map((point) => point.value)).toEqual([180, 200])
        expect(view.equity[0]?.points.map((point) => point.label)).toEqual(view.equity[1]?.points.map((point) => point.label))
        expect(view.noMatches).toBe(false)
    })

    it('reports no match when the mode filter excludes every wallet', () => {
        const view = toDashboardView(input({ summary: { totals: totals({ wallets: 0 }), modes: [], wallets: [] } }), { range: '7d', now })
        expect(view.emptyBook).toBe(false)
        expect(view.noMatches).toBe(true)
        expect(view.walletNames.paper).toBe('Paper')
    })
})
