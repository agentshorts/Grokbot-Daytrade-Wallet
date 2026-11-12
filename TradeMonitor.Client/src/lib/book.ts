import { formatChartLabel, roundTo, startOfChicagoDay } from '@/lib/format'
import type { DateRangeKey, Filters, WalletMode } from '@/lib/filters'

export type TradeSide = 'buy' | 'sell'
export type SeriesGrain = 'day' | 'hour'

export const WALLET_COLORS = ['#465fff', '#7a5af8', '#0ba5ec', '#12b76a', '#fb6514']

export interface WalletRecord {
    id: number
    key: string
    name: string
    mode: WalletMode
    venue: string
    baseCurrency: string
    note: string
    isArchived: boolean
    sortOrder: number
}

export interface TradeRecord {
    id: number
    walletKey: string
    walletName: string
    mode: WalletMode
    symbol: string
    title: string
    side: TradeSide
    qty: number
    price: number
    fee: number
    executedAt: string
    recordedAt: string
    note: string
    pnl: number | null
    pnlPct: number | null
    status: 'open' | 'closed'
}

export interface PriceMarkRecord {
    symbol: string
    price: number
    at: string
    walletKey: string | null
}

export interface EquityPoint {
    walletKey: string
    at: string
    value: number
    grain: SeriesGrain
}

export interface PnlBarPoint {
    walletKey: string
    at: string
    pnl: number
    grain: SeriesGrain
}

export interface PositionRecord {
    walletKey: string
    symbol: string
    title: string
    qty: number
    avgPrice: number
    lastMark: number | null
    lastMarkAt: string | null
    unrealizedPnl: number | null
    unmarked: boolean
}

export interface DashboardSnapshot {
    asOf: string
    lastRecordedAt: string
    wallets: WalletRecord[]
    trades: TradeRecord[]
    marks: PriceMarkRecord[]
    equity: EquityPoint[]
    pnlBars: PnlBarPoint[]
    positions: PositionRecord[]
    cashByWallet: Record<string, number>
}

export interface Kpi {
    id: string
    label: string
    display: 'money' | 'pct' | 'count'
    value: number | null
    signed: boolean
    hint?: string
    emptyLabel?: string
}

export interface WalletSummary {
    key: string
    name: string
    mode: WalletMode
    venue: string
    isArchived: boolean
    currency: string
    value: number
    change: number
    changePct: number
    sparkline: number[]
    openPositions: number
    lastActivityAt: string | null
    unmarked: boolean
}

export interface EquitySeries {
    key: string
    name: string
    color: string
    points: { at: string; label: string; value: number }[]
}

export interface SymbolPnl {
    symbol: string
    title: string
    pnl: number
}

export interface DashboardView {
    emptyBook: boolean
    noMatches: boolean
    walletNames: Record<string, string>
    wallets: WalletSummary[]
    kpis: Kpi[]
    trades: TradeRecord[]
    positions: PositionRecord[]
    marks: PriceMarkRecord[]
    equity: EquitySeries[]
    pnlBars: { label: string; pnl: number }[]
    pnlBySymbol: SymbolPnl[]
    lastRecordedAt: string
    grain: SeriesGrain
}

export function rangeStart(range: DateRangeKey, now: Date): Date | null {
    if (range === 'all') return null
    const today = startOfChicagoDay(now)
    if (range === 'today') return today
    const days = range === '7d' ? 6 : 29
    return new Date(today.getTime() - days * 86_400_000)
}

function inRange(iso: string, start: Date | null): boolean {
    if (!start) return true
    return new Date(iso).getTime() >= start.getTime()
}

function sum(values: number[]): number {
    return roundTo(values.reduce((total, value) => total + value, 0))
}

