import { computed, ref } from 'vue'
import { describe, expect, it } from 'vite-plus/test'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import HomePage from './index.vue'
import { toDashboardView, type OverviewInput } from '@/api/overview'
import type { PnlTotals, TradeRow, WalletCardModel } from '@/api/types'
import { dashboardKey, type DashboardContext } from '@/composables/useDashboard'
import type { DashboardView } from '@/lib/book'
import { UNKNOWN_AMOUNT } from '@/lib/format'

const now = new Date('2026-11-12T02:18:00.000Z')

function context(view: DashboardView | null, status: DashboardContext['status']['value'] = 'ready'): DashboardContext {
    return {
        status: ref(status),
        error: ref(status === 'error' ? 'The read failed' : null),
        view: computed(() => view),
        refresh: async () => {},
    }
}

async function mountHome(view: DashboardView | null, status: DashboardContext['status']['value'] = 'ready') {
    const router = createRouter({
        history: createMemoryHistory(),
        routes: [
            { path: '/', component: HomePage },
            { path: '/trades', component: { template: '<div />' } },
            { path: '/wallets', component: { template: '<div />' } },
            { path: '/wallet/:key', component: { template: '<div />' } },
            { path: '/predictions', component: { template: '<div />' } },
        ],
    })
    await router.push('/')
    await router.isReady()
    const wrapper = mount(HomePage, {
        global: {
            plugins: [router, createPinia()],
            provide: {
                [dashboardKey as symbol]: context(view, status),
            },
            stubs: {
                EChart: { template: '<div data-testid="chart" />' },
            },
        },
    })
    return { wrapper, router }
}

function totals(partial: Partial<PnlTotals> = {}): PnlTotals {
    return {
        wallets: 1,
        baseCurrency: 'USD',
        mixedCurrencies: false,
        totalValue: 1000,
        valueChange: 25,
        cash: 360,
        swept: 0,
        openExposure: 0,
        unmarkedCost: 640,
        positionsValue: 640,
        valuedAtCost: true,
        realizedPnl: 12.5,
        unrealizedPnl: null,
        unrealizedChange: null,
        pnl: null,
        fees: 0.75,
        deposits: 1000,
        withdrawals: 0,
        sweeps: 0,
        winRate: 100,
        wins: 1,
        losses: 0,
        scratches: 0,
        tradeCount: 21,
        openPositions: 1,
        ...partial,
    }
}

function card(): WalletCardModel {
    return {
        id: 1,
        key: 'paper',
        name: 'Paper',
        mode: 'paper',
        baseCurrency: 'USD',
        isArchived: false,
        sortOrder: 1,
        totalValue: 1000,
        valueChange: 25,
        cash: 360,
        swept: 0,
        openExposure: 0,
        unmarkedCost: 640,
        positionsValue: 640,
        valuedAtCost: true,
        realizedPnl: 12.5,
        unrealizedPnl: null,
        unrealizedChange: null,
        pnl: null,
        fees: 0.75,
        deposits: 1000,
        withdrawals: 0,
        sweeps: 0,
        winRate: 100,
        wins: 1,
        losses: 0,
        scratches: 0,
        tradeCount: 21,
        openPositions: 1,
        lastActivityAt: '2026-11-10T16:00:00.000Z',
        sparkline: [
            {
                at: '2026-11-02T15:04:00.000Z',
                totalValue: 975,
                cash: 360,
                positionsValue: 615,
                openExposure: 0,
                unmarkedCost: 615,
                swept: 0,
                unrealizedPnl: null,
                valuedAtCost: true,
            },
            {
                at: '2026-11-10T16:00:00.000Z',
                totalValue: 1000,
                cash: 360,
                positionsValue: 640,
                openExposure: 0,
                unmarkedCost: 640,
                swept: 0,
                unrealizedPnl: null,
                valuedAtCost: true,
            },
        ],
    }
}

function trade(id: number): TradeRow {
    return {
        id,
        wallet: 'paper',
        walletName: 'Paper',
        mode: 'paper',
        symbol: id === 1 ? 'OLD-USD' : 'BTC-USD',
        title: 'Clip',
        side: 'buy',
        kind: 'fill',
        qty: 0.01,
        price: 64000,
        fee: 0.5,
        executedAt: new Date(Date.UTC(2026, 10, id)).toISOString(),
        recordedAt: new Date(Date.UTC(2026, 10, id, 1)).toISOString(),
        note: '',
        status: 'open',
        buyPrice: 64000,
        currentOrExitPrice: null,
        priceKind: 'unknown',
        exitPrice: null,
        lastMarkPrice: null,
        lastMarkAt: null,
        realizedPnl: 0,
        unrealizedPnl: null,
        pnl: null,
        pnlPercent: null,
        remainingQty: 0.01,
        valuedAtCost: true,
        targetPrice: null,
        orderRef: null,
        clientId: `fill-${id}`,
    }
}

