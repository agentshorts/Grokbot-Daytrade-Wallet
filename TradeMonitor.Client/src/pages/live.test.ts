import { ref } from 'vue'
import { describe, expect, it } from 'vite-plus/test'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import type { LiveBook, LiveClient, LiveTradeMark, TrackedSymbols } from '@/api/live'
import type { LedgerClient, WalletsResponse } from '@/api'
import { ledgerClientKey } from '@/composables/useLedger'
import { liveClientKey } from '@/composables/useLive'
import { liveStreamKey, type LiveStream } from '@/composables/useLiveStream'
import LivePage from './live.vue'

const start = 1_780_000_200

function book(): LiveBook {
    return {
        wallet: 'paper',
        walletName: 'Paper',
        mode: 'Paper',
        simulated: false,
        simulationId: null,
        symbol: 'BTC-USD',
        price: 100,
        cash: 1000,
        realizedPnl: 0,
        lots: [],
        average: { openQty: 0, openCost: 0, avgCost: 0, sellQty: 0, price: 100, matchedCost: 0, profit: null },
        orders: [],
        tradeId: null,
    }
}

const saved: LiveTradeMark = {
    tradeId: 1,
    side: 'Buy',
    kind: 'Fill',
    qty: 2,
    price: 10,
    fee: 0.25,
    executedAt: '2026-06-15T12:00:00.000Z',
    simulated: false,
}

const sold: LiveTradeMark = {
    tradeId: 2,
    side: 'Sell',
    kind: 'Fill',
    qty: 1,
    price: 12,
    fee: 0,
    executedAt: '2026-06-15T12:01:00.000Z',
    simulated: false,
}

async function mountLive(options?: { real?: boolean; simulatedMark?: boolean }) {
    const link = ref<'off' | 'live' | 'reconnecting'>('live')
    const listeners: ((tick: { productId: string; price: number; at: string | null }) => void)[] = []
    const stream: LiveStream = {
        link,
        subscribe(_product, onTick) {
            listeners.push(onTick)
            return () => {
                const index = listeners.indexOf(onTick)
                if (index >= 0) listeners.splice(index, 1)
            }
        },
    }
    let placed = false
    const products = viProducts()
    const client = {
        products,
        bars: async () => [{ start, open: 100, high: 100, low: 100, close: 100 }],
        tracked: async (): Promise<TrackedSymbols> => ({
            catalogLoaded: true,
            results: [
                { productId: 'BTC-USD', name: 'Bitcoin', sortOrder: 0, listed: true },
                { productId: 'ETH-USD', name: 'Ether', sortOrder: 1, listed: true },
                { productId: 'GONE-USD', name: 'Gone', sortOrder: 2, listed: false },
            ],
        }),
        trades: async () => {
            if (options?.simulatedMark) {
                return [{ ...saved, tradeId: 9, price: 6, fee: 0, simulated: true, side: 'Buy' as const }]
            }
            return placed ? [saved, sold] : [saved]
        },
        book: async () => book(),
        placePaper: async () => {
            placed = true
            return book()
        },
        openSimulation: async () => ({ ...book(), mode: 'Real' as const, simulated: true, simulationId: 'sim-1' }),
        simulation: async () => ({ ...book(), simulated: true, simulationId: 'sim-1' }),
    } as unknown as LiveClient
    const wallets: WalletsResponse = {
        results: [
            {
                key: 'paper',
                id: 1,
                name: 'Paper',
                mode: options?.real ? 'real' : 'paper',
                venue: 'Paper',
                baseCurrency: 'USD',
                note: '',
                isArchived: false,
                sortOrder: 1,
                createdAt: null,
            },
        ],
    }
    const ledger = { wallets: async () => wallets } as LedgerClient
    const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/live', component: LivePage }, { path: '/signin', component: { template: '<div />' } }] })
    await router.push('/live')
    await router.isReady()
    const wrapper = mount(LivePage, {
        global: {
            plugins: [router, createPinia()],
            provide: {
                [liveClientKey as symbol]: client,
                [ledgerClientKey as symbol]: ledger,
                [liveStreamKey as symbol]: stream,
            },
            stubs: {
                LiveChart: {
                    name: 'LiveChart',
                    props: ['points', 'markers'],
                    template:
                        '<div data-testid="chart-stub" :data-values="(points || []).map((point) => point.value).join(\',\')" :data-marks="(markers || []).map((mark) => mark.side + \':\' + mark.simulated + \':\' + mark.text).join(\'|\')" />',
                },
            },
        },
    })
    await flushPromises()
    return { wrapper, listeners, link, products }
}

const productCalls = { n: 0 }
function viProducts() {
    return async () => {
        productCalls.n += 1
        return [{ productId: 'DOGE-USD', base: 'DOGE', quote: 'USD', name: 'Dogecoin', price: 1 }]
    }
}

describe('live page', () => {
    it('appends ticks on the chart and keeps the last price', async () => {
        productCalls.n = 0
        const { wrapper, listeners } = await mountLive()
        expect(wrapper.get('[data-testid=live-price]').text()).toBe('$100')
        expect(wrapper.get('[data-testid=chart-stub]').attributes('data-values')).toBe('100')
        expect(wrapper.get('[data-testid=live-link]').text()).toBe('live')
        expect(productCalls.n).toBe(0)

        await wrapper.get('[data-testid=product-picker]').trigger('click')
        expect(wrapper.text()).toContain('BTC-USD')
        expect(wrapper.text()).toContain('ETH-USD')
        expect(wrapper.text()).not.toContain('DOGE-USD')
        expect(wrapper.text()).not.toContain('GONE-USD')

        listeners[0]?.({ productId: 'BTC-USD', price: 101, at: new Date(start * 1000 + 20_000).toISOString() })
        listeners[0]?.({ productId: 'BTC-USD', price: 102, at: new Date((start + 300) * 1000).toISOString() })
        await flushPromises()
        expect(wrapper.get('[data-testid=live-price]').text()).toBe('$102')
        expect(wrapper.get('[data-testid=chart-stub]').attributes('data-values')).toBe('101,102')
    })

    it('shows reconnecting and draws paper and simulation fills', async () => {
        const { wrapper, link } = await mountLive()
        link.value = 'reconnecting'
        await flushPromises()
        expect(wrapper.get('[data-testid=live-link]').text()).toBe('reconnecting')

        await wrapper.get('[data-testid=wallet-select]').setValue('paper')
        await flushPromises()
        expect(wrapper.get('[data-testid=chart-stub]').attributes('data-marks')).toContain('buy:false:Buy 2 @ $10, fee $0.25, fill')

        await wrapper.get('[data-testid=show-trades]').setValue(false)
        await flushPromises()
        expect(wrapper.get('[data-testid=chart-stub]').attributes('data-marks')).toBe('')

        await wrapper.get('[data-testid=show-trades]').setValue(true)
        await wrapper.get('[data-testid=quick-buy]').trigger('click')
        await flushPromises()
        expect(wrapper.get('[data-testid=chart-stub]').attributes('data-marks')).toContain('sell:false:Sell 1 @ $12, fee $0, fill')
    })

    it('draws a hollow simulation fill for a real wallet', async () => {
        const { wrapper } = await mountLive({ real: true, simulatedMark: true })
        await wrapper.get('[data-testid=wallet-select]').setValue('paper')
        await flushPromises()
        expect(wrapper.get('[data-testid=chart-stub]').attributes('data-marks')).toContain('buy:true:')
        expect(wrapper.get('[data-testid=chart-stub]').attributes('data-marks')).toContain('simulation')
    })
})
