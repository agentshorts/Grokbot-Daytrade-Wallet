import { watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { serializeFilters, type DateRangeKey, type Filters, type LedgerMode } from '@/lib/filters'
import { TRADES_PAGE_KEYS } from '@/lib/tradeQuery'
import { useFiltersStore } from '@/stores/filters'

export function useFilterQuery() {
    const route = useRoute()
    const router = useRouter()
    const filters = useFiltersStore()

    filters.hydrate(route.query)
    watch(
        () => route.query,
        (query) => filters.hydrate(query),
    )

    async function commit(partial: Partial<Filters>) {
        const next = { ...filters.current(), ...partial }
        const query = serializeFilters(route.query, next)
        await router.replace({ query })
    }

    function shellLocation(path: string, extra?: Record<string, string | null | undefined>) {
        const query: Record<string, string> = serializeFilters(route.query, filters.current())
        if (!path.startsWith('/trades')) {
            for (const key of TRADES_PAGE_KEYS) delete query[key]
        }
        if (extra) {
            for (const [key, value] of Object.entries(extra)) {
                if (value) query[key] = value
                else delete query[key]
            }
        }
        return { path, query }
    }

    return {
        filters,
        setMode: (mode: LedgerMode) => commit({ mode }),
        setRange: (range: DateRangeKey) => commit({ range }),
        setWallet: (wallet: string | null) => commit({ wallet }),
        shellLocation,
    }
}
