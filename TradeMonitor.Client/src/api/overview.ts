import type {
    EquityPoint,
    EquityResponse,
    FeesResponse,
    PnlDailyResponse,
    PnlTotals,
    PositionRow,
    PositionsResponse,
    PriceMarksResponse,
    SummaryResponse,
    SymbolPnlResponse,
    SymbolPnlRow,
    TradeRow,
    TradesResponse,
    WalletCardModel,
    WalletInfo,
} from '@/api/types'
import {
    WALLET_COLORS,
    type DashboardView,
    type EquitySeries,
    type Kpi,
    type PositionRecord,
    type SeriesGrain,
    type TradeRecord,
    type WalletSummary,
} from '@/lib/book'
import { UNKNOWN_AMOUNT, formatChartLabel } from '@/lib/format'
import type { DateRangeKey } from '@/lib/filters'
import { clipContractChart, presentWalletCard } from '@/lib/predictionContract'

const SYMBOL_LIMIT = 10

export interface OverviewInput {
    catalog: WalletInfo[]
    summary: SummaryResponse
    equity: EquityResponse
    daily: PnlDailyResponse
    symbols: SymbolPnlResponse
    fees: FeesResponse
    positions: PositionsResponse
    trades: TradesResponse
    marks: PriceMarksResponse
}

export function changePercent(value: number, change: number): number | null {
    const start = value - change
    if (start === 0) return null
    return (change / start) * 100
}

function grainFor(range: DateRangeKey): SeriesGrain {
    return range === 'today' ? 'hour' : 'day'
}

function utcDayLabel(iso: string): string {
    const date = new Date(iso)
    if (Number.isNaN(date.getTime())) return ''
    const parts = new Intl.DateTimeFormat('en-US', { timeZone: 'UTC', month: 'short', day: 'numeric' }).formatToParts(date)
    const get = (type: Intl.DateTimeFormatPartTypes) => parts.find((part) => part.type === type)?.value ?? ''
    return `${get('month')} ${get('day')}`
}

function carried(points: { at: string; value: number }[], at: string): number {
    let value = 0
    for (const point of points) {
        if (point.at > at) break
        value = point.value
    }
    return value
}

function equitySeries(rows: EquityResponse['wallets'], grain: SeriesGrain): EquitySeries[] {
    const prepared = rows
        .map((row) => ({
            key: row.wallet,
            name: row.name || row.wallet,
            points: row.points.map((point) => ({ at: point.at, value: point.totalValue })).sort((a, b) => a.at.localeCompare(b.at)),
        }))
        .filter((row) => row.points.length > 0)
    const times = [...new Set(prepared.flatMap((row) => row.points.map((point) => point.at)))].sort()
    return prepared.map((row, index) => ({
        key: row.key,
        name: row.name,
        color: WALLET_COLORS[index % WALLET_COLORS.length] ?? WALLET_COLORS[0]!,
        points: times.map((at) => ({
            at,
            label: formatChartLabel(at, grain),
            value: carried(row.points, at),
        })),
    }))
}

function rankSymbol(row: SymbolPnlRow): number {
    return row.pnl == null ? Number.NEGATIVE_INFINITY : Math.abs(row.pnl)
}

function toTrade(row: TradeRow): TradeRecord {
    return {
        id: row.id,
        walletKey: row.wallet,
        walletName: row.walletName,
        mode: row.mode,
        symbol: row.symbol,
        title: row.title || row.symbol,
        side: row.side,
        qty: row.qty,
        price: row.price,
        fee: row.fee,
        executedAt: row.executedAt,
        recordedAt: row.recordedAt || row.executedAt,
        note: row.note ?? '',
        pnl: row.pnl,
        pnlPct: row.pnlPercent,
        status: row.status,
    }
}