function seeded(): OverviewInput {
    return {
        catalog: [
            {
                id: 1,
                key: 'paper',
                name: 'Paper',
                mode: 'paper',
                venue: 'Coinbase',
                baseCurrency: 'USD',
                note: '',
                isArchived: false,
                sortOrder: 1,
                createdAt: null,
            },
        ],
        summary: { totals: totals(), modes: [], wallets: [card()] },
        equity: {
            wallets: [
                {
                    wallet: 'paper',
                    name: 'Paper',
                    mode: 'paper',
                    points: [
                        {
                            at: '2026-11-10T16:00:00.000Z',
                            totalValue: 1000,
                            cash: 360,
                            positionsValue: 640,
                            openExposure: 0,
                            unmarkedCost: 640,
                            swept: 0,
                            unrealizedPnl: null,
                            valuedAtCost: true,
                        },
                    ],
                },
            ],
            total: [],
        },
        daily: {
            results: [
                {
                    date: '2026-11-10T00:00:00.000Z',
                    realizedPnl: 12.5,
                    unrealizedChange: null,
                    pnl: null,
                    fees: 0.75,
                    deposits: 0,
                    withdrawals: 0,
                    sweeps: 0,
                    totalValue: 1000,
                    valueChange: 25,
                    cash: 360,
                    positionsValue: 640,
                    openExposure: 0,
                    unmarkedCost: 640,
                    valuedAtCost: true,
                    trades: 1,
                },
            ],
            wallets: [],
        },
        symbols: {
            results: [
                {
                    symbol: 'BTC-USD',
                    realizedPnl: 12.5,
                    unrealizedPnl: null,
                    unrealizedChange: null,
                    pnl: null,
                    fees: 0.75,
                    trades: 2,
                    wins: 1,
                    losses: 0,
                    qty: 0.01,
                    marketValue: 640,
                    valuedAtCost: true,
                },
            ],
        },
        fees: { total: 0.75, days: [{ date: '2026-11-10T00:00:00.000Z', fees: 0.75 }] },
        positions: { markStaleAfterMinutes: 15, results: [] },
        trades: { total: 21, skip: 0, take: 200, results: Array.from({ length: 21 }, (_, index) => trade(index + 1)) },
        marks: { total: 0, skip: 0, take: 0, results: [] },
    }
}

describe('overview page', () => {
    it('shows a loading state before the ledger answers', async () => {
        const { wrapper } = await mountHome(null, 'loading')
        expect(wrapper.get('h1').text()).toBe('Overview')
        expect(wrapper.get('[data-testid="prediction-pnl"]').text()).toBe('Prediction markets')
        expect(wrapper.text()).toContain('Loading the book')
        expect(wrapper.find('[data-testid="kpi-value"]').exists()).toBe(false)
        expect(wrapper.find('[data-testid="chart"]').exists()).toBe(false)
    })

    it('shows an empty ledger', async () => {
        const view = toDashboardView(
            {
                catalog: [],
                summary: { totals: totals({ wallets: 0, unrealizedPnl: 0, tradeCount: 0, openPositions: 0 }), modes: [], wallets: [] },
                equity: { wallets: [], total: [] },
                daily: { results: [], wallets: [] },
                symbols: { results: [] },
                fees: { total: 0, days: [] },
                positions: { markStaleAfterMinutes: 15, results: [] },
                trades: { total: 0, skip: 0, take: 0, results: [] },
                marks: { total: 0, skip: 0, take: 0, results: [] },
            },
            { range: '30d', now },
        )
        const { wrapper } = await mountHome(view)
        expect(view.emptyBook).toBe(true)
        expect(wrapper.text()).toContain('No wallets yet')
        expect(wrapper.find('[data-testid="kpi-unrealized"]').exists()).toBe(false)
    })

    it('renders ledger data and a dash when unrealized P&L has no mark', async () => {
        const view = toDashboardView(seeded(), { range: '30d', now })
        const { wrapper, router } = await mountHome(view)
        expect(wrapper.text()).toContain('Paper')
        expect(wrapper.text()).toContain('Total value')
        expect(wrapper.get('[data-testid="kpi-unrealized"]').text()).toContain(UNKNOWN_AMOUNT)
        expect(wrapper.get('[data-testid="kpi-unrealized"]').text()).not.toContain('$0.00')
        expect(wrapper.get('[data-testid="kpi-realized"]').text()).toContain('+$12.50')
        expect(wrapper.get('[data-testid="kpi-fees"]').text()).toContain('$0.75')
        expect(wrapper.findAll('[data-testid="chart"]')).toHaveLength(4)
        expect(wrapper.find('[data-testid="trade-1"]').exists()).toBe(false)
        expect(wrapper.findAll('[data-testid="trade-21"]').length).toBeGreaterThan(0)

        await wrapper.get('[data-testid="wallet-paper"]').trigger('click')
        await flushPromises()
        expect(router.currentRoute.value.path).toBe('/wallet/paper')
        expect(router.currentRoute.value.query.wallet).toBe('paper')

        await router.push('/')
        await flushPromises()
        const again = await mountHome(view)
        await again.wrapper.findAll('[data-testid="trade-21"]')[0]!.trigger('click')
        await flushPromises()
        expect(again.router.currentRoute.value.path).toBe('/trades')
        expect(again.router.currentRoute.value.query.trade).toBe('21')
        expect(again.router.currentRoute.value.query.symbol).toBe('BTC-USD')
    })
})
