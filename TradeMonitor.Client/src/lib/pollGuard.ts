import type { DashboardView } from '@/lib/book'

export function sameRows<T>(prev: readonly T[] | null | undefined, next: readonly T[], signature: (row: T) => string): boolean {
    if (!prev || prev.length !== next.length) return false
    for (let i = 0; i < next.length; i++) {
        if (signature(prev[i]!) !== signature(next[i]!)) return false
    }
    return true
}

function mix(values: number[]): string {
    let hash = values.length
    for (const value of values) hash = Math.imul(hash, 33) ^ Math.round(value * 100)
    return String(hash)
}

/** True when a dashboard poll would change what the page draws. */
export function dashboardChanged(prev: DashboardView | null, next: DashboardView): boolean {
    if (!prev) return true
    if (prev.emptyBook !== next.emptyBook || prev.noMatches !== next.noMatches || prev.grain !== next.grain) return true
    if (prev.lastRecordedAt !== next.lastRecordedAt) return true
    if (!sameRows(prev.wallets, next.wallets, (row) => `${row.key}:${row.value}:${row.change}:${row.openPositions}:${row.badge ?? ''}:${row.closed ? 1 : 0}`))
        return true
    if (!sameRows(prev.trades, next.trades, (row) => `${row.id}:${row.status}:${row.pnl ?? ''}`)) return true
    if (!sameRows(prev.positions, next.positions, (row) => `${row.walletKey}:${row.symbol}:${row.qty}:${row.unrealizedPnl ?? ''}`)) return true
    if (!sameRows(prev.kpis, next.kpis, (row) => `${row.id}:${row.value ?? ''}`)) return true
    if (!sameRows(prev.pnlBars, next.pnlBars, (row) => `${row.label}:${row.pnl ?? ''}`)) return true
    if (!sameRows(prev.pnlBySymbol, next.pnlBySymbol, (row) => `${row.symbol}:${row.pnl ?? ''}`)) return true
    if (!sameRows(prev.fees, next.fees, (row) => `${row.label}:${row.amount}`)) return true
    if (prev.equity.length !== next.equity.length) return true
    for (let i = 0; i < next.equity.length; i++) {
        const before = prev.equity[i]!
        const after = next.equity[i]!
        if (before.key !== after.key || mix(before.points.map((point) => point.value)) !== mix(after.points.map((point) => point.value))) return true
    }
    return false
}
