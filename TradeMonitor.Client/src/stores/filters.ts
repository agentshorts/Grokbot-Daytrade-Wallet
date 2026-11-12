import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { LocationQuery } from 'vue-router'
import { defaultFilters, parseFilters, type DateRangeKey, type Filters, type LedgerMode } from '@/lib/filters'

export const useFiltersStore = defineStore('filters', () => {
    const mode = ref<LedgerMode>(defaultFilters().mode)
    const range = ref<DateRangeKey>(defaultFilters().range)
    const wallet = ref<string | null>(defaultFilters().wallet)

    function hydrate(query: LocationQuery | Record<string, unknown>) {
        const parsed = parseFilters(query)
        mode.value = parsed.mode
        range.value = parsed.range
        wallet.value = parsed.wallet
    }

    function current(): Filters {
        return { mode: mode.value, range: range.value, wallet: wallet.value }
    }

    return { mode, range, wallet, hydrate, current }
})
