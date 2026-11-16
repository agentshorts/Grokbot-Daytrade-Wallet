import { describe, expect, it } from 'vite-plus/test'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import type { LiveClient, TrackedSymbols } from '@/api/live'
import type { LedgerClient, WalletsResponse } from '@/api'
import { ledgerClientKey } from '@/composables/useLedger'
import { liveClientKey } from '@/composables/useLive'
import { liveStreamKey, type LiveStream } from '@/composables/useLiveStream'
import { CHART_PREFS_KEY, readChartPrefs } from '@/lib/chartPrefs'
import { ref } from 'vue'
import LivePage from './live.vue'
import SettingsPage from './settings.vue'

const start = 1_780_000_200

describe('step ladder settings', () => {
    it('saves a valid input and keeps the previous value when validation fails', async () => {
        localStorage.removeItem(CHART_PREFS_KEY)
        const client = {
            tracked: async (): Promise<TrackedSymbols> => ({ catalogLoaded: true, results: [] }),
            products: async () => [],
        } as unknown as LiveClient
        const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/settings', component: SettingsPage }] })
        await router.push('/settings')
        await router.isReady()
        const wrapper = mount(SettingsPage, {
            global: { plugins: [router, createPinia()], provide: { [liveClientKey as symbol]: client } },
        })
        await flushPromises()
        const toggle = wrapper.get('[data-testid=chart-step-ladder]')
        expect((toggle.element as HTMLInputElement).checked).toBe(false)
        expect((wrapper.get('[data-testid=step-increase]').element as HTMLInputElement).value).toBe('2')
        expect((wrapper.get('[data-testid=dormant-threshold]').element as HTMLInputElement).value).toBe('0.5')
        expect((wrapper.get('[data-testid=dormant-period]').element as HTMLInputElement).value).toBe('5')

        await toggle.trigger('change')
        await wrapper.get('[data-testid=dormant-period]').setValue('6')
        await wrapper.get('[data-testid=dormant-period]').trigger('change')
        await flushPromises()
        expect(readChartPrefs(localStorage)).toMatchObject({ showStepLadder: true, dormantPeriod: 6, stepIncrease: 2 })

        await wrapper.get('[data-testid=step-increase]').setValue('0')
        await wrapper.get('[data-testid=step-increase]').trigger('change')
        expect(wrapper.get('[data-testid=step-ladder-error]').text()).toContain('greater than 0')
        expect(readChartPrefs(localStorage).stepIncrease).toBe(2)
        wrapper.unmount()
        localStorage.removeItem(CHART_PREFS_KEY)
    })
})

describe('step ladder on the live chart', () => {
    it('toggles markers and updates them when an input changes, without a reload', async () => {
        localStorage.removeItem(CHART_PREFS_KEY)
        const calls: { stepIncrease: number; dormantThreshold: number; dormantPeriod: number; interval: string }[] = []
        const listeners: ((tick: { productId: string; price: number; at: string | null }) => void)[] = []
        const stream: LiveStream = {
            link: ref('live'),
            subscribe(_product, onTick) {
                listeners.push(onTick)
                return () => undefined
            },
        }
        const client = {
            products: async () => [],
            bars: async () => [{ start, open: 100, high: 110, low: 90, close: 100, volume: 10 }],
            tracked: async (): Promise<TrackedSymbols> => ({
                catalogLoaded: true,
                results: [{ productId: 'BTC-USD', name: 'Bitcoin', sortOrder: 0, listed: true }],
            }),
            stepLadder: async (_symbol: string, interval: string, inputs: { stepIncrease: number; dormantThreshold: number; dormantPeriod: number }) => {
                calls.push({ ...inputs, interval })
                const up = inputs.stepIncrease >= 3
                return { bars: [{ time: start, volume: up ? 40 : 1, vol24: 10, step: up, dormant: !up }] }
            },
        } as unknown as LiveClient
        const ledger = { wallets: async () => ({ results: [] }) as WalletsResponse } as LedgerClient
        const router = createRouter({
            history: createMemoryHistory(),
            routes: [
                { path: '/live', component: LivePage },
                { path: '/signin', component: { template: '<div />' } },
            ],
        })
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
                        props: ['study', 'showStudy'],
                        template: '<div data-testid="chart-stub" :data-study-on="String(showStudy)" :data-study="(study || []).map((bar) => (bar.step ? \'up\' : \'\') + (bar.dormant ? \'down\' : \'\') + \'@\' + bar.time).join(\'|\')" />',
                    },
                },
            },
        })
        await flushPromises()
        expect(wrapper.get('[data-testid=step-ladder-toggle]').attributes('aria-pressed')).toBe('false')
        expect(calls).toEqual([])
        expect(wrapper.get('[data-testid=chart-stub]').attributes('data-study-on')).toBe('false')

        await wrapper.get('[data-testid=step-ladder-toggle]').trigger('click')
        await flushPromises()
        expect(calls).toEqual([{ stepIncrease: 2, dormantThreshold: 0.5, dormantPeriod: 5, interval: '5m' }])
        expect(wrapper.get('[data-testid=chart-stub]').attributes('data-study')).toBe(`down@${start}`)
        expect(readChartPrefs(localStorage).showStepLadder).toBe(true)

        await wrapper.get('[data-testid=step-ladder-inputs]').trigger('click')
        const step = wrapper.get('[data-testid=step-increase]')
        await step.setValue('3')
        await step.trigger('change')
        await flushPromises()
        expect(calls[1]).toMatchObject({ stepIncrease: 3, dormantPeriod: 5 })
        expect(wrapper.get('[data-testid=chart-stub]').attributes('data-study')).toBe(`up@${start}`)

        await step.setValue('0')
        await step.trigger('change')
        await flushPromises()
        expect(wrapper.get('[data-testid=step-ladder-error]').text()).toContain('greater than 0')
        expect(calls).toHaveLength(2)
        expect(wrapper.get('[data-testid=chart-stub]').attributes('data-study')).toBe(`up@${start}`)

        listeners[0]?.({ productId: 'BTC-USD', price: 101, at: new Date(start * 1000 + 20_000).toISOString() })
        await flushPromises()
        expect(calls).toHaveLength(2)

        listeners[0]?.({ productId: 'BTC-USD', price: 102, at: new Date((start + 300) * 1000).toISOString() })
        await flushPromises()
        expect(calls).toHaveLength(3)
        expect(calls[2]).toMatchObject({ stepIncrease: 3, interval: '5m' })

        await wrapper.get('[data-testid=step-ladder-toggle]').trigger('click')
        await flushPromises()
        expect(wrapper.get('[data-testid=chart-stub]').attributes('data-study')).toBe('')
        expect(wrapper.get('[data-testid=chart-stub]').attributes('data-study-on')).toBe('false')
        expect(readChartPrefs(localStorage).stepIncrease).toBe(3)
        wrapper.unmount()
        localStorage.removeItem(CHART_PREFS_KEY)
    })
})
