import { defineComponent } from 'vue'
import { afterEach, describe, expect, it, vi } from 'vite-plus/test'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import type { ContractDetail, ContractQuery, LedgerClient } from '@/api'
import { ledgerClientKey } from '@/composables/useLedger'
import ContractPage from '@/pages/prediction-contract/[ticker].vue'

vi.mock('@/lib/loadPriceChart', () => ({
    loadPriceChart: async () => ({
        __esModule: true,
        default: defineComponent({
            name: 'PriceChart',
            props: ['points', 'compare', 'markers', 'dark', 'empty'],
            template: '<div data-testid="price-chart">{{ points.length }} yes, {{ compare.length }} no, {{ markers.length }} marks</div>',
        }),
    }),
}))

let wrapper: VueWrapper | undefined

afterEach(() => {
    wrapper?.unmount()
    wrapper = undefined
})

function detail(partial: Partial<ContractDetail> = {}): ContractDetail {
    return {
        ticker: 'KXBTCD-26NOV1617-T64999.99',
        productId: 'KXBTCD-26NOV1617-T64999.99-KALSHI',
        series: 'KXBTCD',
        title: 'Bitcoin price today?',
        subtitle: '$64,999.99 or above',
        underlying: 'BTC',
        strike: 64999.99,
        direction: 'above',
        status: 'active',
        closeAt: '2026-11-16T22:00:00.000Z',
        closeCentral: '4:00 PM CST, Nov 16',
        closeEastern: '5:00 PM EST, Nov 16',
        yesCents: 56,
        yesBidCents: 54,
        yesAskCents: 55,
        yesLabel: 'bid/ask',
        noCents: 45.5,
        noBidCents: 45,
        noAskCents: 46,
        noLabel: 'bid/ask',
        noImplied: false,
        noLineImplied: true,
        highCents: 60,
        lowCents: 22,
        rangeCents: 38,
        rangePercent: 172.7,
        reversals: 3,
        oscillating: true,
        volume: 128,
        openInterest: 40,
        lookbackHours: 6,
        thresholdCents: 5,
        allAvailable: false,
        source: 'kalshi',
        error: null,
        points: [
            { at: '2026-11-16T18:00:00.000Z', yesCents: 40, noCents: 60 },
            { at: '2026-11-16T18:05:00.000Z', yesCents: 56, noCents: 44 },
        ],
        marks: [
            { at: '2026-11-16T18:00:00.000Z', kind: 'low', text: 'Low' },
            { at: '2026-11-16T18:05:00.000Z', kind: 'high', text: 'High' },
            { at: '2026-11-16T18:02:00.000Z', kind: 'buy', text: 'Buy 4 @ 40c' },
        ],
        holdings: [
            {
                wallet: 'pm-KXBTCD-26NOV1617-T64999.99-KALSHI',
                name: 'BTC 65k',
                side: 'YES',
                qty: 4,
                avgCost: 0.4,
                unrealizedPnl: -0.2,
                fills: [{ tradeId: 7, side: 'buy', qty: 4, price: 0.4, at: '2026-11-16T18:02:00.000Z' }],
            },
        ],
        ...partial,
    }
}

async function mountPage(
    contract: (query: ContractQuery) => Promise<{ result: ContractDetail }>,
    path = '/predictions/contract/KXBTCD-26NOV1617-T64999.99?hours=6&cents=5',
) {
    const client = { contract } as unknown as LedgerClient
    const router = createRouter({
        history: createMemoryHistory(),
        routes: [
            { path: '/predictions/contract/:ticker', component: ContractPage },
            { path: '/predictions', component: { template: '<div />' } },
            { path: '/wallet/:key', component: { template: '<div />' } },
        ],
    })
    await router.push(path)
    await router.isReady()
    wrapper = mount(ContractPage, {
        global: {
            plugins: [router, createPinia()],
            provide: { [ledgerClientKey as symbol]: client },
        },
    })
    await flushPromises()
    return wrapper
}

describe('contract details', () => {
    it('shows both prices, the chart, and the open wallet', async () => {
        const calls: ContractQuery[] = []
        const page = await mountPage(async (query) => {
            calls.push(query)
            return { result: detail() }
        })

        expect(calls[0]).toMatchObject({ ticker: 'KXBTCD-26NOV1617-T64999.99', lookbackHours: 6, thresholdCents: 5 })
        expect(page.get('[data-testid=contract-title]').text()).toBe('Bitcoin price today?')
        expect(page.text()).toContain('$64,999.99 or above')
        expect(page.text()).toContain('BTC')
        expect(page.text()).toContain('4:00 PM CST, Nov 16')
        expect(page.text()).toContain('5:00 PM EST, Nov 16')
        expect(page.get('[data-testid=contract-yes]').text()).toBe('54/55c bid/ask')
        expect(page.get('[data-testid=contract-no]').text()).toBe('45/46c bid/ask')
        expect(page.get('[data-testid=contract-note]').text()).toContain('live bid and ask')
        expect(page.get('[data-testid=contract-reversals]').text()).toBe('3')
        expect(page.text()).toContain('128')
        expect(page.text()).toContain('40')
        expect(page.get('[data-testid=contract-chart]').text()).toContain('2 yes, 2 no, 3 marks')
        const holding = page.get('[data-testid=contract-holding]')
        expect(holding.text()).toContain('BTC 65k')
        expect(holding.text()).toContain('YES')
        expect(holding.text()).toContain('40c')
        expect(holding.text()).toContain('Buy 4')

        await page.get('[data-testid=contract-range-all]').trigger('click')
        await flushPromises()
        expect(calls.at(-1)?.allAvailable).toBe(true)
        expect(calls.at(-1)?.lookbackHours).toBeUndefined()
    })

    it('works for a ticker with no wallet and says when NO is implied', async () => {
        const page = await mountPage(async () => ({
            result: detail({
                holdings: [],
                noBidCents: null,
                noAskCents: null,
                noCents: 44,
                noLabel: 'implied',
                noImplied: true,
                volume: null,
                openInterest: null,
            }),
        }))
        expect(page.get('[data-testid=contract-empty-holding]').text()).toContain('No open pm- wallet')
        expect(page.get('[data-testid=contract-no]').text()).toBe('44c implied')
        expect(page.get('[data-testid=contract-note]').text()).toContain('publishes the YES price')
        expect(page.text()).not.toContain('Open interest')
    })
})
