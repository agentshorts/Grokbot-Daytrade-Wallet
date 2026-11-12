import type { LocationQuery } from 'vue-router'

export type WalletMode = 'real' | 'paper'
export type LedgerMode = 'all' | WalletMode
export type DateRangeKey = 'today' | '7d' | '30d' | 'all'

export interface Filters {
    mode: LedgerMode
    range: DateRangeKey
    wallet: string | null
}

export const DEFAULT_RANGE: DateRangeKey = '30d'

const MANAGED = new Set(['mode', 'range', 'wallet'])

export function defaultFilters(): Filters {
    return { mode: 'all', range: DEFAULT_RANGE, wallet: null }
}

function firstQueryValue(value: unknown): string {
    if (typeof value === 'string') return value
    if (Array.isArray(value) && typeof value[0] === 'string') return value[0]
    return ''
}

export function parseFilters(query: LocationQuery | Record<string, unknown>): Filters {
    const mode = firstQueryValue(query.mode)
    const range = firstQueryValue(query.range)
    const wallet = firstQueryValue(query.wallet)
    return {
        mode: mode === 'real' || mode === 'paper' ? mode : 'all',
        range: range === 'today' || range === '7d' || range === '30d' || range === 'all' ? range : DEFAULT_RANGE,
        wallet: wallet || null,
    }
}

/** Write mode, range and wallet into a query, dropping defaults so a reload still resolves them. */
export function serializeFilters(current: LocationQuery | Record<string, unknown>, next: Filters): Record<string, string> {
    const query: Record<string, string> = {}
    for (const [key, value] of Object.entries(current)) {
        if (MANAGED.has(key)) continue
        const text = firstQueryValue(value)
        if (text) query[key] = text
    }
    if (next.mode !== 'all') query.mode = next.mode
    if (next.range !== DEFAULT_RANGE) query.range = next.range
    if (next.wallet) query.wallet = next.wallet
    return query
}
