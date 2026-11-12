import type { LocationQuery } from 'vue-router'

export const TRADES_PAGE_KEYS = ['symbol', 'q', 'side', 'outcome', 'order', 'dir', 'page'] as const

export const TRADE_SORTS = ['time', 'symbol', 'wallet', 'pnl', 'fee', 'qty', 'price'] as const

export type TradeSort = (typeof TRADE_SORTS)[number]
export type TradeSideFilter = 'buy' | 'sell'
export type TradeOutcome = 'won' | 'lost' | 'open' | 'closed'

export interface TradeQueryState {
    symbol: string | null
    side: TradeSideFilter | null
    outcome: TradeOutcome | null
    q: string | null
    orderBy: TradeSort
    descending: boolean
    page: number
    tradeId: number | null
}

const MANAGED = new Set<string>([...TRADES_PAGE_KEYS, 'trade'])

export function defaultTradeQuery(): TradeQueryState {
    return {
        symbol: null,
        side: null,
        outcome: null,
        q: null,
        orderBy: 'time',
        descending: true,
        page: 1,
        tradeId: null,
    }
}

function queryText(value: unknown): string {
    if (typeof value === 'string') return value
    if (Array.isArray(value)) {
        const first = value.find((item) => typeof item === 'string')
        return typeof first === 'string' ? first : ''
    }
    return ''
}

function clean(value: string, max: number): string | null {
    const text = value.trim()
    if (!text) return null
    return text.slice(0, max)
}

export function isTradeSort(value: string): value is TradeSort {
    return (TRADE_SORTS as readonly string[]).includes(value)
}

export function parseTradeQuery(query: LocationQuery | Record<string, unknown>): TradeQueryState {
    const side = queryText(query.side).toLowerCase()
    const outcome = queryText(query.outcome).toLowerCase()
    const order = queryText(query.order).toLowerCase()
    const pageText = queryText(query.page)
    const page = /^\d+$/.test(pageText) ? Number(pageText) : 1
    const tradeText = queryText(query.trade)
    const tradeId = /^\d+$/.test(tradeText) ? Number(tradeText) : 0
    return {
        symbol: clean(queryText(query.symbol), 64),
        side: side === 'buy' || side === 'sell' ? side : null,
        outcome: outcome === 'won' || outcome === 'lost' || outcome === 'open' || outcome === 'closed' ? outcome : null,
        q: clean(queryText(query.q), 200),
        orderBy: isTradeSort(order) ? order : 'time',
        descending: queryText(query.dir).toLowerCase() !== 'asc',
        page: page >= 1 ? page : 1,
        tradeId: tradeId > 0 ? tradeId : null,
    }
}

/** Write trades filters, sort, page, and the open trade into the query. Defaults are omitted. */
export function writeTradeQuery(current: LocationQuery | Record<string, unknown>, patch: Partial<TradeQueryState>): Record<string, string> {
    const next: TradeQueryState = { ...parseTradeQuery(current), ...patch }
    const query: Record<string, string> = {}
    for (const [key, value] of Object.entries(current)) {
        if (MANAGED.has(key)) continue
        const text = queryText(value)
        if (text) query[key] = text
    }
    if (next.symbol) query.symbol = next.symbol
    if (next.side) query.side = next.side
    if (next.outcome) query.outcome = next.outcome
    if (next.q) query.q = next.q
    if (next.orderBy !== 'time') query.order = next.orderBy
    if (!next.descending) query.dir = 'asc'
    if (next.page > 1) query.page = String(next.page)
    if (next.tradeId) query.trade = String(next.tradeId)
    return query
}

export function priceKindLabel(kind: string): string {
    if (kind === 'exit') return 'Exit'
    if (kind === 'mark') return 'Mark'
    return ''
}

export function priceKindTitle(kind: string): string {
    if (kind === 'exit') return 'Exit price'
    if (kind === 'mark') return 'Last mark'
    return 'Exit or mark'
}

export function statusLabel(status: string): string {
    if (status === 'open') return 'Open'
    if (status === 'closed') return 'Closed'
    return status ? status : '-'
}

export function modeLabel(mode: string): string {
    return mode === 'real' ? 'Real' : 'Paper'
}

export function sideLabel(side: string): string {
    return side === 'sell' ? 'Sell' : 'Buy'
}

export function tradeKindLabel(kind: string): string {
    return kind === 'settlement' ? 'Settlement' : 'Fill'
}
