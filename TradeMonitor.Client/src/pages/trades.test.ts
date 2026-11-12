import { defineComponent } from 'vue'
import { afterEach, describe, expect, it, vi } from 'vite-plus/test'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { LedgerReadError, type LedgerClient, type LedgerQuery, type TradeDetailResponse, type TradeFill, type TradeRow } from '@/api'
import { ledgerClientKey } from '@/composables/useLedger'
import TradeDrawer from '@/components/TradeDrawer.vue'
import TradesPage from './trades.vue'

vi.mock('@/lib/loadPriceChart', () => ({
    loadPriceChart: async () => ({
        __esModule: true,
        default: {
            name: 'PriceChart',
            props: ['points', 'markers', 'dark'],
            template: '<div data-testid="price-chart">chart</div>',
        },
    }),
}))

const buyAt = '2026-11-02T15:04:00.000Z'
const sellAt = '2026-11-03T16:00:00.000Z'

function row(partial: Partial<TradeRow> = {}): TradeRow {
    return {
        id: 2,
        wallet: 'paper',
        walletName: 'Paper',
        mode: 'paper',
        symbol: 'BTC-USD',
        title: 'First clip',
        side: 'buy',
        qty: 0.01,
        price: 64000,
        fee: 0.5,
        executedAt: buyAt,
        recordedAt: buyAt,
        note: 'Paper fill',
        status: 'open',
        buyPrice: 64000,
        currentOrExitPrice: 64100,
        priceKind: 'mark',
        exitPrice: null,
        lastMarkPrice: 64100,
        lastMarkAt: '2026-11-02T15:10:00.000Z',
        realizedPnl: 0,
        unrealizedPnl: 1,
        pnl: 1,
        pnlPercent: 0.16,
        remainingQty: 0.01,
        valuedAtCost: false,
        targetPrice: 64500,
        orderRef: 'ord-1',
        clientId: 'fill-1',
        ...partial,
    }
}

function fill(partial: Partial<TradeFill> = {}): TradeFill {
    return {
        tradeId: 4,
        side: 'sell',
        symbol: 'BTC-USD',
        qty: 0.004,
        price: 65000,
        buyFee: 0.2,
        sellFee: 0.25,
        cost: 256.2,
        proceeds: 259.75,
        realizedPnl: 3.55,
        executedAt: sellAt,
        ...partial,
    }
}

function detailFor(id: number): TradeDetailResponse {
    if (id === 1) {
        return {
            result: row({
                id: 1,
                symbol: 'SOL-USD',
                title: 'No mark',
                price: 5,
                buyPrice: 5,
                fee: 1,
                qty: 2,
                currentOrExitPrice: null,
                priceKind: 'unknown',
                unrealizedPnl: null,
                pnl: null,
                pnlPercent: null,
                targetPrice: 9,
                note: 'no mark',
                valuedAtCost: true,
            }),
            fills: [],
            markers: [{ tradeId: 1, side: 'buy', at: buyAt, price: 5, qty: 2 }],
            priceMarks: [],
            hasPriceMarks: false,
            chartFrom: buyAt,
            chartTo: buyAt,
        }
    }
    return {
        result: row({
            id,
            side: 'sell',
            price: 65000,
            buyPrice: 64000,
            currentOrExitPrice: 65000,
            priceKind: 'exit',
            status: 'closed',
            realizedPnl: 3.55,
            unrealizedPnl: null,
            pnl: 3.55,
            pnlPercent: 1.4,
        }),
        fills: [fill()],
        markers: [
            { tradeId: id, side: 'buy', at: buyAt, price: 64000, qty: 0.01 },
            { tradeId: 4, side: 'sell', at: sellAt, price: 65000, qty: 0.004 },
        ],
        priceMarks: [
            {
                id: 10,
                clientId: 'mark-1',
                symbol: 'BTC-USD',
                price: 64100,
                at: '2026-11-02T15:10:00.000Z',
                source: 'agent',
                wallet: 'paper',
                recordedAt: '2026-11-02T15:10:00.000Z',
            },
            {
                id: 11,
                clientId: 'mark-2',
                symbol: 'BTC-USD',
                price: 65000,
                at: sellAt,
                source: 'agent',
                wallet: 'paper',
                recordedAt: sellAt,
            },
        ],
        hasPriceMarks: true,
        chartFrom: buyAt,
        chartTo: sellAt,
    }
}

let wrapper: VueWrapper | undefined

afterEach(() => {
    wrapper?.unmount()
    wrapper = undefined
})

async function mountTrades(path: string, client: LedgerClient) {
    const router = createRouter({
        history: createMemoryHistory(),
        routes: [
            { path: '/trades', component: { template: '<div />' } },
            { path: '/', component: { template: '<div />' } },
            { path: '/signin', component: { template: '<div />' } },
        ],
    })
    await router.push(path)
    await router.isReady()
    const Harness = defineComponent({
        components: { TradesPage, TradeDrawer },
        template: '<div><TradesPage /><TradeDrawer /></div>',
    })
    wrapper = mount(Harness, {
        global: {
            plugins: [router, createPinia()],
            provide: {
                [ledgerClientKey as symbol]: client,
            },
        },
    })
    await flushPromises()
    return { router, wrapper }
}

