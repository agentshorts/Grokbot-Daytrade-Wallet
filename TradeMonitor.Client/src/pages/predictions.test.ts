import { afterEach, describe, expect, it } from 'vite-plus/test'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { ledgerClientKey } from '@/composables/useLedger'
import type { LedgerClient } from '@/api'
import type { PredictionBookingResponse, PredictionPnlResponse } from '@/api/types'
import { UI_STATE_KEY } from '@/lib/uiState'
import PredictionsPage from './predictions.vue'

let wrapper: VueWrapper | undefined

afterEach(() => {
    wrapper?.unmount()
    wrapper = undefined
    localStorage.removeItem(UI_STATE_KEY)
})

const sample: PredictionPnlResponse = {
    wallets: [
        {
            wallet: 'pm-KXBTCD-26NOV1210-T71199.99-KALSHI',
            name: 'BTC 71199.99',
            symbol: 'KXBTCD-26NOV1210-T71199.99-KALSHI',
            bought: 8.25,
            sold: 8.25,
            fees: 0.335,
            realizedPnl: -0.5,
            settlement: 'closed',
        },
        {
            wallet: 'pm-win',
            name: 'Winner',
            symbol: 'CONTRACT',
            bought: 2,
            sold: 2,
            fees: 0.05,
            realizedPnl: 1.15,
            settlement: 'settled-1',
        },
    ],
    total: { wallets: 2, bought: 10.25, sold: 10.25, fees: 0.385, realizedPnl: 0.65 },
}

async function mountPage(client: Partial<LedgerClient>) {
    const router = createRouter({
        history: createMemoryHistory(),
        routes: [
            { path: '/predictions', component: PredictionsPage },
            { path: '/wallet/:key', component: { template: '<div />' } },
        ],
    })
    await router.push('/predictions')
    await router.isReady()
    wrapper = mount(PredictionsPage, {
        global: {
            plugins: [router, createPinia()],
            provide: {
                [ledgerClientKey as symbol]: client,
            },
        },
    })
    await flushPromises()
    return wrapper
}

describe('prediction P&L page', () => {
    it('shows each contract and the total', async () => {
        const page = await mountPage({
            async predictions() {
                return sample
            },
        })
        expect(page.get('h1').text()).toBe('Prediction markets')
        const row = page.get('[data-testid="prediction-pm-KXBTCD-26NOV1210-T71199.99-KALSHI"]')
        expect(row.text()).toContain('BTC 71199.99')
        expect(row.text()).toContain('8.25')
        expect(row.text()).toContain('Closed')
        expect(row.text()).toContain('-$0.50')
        expect(page.get('[data-testid="prediction-pm-win"]').text()).toContain('Settled at 1')
        const total = page.get('[data-testid="prediction-total"]')
        expect(total.text()).toContain('Total (2)')
        expect(total.text()).toContain('10.25')
        expect(total.text()).toContain('+$0.65')
    })

    it('shows an empty book', async () => {
        const page = await mountPage({
            async predictions() {
                return { wallets: [], total: { wallets: 0, bought: 0, sold: 0, fees: 0, realizedPnl: 0 } }
            },
        })
        expect(page.text()).toContain('No prediction wallets')
        expect(page.find('[data-testid="prediction-booking"]').exists()).toBe(false)
    })

    it('shows booking status and filters it with the predictions filter', async () => {
        const booked: PredictionBookingResponse = {
            results: [
                {
                    productId: 'KXBTCD-26NOV1617-T63749.99-KALSHI',
                    firstSeen: '2026-11-16T17:00:00.000Z',
                    lastSeen: '2026-11-16T17:05:00.000Z',
                    status: 'problem',
                    wallet: null,
                    detail: 'missing cash total or YES/NO side',
                },
                {
                    productId: 'OTHER-KALSHI',
                    firstSeen: '2026-11-16T17:00:00.000Z',
                    lastSeen: '2026-11-16T17:02:00.000Z',
                    status: 'booked',
                    wallet: 'pm-OTHER-KALSHI',
                    detail: null,
                },
            ],
        }
        const page = await mountPage({
            async predictions() {
                return sample
            },
            async predictionBookings() {
                return booked
            },
        })
        const problem = page.get('[data-testid="booking-KXBTCD-26NOV1617-T63749.99-KALSHI"]')
        expect(problem.text()).toContain('Problem')
        expect(problem.text()).toContain('missing cash total or YES/NO side')
        expect(problem.text()).toContain('11:05 AM')
        expect(page.get('[data-testid="booking-OTHER-KALSHI"]').text()).toContain('Booked')
        await page.get('[data-testid="text-filter-input-predictions"]').setValue('OTHER-KALSHI')
        expect(page.find('[data-testid="booking-KXBTCD-26NOV1617-T63749.99-KALSHI"]').exists()).toBe(false)
        expect(page.get('[data-testid="booking-OTHER-KALSHI"]').text()).toContain('pm-OTHER-KALSHI')
        await page.get('[data-testid="collapse-prediction-booking"]').trigger('click')
        await flushPromises()
        const stored = JSON.parse(localStorage.getItem(UI_STATE_KEY) ?? '{}') as { hidden?: string[] }
        expect(stored.hidden).toContain('prediction-booking')
    })

    it('keeps the empty book when no contracts have been seen', async () => {
        const page = await mountPage({
            async predictions() {
                return { wallets: [], total: { wallets: 0, bought: 0, sold: 0, fees: 0, realizedPnl: 0 } }
            },
            async predictionBookings() {
                return { results: [] }
            },
        })
        expect(page.text()).toContain('No prediction wallets')
        expect(page.find('[data-testid="prediction-booking"]').exists()).toBe(false)
    })

    it('shows a booking problem when the book itself is empty', async () => {
        const page = await mountPage({
            async predictions() {
                return { wallets: [], total: { wallets: 0, bought: 0, sold: 0, fees: 0, realizedPnl: 0 } }
            },
            async predictionBookings() {
                return {
                    results: [
                        {
                            productId: 'BARE-KALSHI',
                            firstSeen: '2026-11-16T17:00:00.000Z',
                            lastSeen: '2026-11-16T17:05:00.000Z',
                            status: 'problem',
                            wallet: null,
                            detail: 'missing cash total or YES/NO side',
                        },
                    ],
                }
            },
        })
        expect(page.text()).toContain('No prediction wallets')
        expect(page.get('[data-testid="booking-BARE-KALSHI"]').text()).toContain('Problem')
    })
})
