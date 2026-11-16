import { afterEach, describe, expect, it } from 'vite-plus/test'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import type { LedgerClient, OscillationQuery, OscillationResponse } from '@/api'
import { ledgerClientKey } from '@/composables/useLedger'
import { OSCILLATION_PREFS_KEY } from '@/lib/oscillationPrefs'
import PredictionsPage from '@/pages/predictions.vue'

let wrapper: VueWrapper | undefined

afterEach(() => {
    wrapper?.unmount()
    wrapper = undefined
    localStorage.removeItem(OSCILLATION_PREFS_KEY)
})

const held: OscillationResponse = {
    lookbackHours: 6,
    thresholdCents: 5,
    lookup: null,
    lookupError: null,
    results: [
        {
            ticker: 'KXBTCD-26NOV1617-T64999.99',
            productId: 'KXBTCD-26NOV1617-T64999.99-KALSHI',
            series: 'KXBTCD',
            wallet: 'pm-KXBTCD-26NOV1617-T64999.99-KALSHI',
            name: 'BTC 64999.99',
            qty: 4,
            priceCents: 56,
            yesCents: 56,
            yesBidCents: null,
            yesAskCents: null,
            yesLabel: 'last',
            noCents: 44,
            noBidCents: null,
            noAskCents: null,
            noLabel: 'implied',
            noImplied: true,
            highCents: 60,
            lowCents: 43,
            rangeCents: 17,
            rangePercent: 39.5,
            reversals: 4,
            oscillating: true,
            sparkline: [43, 56, 48, 60],
            closeAt: '2026-11-16T22:00:00.000Z',
            closeCentral: '4:00 PM CST, Nov 16',
            source: 'kalshi',
            error: null,
        },
    ],
}

async function mountPage(oscillation: (query?: OscillationQuery) => Promise<OscillationResponse>) {
    const client = {
        async predictions() {
            return { wallets: [], total: { wallets: 0, bought: 0, sold: 0, fees: 0, realizedPnl: 0 } }
        },
        oscillation,
    } as unknown as LedgerClient
    const router = createRouter({
        history: createMemoryHistory(),
        routes: [
            { path: '/predictions', component: PredictionsPage },
            { path: '/wallet/:key', component: { template: '<div />' } },
            { path: '/predictions/contract/:ticker', component: { template: '<div />' } },
        ],
    })
    await router.push('/predictions')
    await router.isReady()
    wrapper = mount(PredictionsPage, {
        global: {
            plugins: [router, createPinia()],
            provide: { [ledgerClientKey as symbol]: client },
        },
    })
    await flushPromises()
    return wrapper
}

describe('oscillation view', () => {
    it('lists swinging contracts and looks up a ticker with no wallet', async () => {
        const calls: OscillationQuery[] = []
        const page = await mountPage(async (query) => {
            calls.push(query ?? {})
            if (query?.ticker) {
                return {
                    ...held,
                    lookup: {
                        ...held.results[0]!,
                        ticker: 'KXBTCD-26NOV1617-T65249.99',
                        productId: 'KXBTCD-26NOV1617-T65249.99-KALSHI',
                        wallet: null,
                        name: 'KXBTCD-26NOV1617-T65249.99',
                        qty: 0,
                        oscillating: false,
                        reversals: 1,
                    },
                }
            }
            return held
        })

        expect(page.get('h1').text()).toBe('Prediction markets')
        await page.get('[data-testid=open-oscillation]').trigger('click')
        await flushPromises()

        expect(page.get('h1').text()).toBe('Oscillation')
        expect(calls[0]).toMatchObject({ lookbackHours: 6, thresholdCents: 5 })
        const row = page.get('[data-testid="oscillation-KXBTCD-26NOV1617-T64999.99"]')
        expect(row.text()).toContain('Swinging')
        expect(row.get('[data-testid=oscillation-yes]').text()).toBe('56c last')
        expect(row.get('[data-testid=oscillation-no]').text()).toBe('44c implied')
        expect(row.get('[data-testid=oscillation-contract]').attributes('href')).toContain('/predictions/contract/KXBTCD-26NOV1617-T64999.99')
        expect(row.text()).toContain('17c')
        expect(row.text()).toContain('40%')
        expect(row.text()).toContain('4')
        expect(row.text()).toContain('4:00 PM CST, Nov 16')
        expect(row.text()).toContain('Kalshi')
        expect(row.get('svg').attributes('aria-label')).toBe('Value over the selected range')

        await page.get('[data-testid=oscillation-search]').setValue('KXBTCD-26NOV1617-T65249.99-KALSHI')
        await page.get('form').trigger('submit')
        await flushPromises()

        expect(calls.at(-1)?.ticker).toBe('KXBTCD-26NOV1617-T65249.99-KALSHI')
        const lookup = page.get('[data-testid=oscillation-lookup]')
        expect(lookup.text()).toContain('KXBTCD-26NOV1617-T65249.99')
        expect(lookup.text()).not.toContain('Swinging')
    })
})