function clientWith(total = 40, fail?: LedgerReadError): { client: LedgerClient; calls: LedgerQuery[] } {
    const calls: LedgerQuery[] = []
    const client = {
        async trades(request: LedgerQuery = {}) {
            calls.push({ ...request })
            if (fail) throw fail
            const skip = request.skip ?? 0
            const take = request.take ?? 25
            const count = Math.min(take, Math.max(0, total - skip))
            return {
                total,
                skip,
                take,
                results: Array.from({ length: count }, (_, index) => {
                    const id = skip + index + 1
                    if (id === 1) {
                        return row({
                            id,
                            symbol: 'SOL-USD',
                            title: 'No mark',
                            buyPrice: 5,
                            price: 5,
                            qty: 2,
                            currentOrExitPrice: null,
                            priceKind: 'unknown',
                            unrealizedPnl: null,
                            pnl: null,
                            pnlPercent: null,
                            note: 'no mark',
                        })
                    }
                    return row({ id })
                }),
            }
        },
        async trade(id: number) {
            if (id === 99) throw new LedgerReadError(404, 'UnknownTrade', "Unknown trade '99'.")
            return detailFor(id)
        },
    }
    return { client: client as unknown as LedgerClient, calls }
}

async function clickText(root: VueWrapper, selector: string, text: string) {
    const button = root
        .get(selector)
        .findAll('button')
        .find((item) => item.text() === text)
    if (!button) throw new Error(`missing ${text}`)
    await button.trigger('click')
    await flushPromises()
}

