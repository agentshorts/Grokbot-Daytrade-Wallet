export interface SymbolHolding {
    symbol: string
    qty: number
    cost: number
    open?: boolean
}

export interface LiveSelection {
    walletKey: string
    symbol: string
    unknownWallet: string | null
}

export function queryValue(value: unknown): string {
    if (typeof value === 'string') return value.trim()
    if (Array.isArray(value)) return queryValue(value[0])
    return ''
}

/** One open symbol, or the holding with a strictly larger cost. A tie leaves the chart on its default. */
export function primarySymbol(rows: SymbolHolding[]): string | null {
    const totals = new Map<string, number>()
    for (const row of rows) {
        const symbol = row.symbol.trim()
        if (!symbol || row.open === false || row.qty <= 0) continue
        totals.set(symbol, (totals.get(symbol) ?? 0) + Math.max(row.cost, 0))
    }
    const ranked = [...totals.entries()].sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]))
    const first = ranked[0]
    if (!first) return null
    const second = ranked[1]
    if (!second || first[1] > second[1]) return first[0]
    return null
}

export function liveHref(walletKey: string, symbol?: string | null): string {
    const params = new URLSearchParams()
    params.set('wallet', walletKey)
    const product = symbol?.trim()
    if (product) params.set('symbol', product)
    return `/live?${params.toString()}`
}

export function readLiveSelection(
    query: Record<string, unknown>,
    wallets: { key: string }[],
    products: { productId: string }[],
    fallbackSymbol: string,
): LiveSelection {
    const requested = queryValue(query.symbol)
    const match = products.find((item) => item.productId.toLowerCase() === requested.toLowerCase())
    const symbol = match?.productId ?? fallbackSymbol
    const wallet = queryValue(query.wallet)
    if (!wallet) return { walletKey: '', symbol, unknownWallet: null }
    if (wallets.some((item) => item.key === wallet)) return { walletKey: wallet, symbol, unknownWallet: null }
    return { walletKey: '', symbol, unknownWallet: wallet }
}

export function liveQuery(walletKey: string, symbol: string, unknownWallet: string | null): Record<string, string> {
    const query: Record<string, string> = {}
    const wallet = walletKey || unknownWallet || ''
    if (wallet) query.wallet = wallet
    if (symbol) query.symbol = symbol
    return query
}
