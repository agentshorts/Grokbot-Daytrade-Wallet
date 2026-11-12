import { changePercent, rangeBounds } from '@/api'
import type { EquityPoint, HistoryEntry, LedgerQuery, PnlDay, PositionRow, TradeRow, WalletBalances, WalletCardModel, WalletInfo } from '@/api/types'
import type { TradeRecord, WalletSummary } from '@/lib/book'
import { formatChartLabel, UNKNOWN_AMOUNT } from '@/lib/format'
import type { DateRangeKey, Filters, LedgerMode } from '@/lib/filters'

/** Shown wherever a P&L figure is unknown. A missing mark is not zero. */
export const DASH = UNKNOWN_AMOUNT

export const DEFAULT_STALE_MINUTES = 15

export interface WalletListModel {
    emptyBook: boolean
    noMatches: boolean
    wallets: WalletSummary[]
}

export interface WalletPositionView {
    symbol: string
    qty: number
    avgPrice: number
    lastMark: number | null
    lastMarkAt: string | null
    stale: boolean
    unrealizedPnl: number | null
    unrealizedPnlPercent: number | null
    costBasis: number
    realizedPnl: number
    feesPaid: number
    targetPrice: number | null
    distanceToTarget: number | null
    distanceToTargetPercent: number | null
}

export interface TransferView {
    id: string
    kind: 'deposit' | 'withdrawal' | 'sweep'
    amount: number
    at: string
    note: string
    currency: string
}

export interface ChartPoint {
    label: string
    equity: number
    cash: number
}

export interface DailyBar {
    label: string
    pnl: number | null
}

export interface FeeDayView {
    label: string
    fees: number
}

export interface WalletDetailModel {
    key: string
    name: string
    mode: 'real' | 'paper'
    venue: string
    currency: string
    isArchived: boolean
    totalValue: number
    cash: number
    swept: number
    fees: number
    change: number
    changePct: number | null
    realizedPnl: number
    unrealizedPnl: number | null
    startingBalanceMissing: boolean
    equity: ChartPoint[]
    daily: DailyBar[]
    dailyHasGap: boolean
    feeTotal: number
    feeDays: FeeDayView[]
    openPositions: WalletPositionView[]
    closedPositions: WalletPositionView[]
    transfers: TransferView[]
    trades: TradeRecord[]
    lastActivityAt: string | null
    staleAfterMinutes: number
}

/**
 * Target minus the latest mark, in quote currency per unit.
 * Null when either price is missing or the mark is zero. Matches the ledger engine.
 */
export function distanceToTarget(target: number | null | undefined, mark: number | null | undefined): { amount: number; percent: number } | null {
    if (target == null || mark == null || mark === 0) return null
    const amount = target - mark
    return { amount, percent: (amount / mark) * 100 }
}

/** A mark older than `minutes` is stale. Exactly `minutes` is still fresh, matching the engine. */
export function markIsStale(at: string | null | undefined, asOf: Date, minutes = DEFAULT_STALE_MINUTES): boolean {
    if (!at) return false
    const marked = new Date(at).getTime()
    if (Number.isNaN(marked)) return false
    return asOf.getTime() - marked > minutes * 60_000
}

export function summaryQuery(filters: Filters, now: Date): LedgerQuery {
    const bounds = rangeBounds(filters.range, now)
    return {
        wallet: filters.wallet,
        mode: filters.mode === 'all' ? null : filters.mode,
        from: bounds.from ?? null,
        to: bounds.to ?? null,
        includeArchived: true,
    }
}

function byKey(cards: WalletCardModel[]): Map<string, WalletCardModel> {
    const map = new Map<string, WalletCardModel>()
    for (const card of cards) map.set(card.key, card)
    return map
}

function sortWallets(rows: WalletInfo[]): WalletInfo[] {
    return rows.slice().sort((a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name) || a.key.localeCompare(b.key))
}

function allowed(rows: WalletInfo[], mode: LedgerMode, wallet: string | null): WalletInfo[] {
    return sortWallets(rows).filter((row) => {
        if (mode !== 'all' && row.mode !== mode) return false
        if (wallet && row.key !== wallet) return false
        return true
    })
}

export function buildWalletList(directory: WalletInfo[], cards: WalletCardModel[], filters: Filters): WalletListModel {
    const emptyBook = directory.length === 0
    const shown = allowed(directory, filters.mode, filters.wallet)
    const cardsByKey = byKey(cards)
    const wallets: WalletSummary[] = shown.map((wallet) => {
        const card = cardsByKey.get(wallet.key)
        const value = card?.totalValue ?? 0
        const change = card?.valueChange ?? 0
        return {
            key: wallet.key,
            name: wallet.name || card?.name || wallet.key,
            mode: wallet.mode,
            venue: wallet.venue,
            isArchived: wallet.isArchived,
            currency: wallet.baseCurrency || card?.baseCurrency || 'USD',
            value,
            change,
            changePct: changePercent(value, change),
            sparkline: (card?.sparkline ?? []).map((point) => point.totalValue),
            openPositions: card?.openPositions ?? 0,
            lastActivityAt: card?.lastActivityAt ?? null,
            unmarked: (card?.unmarkedCost ?? 0) > 0 || card?.valuedAtCost === true,
        }
    })
    return { emptyBook, noMatches: !emptyBook && wallets.length === 0, wallets }
}