function toPosition(row: PositionRow): PositionRecord {
    const unmarked = row.lastMarkPrice == null || row.valuedAtCost
    return {
        walletKey: row.wallet,
        symbol: row.symbol,
        title: row.symbol,
        qty: row.qty,
        avgPrice: row.avgPrice,
        lastMark: row.lastMarkPrice,
        lastMarkAt: row.lastMarkAt,
        unrealizedPnl: unmarked ? null : row.unrealizedPnl,
        unmarked,
    }
}

function walletSummary(card: WalletCardModel, venue: string, symbol: string | null, now: Date): WalletSummary {
    const presented = presentWalletCard(card, { symbol, now })
    const value = presented.value
    const change = presented.change
    return {
        key: card.key,
        name: presented.name,
        mode: card.mode,
        venue,
        isArchived: card.isArchived,
        currency: card.baseCurrency || 'USD',
        value,
        change,
        changePct: presented.closed ? null : changePercent(value, change),
        sparkline: presented.sparkline,
        openPositions: card.openPositions,
        lastActivityAt: card.lastActivityAt,
        unmarked: presented.closed ? false : card.valuedAtCost || card.unmarkedCost > 0,
        prediction: presented.prediction,
        closed: presented.closed,
        badge: presented.badge,
        ticker: presented.ticker,
        asset: presented.asset,
        closeAt: presented.closeAt,
    }
}

function clipOverviewEquity(row: EquityResponse['wallets'][number], wallet: WalletSummary | undefined): EquityResponse['wallets'][number] {
    if (!wallet?.prediction) return { ...row, name: wallet?.name || row.name }
    const line = clipContractChart(
        row.points.map((point) => ({ at: point.at, value: point.totalValue, cash: point.cash })),
        {
            closeAt: wallet.closeAt ?? null,
            ledgerClosed: wallet.badge === 'Settled' || wallet.badge === 'Closed',
            marketClosed: !!wallet.closed,
            realized: wallet.value,
            pinRealized: false,
        },
    )
    const byAt = new Map(row.points.map((point) => [point.at, point]))
    return {
        ...row,
        name: wallet.name || row.name,
        points: line.map((point) => {
            const source = byAt.get(point.at)
            const blank: EquityPoint = {
                at: point.at,
                totalValue: point.value,
                cash: point.cash ?? 0,
                positionsValue: 0,
                openExposure: 0,
                unmarkedCost: 0,
                swept: 0,
                unrealizedPnl: null,
                valuedAtCost: false,
            }
            return { ...(source ?? blank), at: point.at, totalValue: point.value, cash: point.cash ?? source?.cash ?? 0 }
        }),
    }
}

function kpis(totals: PnlTotals, unmarkedCount: number): Kpi[] {
    const exposureUnknown = totals.openPositions > 0 && totals.openExposure === 0 && (totals.unmarkedCost > 0 || unmarkedCount > 0)
    const markHint = unmarkedCount > 0 ? `${unmarkedCount} without a price mark` : undefined
    const unrealizedUnknown = totals.unrealizedPnl == null && (totals.openPositions > 0 || unmarkedCount > 0)
    return [
        {
            id: 'value',
            label: 'Total value',
            display: 'money',
            value: totals.totalValue,
            signed: false,
            hint: totals.mixedCurrencies ? 'Mixed currencies' : undefined,
        },
        { id: 'cash', label: 'Cash', display: 'money', value: totals.cash, signed: false },
        {
            id: 'exposure',
            label: 'Open exposure',
            display: 'money',
            value: exposureUnknown ? null : totals.openExposure,
            signed: false,
            hint: markHint,
            emptyLabel: 'No price mark',
        },
        { id: 'realized', label: 'Realized P&L', display: 'money', value: totals.realizedPnl, signed: true },
        {
            id: 'unrealized',
            label: 'Unrealized P&L',
            display: 'money',
            value: totals.unrealizedPnl,
            signed: true,
            hint: unrealizedUnknown ? 'No price mark' : undefined,
            emptyLabel: UNKNOWN_AMOUNT,
        },
        { id: 'fees', label: 'Fees paid', display: 'money', value: totals.fees, signed: false },
        {
            id: 'winRate',
            label: 'Win rate',
            display: 'pct',
            value: totals.winRate,
            signed: false,
            emptyLabel: 'No closed trades',
        },
        { id: 'trades', label: 'Trades', display: 'count', value: totals.tradeCount, signed: false },
    ]
}

