import { describe, expect, it } from 'vite-plus/test'
import { defineComponent } from 'vue'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { parseFilters, serializeFilters } from './filters'
import { useFilterQuery } from '@/composables/useFilterQuery'

describe('parseFilters', () => {
    it('reads mode, range and wallet, and falls back when a value is unknown', () => {
        expect(parseFilters({ mode: 'paper', range: '7d', wallet: 'kalshi' })).toEqual({
            mode: 'paper',
            range: '7d',
            wallet: 'kalshi',
        })
        expect(parseFilters({ mode: 'nope', range: 'nope' })).toEqual({
            mode: 'all',
            range: '30d',
            wallet: null,
        })
        expect(parseFilters({ mode: 'all', range: 'all' })).toEqual({
            mode: 'all',
            range: 'all',
            wallet: null,
        })
    })
})

describe('serializeFilters', () => {
    it('keeps other query keys and omits default mode and range', () => {
        expect(serializeFilters({ symbol: 'BTC-USD', mode: 'paper' }, { mode: 'all', range: '30d', wallet: null })).toEqual({
            symbol: 'BTC-USD',
        })
        expect(serializeFilters({}, { mode: 'real', range: 'today', wallet: 'coinbase' })).toEqual({
            mode: 'real',
            range: 'today',
            wallet: 'coinbase',
        })
        expect(serializeFilters({}, { mode: 'all', range: 'all', wallet: null }).range).toBe('all')
    })
})

describe('useFilterQuery', () => {
    const Harness = defineComponent({
        setup() {
            const api = useFilterQuery()
            return { api }
        },
        template: '<div />',
    })

    async function mountHarness(path = '/') {
        const router = createRouter({
            history: createMemoryHistory(),
            routes: [
                { path: '/', component: { template: '<div />' } },
                { path: '/trades', component: { template: '<div />' } },
            ],
        })
        await router.push(path)
        await router.isReady()
        const wrapper = mount(Harness, { global: { plugins: [router, createPinia()] } })
        return { router, wrapper }
    }

    it('writes the mode and range into the URL and restores them from a reload', async () => {
        const { router, wrapper } = await mountHarness('/')
        await wrapper.vm.api.setMode('paper')
        await flushPromises()
        expect(router.currentRoute.value.query.mode).toBe('paper')
        expect(wrapper.vm.api.filters.mode).toBe('paper')

        await wrapper.vm.api.setRange('7d')
        await flushPromises()
        expect(router.currentRoute.value.query.range).toBe('7d')

        await router.push('/trades?mode=real&range=today&wallet=kalshi')
        await flushPromises()
        expect(wrapper.vm.api.filters.mode).toBe('real')
        expect(wrapper.vm.api.filters.range).toBe('today')
        expect(wrapper.vm.api.filters.wallet).toBe('kalshi')

        await wrapper.vm.api.setRange('30d')
        await flushPromises()
        expect(router.currentRoute.value.query.range).toBeUndefined()
        expect(wrapper.vm.api.filters.range).toBe('30d')
    })

    it('keeps the open trade across pages and drops table filters off the trades page', async () => {
        const { wrapper } = await mountHarness('/trades?mode=paper&side=buy&page=2&trade=4&symbol=BTC-USD&order=pnl')
        expect(wrapper.vm.api.shellLocation('/').query).toEqual({ mode: 'paper', trade: '4' })
        const stay = wrapper.vm.api.shellLocation('/trades')
        expect(stay.query.side).toBe('buy')
        expect(stay.query.page).toBe('2')
        expect(stay.query.trade).toBe('4')
        expect(stay.query.symbol).toBe('BTC-USD')
        expect(stay.query.order).toBe('pnl')
    })
})
