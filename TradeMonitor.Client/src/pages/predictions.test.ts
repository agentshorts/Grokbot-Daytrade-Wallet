import { afterEach, describe, expect, it } from 'vite-plus/test'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { ledgerClientKey } from '@/composables/useLedger'
import type { LedgerClient } from '@/api'
import type { PredictionPnlResponse } from '@/api/types'
import PredictionsPage from './predictions.vue'

let wrapper: VueWrapper | undefined

afterEach(() => {
    wrapper?.unmount()
    wrapper = undefined
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
    })
})