function blank(emptyBook: boolean, now: Date, range: DateRangeKey, walletNames: Record<string, string>): DashboardView {
    return {
        emptyBook,
        noMatches: !emptyBook,
        walletNames,
        wallets: [],
        kpis: [],
        trades: [],
        positions: [],
        marks: [],
        equity: [],
        pnlBars: [],
        pnlBySymbol: [],
        fees: [],
        lastRecordedAt: now.toISOString(),
        grain: grainFor(range),
    }
}

export function toDashboardView(input: OverviewInput, options: { range: DateRangeKey; now: Date }): DashboardView {
    const cards = input.summary.wallets
    const venues = new Map(input.catalog.map((wallet) => [wallet.key, wallet.venue]))
    const symbols = new Map(input.positions.results.map((row) => [row.wallet, row.symbol]))
    const walletNames: Record<string, string> = {}
    for (const wallet of input.catalog) walletNames[wallet.key] = wallet.name
    const emptyBook = input.catalog.length === 0 && cards.length === 0
    if (emptyBook || cards.length === 0) return blank(emptyBook, options.now, options.range, walletNames)

    const presented = cards.map((card) => walletSummary(card, venues.get(card.key) ?? '', symbols.get(card.key) ?? null, options.now))
    for (const wallet of presented) walletNames[wallet.key] = wallet.name
    const keys = new Set(presented.map((card) => card.key))
    const byKey = new Map(presented.map((wallet) => [wallet.key, wallet]))
    const grain = grainFor(options.range)
    const positions = input.positions.results.filter((row) => row.status === 'open' && row.qty > 0 && keys.has(row.wallet)).map(toPosition)
    const unmarkedCount = positions.filter((position) => position.unmarked).length
    const trades = input.trades.results
        .filter((row) => keys.has(row.wallet))
        .map(toTrade)
        .sort((a, b) => new Date(b.executedAt).getTime() - new Date(a.executedAt).getTime())
    const times = [...cards.map((card) => card.lastActivityAt), ...trades.map((trade) => trade.recordedAt)].filter((value): value is string => !!value)
    times.sort()

    return {
        emptyBook: false,
        noMatches: false,
        walletNames,
        wallets: presented,
        kpis: kpis(input.summary.totals, unmarkedCount),
        trades,
        positions,
        marks: input.marks.results
            .filter((mark) => mark.wallet == null || keys.has(mark.wallet))
            .map((mark) => ({
                symbol: mark.symbol,
                price: mark.price,
                at: mark.at,
                walletKey: mark.wallet,
            })),
        equity: equitySeries(
            input.equity.wallets.filter((row) => keys.has(row.wallet)).map((row) => clipOverviewEquity(row, byKey.get(row.wallet))),
            grain,
        ),
        pnlBars: input.daily.results.map((day) => ({ label: utcDayLabel(day.date), pnl: day.pnl })),
        pnlBySymbol: input.symbols.results
            .slice()
            .sort((a, b) => {
                const gap = rankSymbol(b) - rankSymbol(a)
                if (gap !== 0) return gap
                return a.symbol.localeCompare(b.symbol)
            })
            .slice(0, SYMBOL_LIMIT)
            .map((row) => ({ symbol: row.symbol, title: row.symbol, pnl: row.pnl })),
        fees: input.fees.days.map((day) => ({ label: utcDayLabel(day.date), amount: day.fees })),
        lastRecordedAt: times[times.length - 1] ?? options.now.toISOString(),
        grain,
    }
}