function grain(range: DateRangeKey): 'day' | 'hour' {
    return range === 'today' ? 'hour' : 'day'
}

function positionView(row: PositionRow, asOf: Date, staleAfterMinutes: number): WalletPositionView {
    return {
        symbol: row.symbol,
        qty: row.qty,
        avgPrice: row.avgPrice,
        lastMark: row.lastMarkPrice,
        lastMarkAt: row.lastMarkAt,
        stale: row.markIsStale || markIsStale(row.lastMarkAt, asOf, staleAfterMinutes),
        unrealizedPnl: row.status === 'open' ? row.unrealizedPnl : null,
        unrealizedPnlPercent: row.status === 'open' ? row.unrealizedPnlPercent : null,
        costBasis: row.costBasis,
        realizedPnl: row.realizedPnl,
        feesPaid: row.feesPaid,
        targetPrice: row.targetPrice,
        distanceToTarget: row.distanceToTarget,
        distanceToTargetPercent: row.distanceToTargetPercent,
    }
}

function transferKind(kind: string): 'deposit' | 'withdrawal' | 'sweep' | null {
    const text = kind.toLowerCase()
    if (text === 'deposit' || text === 'withdrawal' || text === 'sweep') return text
    return null
}

function toTrade(row: TradeRow): TradeRecord {
    return {
        id: row.id,
        walletKey: row.wallet,
        walletName: row.walletName,
        mode: row.mode,
        symbol: row.symbol,
        title: row.title ?? '',
        side: row.side,
        qty: row.qty,
        price: row.price,
        fee: row.fee,
        executedAt: row.executedAt,
        recordedAt: row.recordedAt,
        note: row.note ?? '',
        pnl: row.pnl,
        pnlPct: row.pnlPercent,
        status: row.status,
    }
}

export function buildWalletDetail(input: {
    wallet: WalletInfo
    card: WalletCardModel | null
    equity: EquityPoint[]
    days: PnlDay[]
    feeTotal: number
    feeDays: { date: string; fees: number }[]
    positions: PositionRow[]
    staleAfterMinutes: number
    trades: TradeRow[]
    history: HistoryEntry[]
    balances: Pick<WalletBalances, 'cashIsNegative' | 'hasDeposits'>
    range: DateRangeKey
    now: Date
}): WalletDetailModel {
    const card = input.card
    const totalValue = card?.totalValue ?? 0
    const change = card?.valueChange ?? 0
    const seriesGrain = grain(input.range)
    const equity = input.equity.map((point) => ({
        label: formatChartLabel(point.at, seriesGrain),
        equity: point.totalValue,
        cash: point.cash,
    }))
    const daily = input.days.map((day) => ({
        label: formatChartLabel(day.date, 'day'),
        pnl: day.pnl,
    }))
    const openPositions = input.positions.filter((row) => row.status === 'open').map((row) => positionView(row, input.now, input.staleAfterMinutes))
    const closedPositions = input.positions.filter((row) => row.status === 'closed').map((row) => positionView(row, input.now, input.staleAfterMinutes))
    const transfers = input.history
        .flatMap((row) => {
            const kind = transferKind(row.kind)
            if (!kind || row.amount == null) return []
            return [
                {
                    id: `${kind}-${row.id}`,
                    kind,
                    amount: row.amount,
                    at: row.at,
                    note: row.note ?? '',
                    currency: row.currency || input.wallet.baseCurrency || 'USD',
                },
            ]
        })
        .sort((a, b) => new Date(b.at).getTime() - new Date(a.at).getTime())
    return {
        key: input.wallet.key,
        name: input.wallet.name || card?.name || input.wallet.key,
        mode: input.wallet.mode,
        venue: input.wallet.venue,
        currency: input.wallet.baseCurrency || card?.baseCurrency || 'USD',
        isArchived: input.wallet.isArchived,
        totalValue,
        cash: card?.cash ?? 0,
        swept: card?.swept ?? 0,
        fees: card?.fees ?? 0,
        change,
        changePct: changePercent(totalValue, change),
        realizedPnl: card?.realizedPnl ?? 0,
        unrealizedPnl: card?.unrealizedPnl ?? null,
        startingBalanceMissing: input.balances.cashIsNegative && !input.balances.hasDeposits,
        equity,
        daily,
        dailyHasGap: daily.some((day) => day.pnl == null),
        feeTotal: input.feeTotal,
        feeDays: input.feeDays.filter((day) => day.fees !== 0).map((day) => ({ label: formatChartLabel(day.date, 'day'), fees: day.fees })),
        openPositions,
        closedPositions,
        transfers,
        trades: input.trades.map(toTrade),
        lastActivityAt: card?.lastActivityAt ?? null,
        staleAfterMinutes: input.staleAfterMinutes,
    }
}
