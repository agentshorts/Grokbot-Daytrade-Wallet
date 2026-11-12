import { computed, ref } from 'vue'
import { describe, expect, it } from 'vite-plus/test'
import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import HomePage from './index.vue'
import { applyFilters, type DashboardSnapshot, type DashboardView } from '@/lib/book'
import { buildSample } from '@/lib/sampleData'
import { dashboardKey, type DashboardContext } from '@/composables/useDashboard'

const now = new Date('2026-11-12T02:18:00.000Z')

function context(view: DashboardView | null, status: DashboardContext['status']['value'] = 'ready'): DashboardContext {
    return {
        status: ref(status),
        error: ref(status === 'error' ? 'The read failed' : null),
        view: computed(() => view),
        refresh: async () => {},
    }
}

function mountHome(view: DashboardView | null, status: DashboardContext['status']['value'] = 'ready') {
    const router = createRouter({
        history: createMemoryHistory(),
        routes: [
            { path: '/', component: HomePage },
            { path: '/trades', component: { template: '<div />' } },
            { path: '/wallets', component: { template: '<div />' } },
        ],
    })
    return mount(HomePage, {
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
}

describe('overview page', () => {
    it('renders the sample book', () => {
        const view = applyFilters(buildSample(now), { mode: 'all', range: '30d', wallet: null }, now)
        const wrapper = mountHome(view)
        expect(wrapper.get('h1').text()).toBe('Overview')
        expect(wrapper.text()).toContain('Total value')
        expect(wrapper.text()).toContain('Coinbase')
        expect(wrapper.text()).toContain('Paper Swing')
        expect(wrapper.text()).toContain('Kalshi')
    })

    it('shows the empty wallet state', () => {
        const empty: DashboardSnapshot = {
            asOf: now.toISOString(),
            lastRecordedAt: now.toISOString(),
            wallets: [],
            trades: [],
            marks: [],
            equity: [],
            pnlBars: [],
            positions: [],
            cashByWallet: {},
        }
        const view = applyFilters(empty, { mode: 'all', range: '30d', wallet: null }, now)
        const wrapper = mountHome(view)
        expect(wrapper.text()).toContain('No wallets yet')
    })
})