describe('trades page', () => {
    it('keeps filters, sort, and paging in the url and sends them to the server', async () => {
        const { client, calls } = clientWith()
        const { router, wrapper: page } = await mountTrades('/trades?wallet=paper&mode=paper', client)

        expect(calls[0]).toMatchObject({ wallet: 'paper', mode: 'paper', skip: 0, take: 25, side: null })
        expect(typeof calls[0]?.from).toBe('string')
        expect(page.get('[data-testid="page-status"]').text()).toContain('Showing 1-25 of 40')
        expect(page.get('[data-testid="pnl-1"]').text()).toBe('-')
        expect(page.get('[data-testid="exit-1"]').text()).toBe('-')
        expect(page.get('[data-testid="trade-row-2"]').text()).toContain('Mark')
        expect(page.get('[data-testid="trade-row-2"]').text()).toContain('$64,000.00')

        await clickText(page, '[data-testid="side-filter"]', 'Buy')
        expect(router.currentRoute.value.query.side).toBe('buy')
        expect(router.currentRoute.value.query.wallet).toBe('paper')
        expect(calls.at(-1)).toMatchObject({ side: 'buy', skip: 0 })

        await page.get('[data-testid="filter-symbol"]').setValue('ETH-USD')
        await page.get('[data-testid="symbol-form"]').trigger('submit')
        await flushPromises()
        expect(router.currentRoute.value.query.symbol).toBe('ETH-USD')
        expect(calls.at(-1)?.symbol).toBe('ETH-USD')

        await page.get('[data-testid="filter-search"]').setValue('clip')
        await page.get('[data-testid="search-form"]').trigger('submit')
        await flushPromises()
        expect(router.currentRoute.value.query.q).toBe('clip')
        expect(calls.at(-1)?.q).toBe('clip')

        await clickText(page, '[data-testid="outcome-filter"]', 'Lost')
        expect(router.currentRoute.value.query.outcome).toBe('lost')
        expect(calls.at(-1)?.outcome).toBe('lost')

        await clickText(page, '[data-testid="trades-table"] thead', 'P&L')
        expect(router.currentRoute.value.query.order).toBe('pnl')
        expect(router.currentRoute.value.query.dir).toBeUndefined()
        expect(calls.at(-1)).toMatchObject({ orderBy: 'pnl', descending: true })

        await clickText(page, '[data-testid="trades-table"] thead', 'P&L')
        expect(router.currentRoute.value.query.dir).toBe('asc')
        expect(calls.at(-1)).toMatchObject({ orderBy: 'pnl', descending: false })

        await page.get('[data-testid="page-next"]').trigger('click')
        await flushPromises()
        expect(router.currentRoute.value.path).toBe('/trades')
        expect(router.currentRoute.value.query.page).toBe('2')
        expect(calls.at(-1)).toMatchObject({ skip: 25, take: 25, wallet: 'paper', side: 'buy', q: 'clip' })
        expect(page.get('[data-testid="page-status"]').text()).toContain('Showing 26-40 of 40')

        await clickText(page, '[data-testid="side-filter"]', 'Sell')
        expect(router.currentRoute.value.query.page).toBeUndefined()
        expect(calls.at(-1)?.skip).toBe(0)
    })

    it('omits the date bound when the range is all', async () => {
        const { client, calls } = clientWith()
        await mountTrades('/trades?range=all', client)
        expect(calls[0]?.from).toBeNull()
        expect(calls[0]?.to).toBeNull()
        expect(calls[0]?.mode).toBeNull()
    })

    it('opens a trade in the drawer without leaving the page', async () => {
        const { client } = clientWith()
        const { router, wrapper: page } = await mountTrades('/trades?wallet=paper', client)
        expect(page.find('[data-testid="trade-drawer"]').exists()).toBe(false)

        await page.get('[data-testid="trade-row-2"]').trigger('click')
        await flushPromises()

        expect(router.currentRoute.value.path).toBe('/trades')
        expect(router.currentRoute.value.query).toMatchObject({ wallet: 'paper', trade: '2' })
        const chart = page.get('[data-testid="trade-chart"]')
        expect(chart.attributes('data-empty')).toBe('false')
        expect(chart.attributes('data-points')).toContain('=64000')
        expect(chart.attributes('data-points')).toContain('=65000')
        expect(chart.attributes('data-markers')).toContain('Buy 0.01')
        expect(chart.attributes('data-markers')).toContain('Sell 0.004')
        expect(chart.text()).not.toContain('No price marks recorded')
        expect(page.get('[data-testid="price-chart"]').text()).toBe('chart')
        expect(page.get('[data-testid="trade-fills"]').text()).toContain('0.004')
        expect(page.get('[data-testid="buy-price"]').text()).toContain('$64,000.00')
        expect(page.get('[data-testid="trade-fees"]').text()).toContain('$0.50')
        expect(page.get('[data-testid="realized-pnl"]').text()).toContain('$3.55')
        expect(page.get('[data-testid="unrealized-pnl"]').text()).toBe('-')
        expect(page.get('[data-testid="target-price"]').text()).toContain('$64,500.00')
        expect(page.get('[data-testid="trade-note"]').text()).toBe('Paper fill')

        await page.get('[data-testid="close-trade"]').trigger('click')
        await flushPromises()
        expect(router.currentRoute.value.query.trade).toBeUndefined()
        expect(router.currentRoute.value.query.wallet).toBe('paper')
        expect(page.find('[data-testid="trade-drawer"]').exists()).toBe(false)
    })

    it('shows fills and a note when a trade has no price marks', async () => {
        const { client } = clientWith()
        const { wrapper: page } = await mountTrades('/trades?trade=1', client)
        expect(page.get('[data-testid="trade-chart"]').text()).toContain('No price marks recorded')
        expect(page.get('[data-testid="trade-chart"]').attributes('data-empty')).toBe('true')
        expect(page.text()).not.toContain('Loading price chart')
        expect(page.get('[data-testid="trade-drawer"]').text()).toContain('SOL-USD')
        expect(page.get('[data-testid="trade-fills"]').text()).toContain('$5.00')
        expect(page.get('[data-testid="unrealized-pnl"]').text()).toBe('-')
        expect(page.text()).toContain('No price mark, so unrealized P&L is unknown.')
        expect(page.get('[data-testid="trade-note"]').text()).toBe('no mark')
    })

    it('shows loading, then an error, for a missing trade', async () => {
        let release: (value: TradeDetailResponse) => void = () => {}
        const pending = new Promise<TradeDetailResponse>((resolve) => {
            release = resolve
        })
        const { client } = clientWith()
        const slow = {
            trades: (request?: LedgerQuery) => client.trades(request),
            trade: () => pending,
        } as unknown as LedgerClient
        const { wrapper: page } = await mountTrades('/trades?trade=2', slow)
        expect(page.text()).toContain('Loading trade')

        release(detailFor(2))
        await flushPromises()
        expect(page.get('[data-testid="trade-fills"]').text()).toContain('0.004')
        page.unmount()

        const missing = clientWith()
        const { wrapper: failed } = await mountTrades('/trades?trade=99', missing.client)
        expect(failed.text()).toContain('Could not load this trade')
        expect(failed.text()).toContain("Unknown trade '99'.")
    })

    it('asks a signed-out reader to sign in and exports the filtered set from the first row', async () => {
        URL.createObjectURL = () => 'blob:trades'
        URL.revokeObjectURL = () => {}
        const denied = clientWith(0, new LedgerReadError(401, 'Unauthorized', 'Sign in as an admin to read the ledger.'))
        const { wrapper: page } = await mountTrades('/trades', denied.client)
        expect(page.text()).toContain('Sign in as an admin to read the ledger.')
        expect(page.get('a[href="/signin"]').attributes('href')).toBe('/signin')
        page.unmount()

        const { client, calls } = clientWith()
        const { wrapper: ready } = await mountTrades('/trades?wallet=paper&page=2', client)
        await ready.get('[data-testid="export-csv"]').trigger('click')
        await flushPromises()
        const exported = calls.filter((call) => call.take === 1000)
        expect(exported[0]).toMatchObject({ wallet: 'paper', skip: 0, take: 1000 })
    })
})
