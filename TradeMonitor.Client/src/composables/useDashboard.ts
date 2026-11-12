import { computed, inject, onMounted, onUnmounted, provide, ref, watch, type ComputedRef, type InjectionKey, type Ref } from 'vue'
import type { DashboardView } from '@/lib/book'
import { loadDashboard } from '@/lib/loadDashboard'
import { REFRESH_MS, startPolling } from '@/lib/refresh'
import { useFiltersStore } from '@/stores/filters'

export interface DashboardContext {
    status: Ref<'loading' | 'ready' | 'error'>
    error: Ref<string | null>
    view: ComputedRef<DashboardView | null>
    refresh: () => Promise<void>
}

export const dashboardKey: InjectionKey<DashboardContext> = Symbol('dashboard')

function filterKey(mode: string, range: string, wallet: string | null): string {
    return `${mode}|${range}|${wallet ?? ''}`
}

export function provideDashboard(): DashboardContext {
    const filters = useFiltersStore()
    const viewState = ref<DashboardView | null>(null)
    const status = ref<'loading' | 'ready' | 'error'>('loading')
    const error = ref<string | null>(null)
    const view = computed(() => viewState.value)
    let generation = 0
    let appliedKey = ''

    async function refresh() {
        const current = filters.current()
        const key = filterKey(current.mode, current.range, current.wallet)
        if (appliedKey !== key) {
            viewState.value = null
            status.value = 'loading'
            error.value = null
        }
        const ticket = ++generation
        try {
            const next = await loadDashboard(current, new Date())
            if (ticket !== generation) return
            viewState.value = next
            appliedKey = key
            status.value = 'ready'
            error.value = null
        } catch (err) {
            if (ticket !== generation) return
            error.value = err instanceof Error ? err.message : 'Could not load the book'
            if (!viewState.value) status.value = 'error'
        }
    }

    let stop = () => {}
    watch(
        () => [filters.mode, filters.range, filters.wallet] as const,
        () => {
            void refresh()
        },
        { immediate: true },
    )
    onMounted(() => {
        stop = startPolling(() => {
            void refresh()
        }, REFRESH_MS)
    })
    onUnmounted(() => stop())

    const api: DashboardContext = { status, error, view, refresh }
    provide(dashboardKey, api)
    return api
}

export function useDashboard(): DashboardContext {
    const context = inject(dashboardKey)
    if (!context) throw new Error('Dashboard data is provided by the app shell.')
    return context
}
