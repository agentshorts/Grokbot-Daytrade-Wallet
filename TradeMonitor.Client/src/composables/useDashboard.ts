import { computed, inject, onMounted, onUnmounted, provide, ref, type ComputedRef, type InjectionKey, type Ref } from 'vue'
import { applyFilters, type DashboardSnapshot, type DashboardView } from '@/lib/book'
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

export function provideDashboard(): DashboardContext {
    const filters = useFiltersStore()
    const snapshot = ref<DashboardSnapshot | null>(null)
    const status = ref<'loading' | 'ready' | 'error'>('loading')
    const error = ref<string | null>(null)

    const view = computed(() => {
        if (!snapshot.value) return null
        return applyFilters(snapshot.value, { mode: filters.mode, range: filters.range, wallet: filters.wallet }, new Date())
    })

    async function refresh() {
        try {
            snapshot.value = await loadDashboard(new Date())
            status.value = 'ready'
            error.value = null
        } catch (err) {
            error.value = err instanceof Error ? err.message : 'Could not load the book'
            if (!snapshot.value) status.value = 'error'
        }
    }

    let stop = () => {}
    onMounted(() => {
        void refresh()
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
