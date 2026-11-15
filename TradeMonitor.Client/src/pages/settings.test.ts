import { describe, expect, it } from 'vite-plus/test'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import type { LiveClient, TrackedSymbol } from '@/api/live'
import { liveClientKey } from '@/composables/useLive'
import SettingsPage from './settings.vue'

describe('settings page', () => {
    it('adds, removes, and reorders tracked symbols', async () => {
        let rows: TrackedSymbol[] = [
            { productId: 'BTC-USD', name: 'Bitcoin', sortOrder: 0, listed: true },
            { productId: 'ETH-USD', name: 'Ether', sortOrder: 1, listed: true },
        ]
        const added: string[] = []
        const removed: string[] = []
        const orders: string[][] = []
        const client = {
            tracked: async () => ({ catalogLoaded: true, results: rows }),
            products: async () => [
                { productId: 'BTC-USD', base: 'BTC', quote: 'USD', name: 'Bitcoin', price: null },
                { productId: 'ETH-USD', base: 'ETH', quote: 'USD', name: 'Ether', price: null },
                { productId: 'SOL-USD', base: 'SOL', quote: 'USD', name: 'Solana', price: null },
            ],
            addSymbol: async (product: string) => {
                added.push(product)
                rows = [...rows, { productId: product, name: product, sortOrder: rows.length, listed: true }]
                return { catalogLoaded: true, results: rows }
            },
            removeSymbol: async (product: string) => {
                removed.push(product)
                rows = rows.filter((row) => row.productId !== product)
                return { catalogLoaded: true, results: rows }
            },
            reorderSymbols: async (products: string[]) => {
                orders.push(products)
                rows = products.map((productId, sortOrder) => ({ productId, name: productId, sortOrder, listed: true }))
                return { catalogLoaded: true, results: rows }
            },
        } as unknown as LiveClient
        const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/settings', component: SettingsPage }] })
        await router.push('/settings')
        await router.isReady()
        const wrapper = mount(SettingsPage, {
            global: { plugins: [router, createPinia()], provide: { [liveClientKey as symbol]: client } },
        })
        await flushPromises()
        expect(wrapper.get('[data-testid=settings-symbols]').text()).toContain('BTC-USD')

        await wrapper.get('[data-testid=settings-search]').setValue('sol')
        await wrapper.get('[data-testid=settings-add]').trigger('click')
        await flushPromises()
        expect(added).toEqual(['SOL-USD'])
        expect(wrapper.text()).toContain('SOL-USD')

        const downs = wrapper.findAll('[data-testid=settings-down]')
        await downs[0]?.trigger('click')
        await flushPromises()
        expect(orders[0]).toEqual(['ETH-USD', 'BTC-USD', 'SOL-USD'])

        const removes = wrapper.findAll('[data-testid=settings-remove]')
        await removes[2]?.trigger('click')
        await flushPromises()
        expect(removed).toEqual(['SOL-USD'])
        expect(wrapper.text()).not.toContain('SOL-USD')
    })
})