export function applyFilters(snapshot: DashboardSnapshot, filters: Filters, now: Date): DashboardView {
    const start = rangeStart(filters.range, now)
    const grain: SeriesGrain = filters.range === 'today' ? 'hour' : 'day'
    const allowed = snapshot.wallets
        .filter((wallet) => (filters.mode === 'all' ? true : wallet.mode === filters.mode))
        .filter((wallet) => (filters.wallet ? wallet.key === filters.wallet : true))
        .sort((a, b) => a.sortOrder - b.sortOrder)
    const keys = new Set(allowed.map((wallet) => wallet.key))
    const emptyBook = snapshot.wallets.length === 0
    const noMatches = !emptyBook && allowed.length === 0

    const trades = snapshot.trades
        .filter((trade) => keys.has(trade.walletKey) && inRange(trade.executedAt, start))
        .slice()
        .sort((a, b) => new Date(b.executedAt).getTime() - new Date(a.executedAt).getTime())
    const positions = snapshot.positions.filter((position) => keys.has(position.walletKey) && position.qty > 0)
    const marks = snapshot.marks.filter((mark) => inRange(mark.at, start) && (mark.walletKey == null || keys.has(mark.walletKey)))

    const wallets = allowed.map((wallet) => {
        const series = snapshot.equity
            .filter((point) => point.walletKey === wallet.key && point.grain === grain && inRange(point.at, start))
            .slice()
            .sort((a, b) => new Date(a.at).getTime() - new Date(b.at).getTime())
        const daily = snapshot.equity
            .filter((point) => point.walletKey === wallet.key && point.grain === 'day')
            .slice()
            .sort((a, b) => new Date(a.at).getTime() - new Date(b.at).getTime())
        const latest = daily[daily.length - 1]?.value ?? series[series.length - 1]?.value ?? 0
        const first = series[0]?.value ?? latest
        const last = series[series.length - 1]?.value ?? latest
        const change = series.length >= 2 ? roundTo(last - first) : 0
        const changePct = series.length >= 2 && first !== 0 ? roundTo((change / first) * 100) : 0
        const activity = snapshot.trades
            .filter((trade) => trade.walletKey === wallet.key)
            .map((trade) => trade.recordedAt)
            .sort()
        const open = positions.filter((position) => position.walletKey === wallet.key)
        return {
            key: wallet.key,
            name: wallet.name,
            mode: wallet.mode,
            venue: wallet.venue,
            isArchived: wallet.isArchived,
            currency: wallet.baseCurrency,
            value: roundTo(latest),
            change,
            changePct,
            sparkline: series.map((point) => point.value),
            openPositions: open.length,
            lastActivityAt: activity[activity.length - 1] ?? null,
            unmarked: open.some((position) => position.unmarked),
        }
    })

    const equity: EquitySeries[] = wallets.map((wallet, index) => ({
        key: wallet.key,
        name: wallet.name,
        color: WALLET_COLORS[index % WALLET_COLORS.length] ?? WALLET_COLORS[0]!,
        points: snapshot.equity
            .filter((point) => point.walletKey === wallet.key && point.grain === grain && inRange(point.at, start))
            .slice()
            .sort((a, b) => new Date(a.at).getTime() - new Date(b.at).getTime())
            .map((point) => ({ at: point.at, label: formatChartLabel(point.at, grain), value: point.value })),
    }))

    const barMap = new Map<string, { label: string; pnl: number; at: string }>()
    for (const bar of snapshot.pnlBars) {
        if (!keys.has(bar.walletKey) || bar.grain !== grain || !inRange(bar.at, start)) continue
        const current = barMap.get(bar.at) ?? { label: formatChartLabel(bar.at, grain), pnl: 0, at: bar.at }
        current.pnl = roundTo(current.pnl + bar.pnl)
        barMap.set(bar.at, current)
    }
    const pnlBars = [...barMap.values()].sort((a, b) => new Date(a.at).getTime() - new Date(b.at).getTime()).map(({ label, pnl }) => ({ label, pnl }))

    const symbolTotals = new Map<string, SymbolPnl>()
    const addSymbol = (symbol: string, title: string, pnl: number) => {
        const row = symbolTotals.get(symbol) ?? { symbol, title, pnl: 0 }
        row.pnl = roundTo(row.pnl + pnl)
        symbolTotals.set(symbol, row)
    }
    for (const trade of trades) {
        if (trade.side === 'sell' && trade.pnl != null) addSymbol(trade.symbol, trade.title, trade.pnl)
    }
    for (const position of positions) {
        if (position.unrealizedPnl != null) addSymbol(position.symbol, position.title, position.unrealizedPnl)
    }
    const pnlBySymbol = [...symbolTotals.values()].sort((a, b) => Math.abs(b.pnl) - Math.abs(a.pnl)).slice(0, 10)

    const sells = trades.filter((trade) => trade.side === 'sell' && trade.pnl != null)
    const wins = sells.filter((trade) => (trade.pnl ?? 0) > 0)
    const marked = positions.filter((position) => !position.unmarked)
    const unmarked = positions.filter((position) => position.unmarked)
    const exposure = positions.length === 0 ? 0 : marked.length === 0 ? null : sum(marked.map((position) => position.qty * (position.lastMark ?? 0)))
    const unrealized = positions.length === 0 ? 0 : marked.length === 0 ? null : sum(marked.map((position) => position.unrealizedPnl ?? 0))
    const markHint = unmarked.length ? `${unmarked.length} without a price mark` : undefined

    const kpis: Kpi[] = [
        { id: 'value', label: 'Total value', display: 'money', value: sum(wallets.map((wallet) => wallet.value)), signed: false },
        {
            id: 'cash',
            label: 'Cash',
            display: 'money',
            value: sum(allowed.map((wallet) => snapshot.cashByWallet[wallet.key] ?? 0)),
            signed: false,
        },
        {
            id: 'exposure',
            label: 'Open exposure',
            display: 'money',
            value: exposure,
            signed: false,
            hint: markHint,
            emptyLabel: 'No price mark',
        },
        { id: 'realized', label: 'Realized P&L', display: 'money', value: sum(sells.map((trade) => trade.pnl ?? 0)), signed: true },
        {
            id: 'unrealized',
            label: 'Unrealized P&L',
            display: 'money',
            value: unrealized,
            signed: true,
            hint: markHint,
            emptyLabel: 'No price mark',
        },
        { id: 'fees', label: 'Fees paid', display: 'money', value: sum(trades.map((trade) => trade.fee)), signed: false },
        {
            id: 'winRate',
            label: 'Win rate',
            display: 'pct',
            value: sells.length ? roundTo((wins.length / sells.length) * 100) : null,
            signed: false,
            emptyLabel: 'No closed trades',
        },
        { id: 'trades', label: 'Trades', display: 'count', value: trades.length, signed: false },
    ]

    const walletNames: Record<string, string> = {}
    for (const wallet of snapshot.wallets) walletNames[wallet.key] = wallet.name

    return {
        emptyBook,
        noMatches,
        walletNames,
        wallets,
        kpis,
        trades,
        positions,
        marks,
        equity,
        pnlBars,
        pnlBySymbol,
        lastRecordedAt: snapshot.lastRecordedAt,
        grain,
    }
}
