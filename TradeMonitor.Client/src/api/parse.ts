import { LedgerReadError } from '@/api/errors'
import type {
    ChartMarkerPoint,
    EquityPoint,
    EquityResponse,
    FeesResponse,
    HistoryEntry,
    PnlDailyResponse,
    PnlDay,
    PnlTotals,
    PositionBalance,
    PositionRow,
    PositionsResponse,
    PriceMarkRow,
    PredictionPnlResponse,
    PredictionPnlRow,
    PredictionPnlTotal,
    PriceMarksResponse,
    SummaryResponse,
    SymbolPnlResponse,
    SymbolPnlRow,
    TradeDetailResponse,
    TradeFill,
    TradeKindName,
    TradeRow,
    TradeSideName,
    TradesResponse,
    WalletBalances,
    WalletBalancesResponse,
    WalletCardModel,
    WalletEquity,
    WalletHistoryResponse,
    WalletInfo,
    WalletModeName,
    WalletResponse,
    WalletsResponse,
} from '@/api/types'

export function asRecord(value: unknown): Record<string, unknown> | null {
    if (!value || typeof value !== 'object' || Array.isArray(value)) return null
    return value as Record<string, unknown>
}

export function pick(record: Record<string, unknown>, name: string): unknown {
    if (Object.prototype.hasOwnProperty.call(record, name)) return record[name]
    const pascal = name.slice(0, 1).toUpperCase() + name.slice(1)
    if (pascal !== name && Object.prototype.hasOwnProperty.call(record, pascal)) return record[pascal]
    return undefined
}

/** Null and missing stay null. Zero stays zero. Numeric strings are accepted. */
export function readMoney(value: unknown): number | null {
    if (value == null || value === '') return null
    if (typeof value === 'number') return Number.isFinite(value) ? value : null
    if (typeof value === 'string') {
        const parsed = Number(value)
        return Number.isFinite(parsed) ? parsed : null
    }
    return null
}

export function readTime(value: unknown): string | null {
    if (value == null || value === '') return null
    if (typeof value === 'number' && Number.isFinite(value)) return new Date(value).toISOString()
    if (typeof value !== 'string') return null
    const ticks = /\/Date\((-?\d+)/.exec(value)
    if (ticks?.[1]) {
        const ms = Number(ticks[1])
        return Number.isFinite(ms) ? new Date(ms).toISOString() : null
    }
    let text = value.trim().replace(/(\.\d{3})\d+/, '$1')
    if (/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?$/.test(text)) text += 'Z'
    const ms = Date.parse(text)
    if (Number.isNaN(ms)) return null
    return new Date(ms).toISOString()
}

function amount(row: Record<string, unknown>, name: string): number {
    return readMoney(pick(row, name)) ?? 0
}

function maybe(row: Record<string, unknown>, name: string): number | null {
    return readMoney(pick(row, name))
}

function count(row: Record<string, unknown>, name: string): number {
    const value = readMoney(pick(row, name))
    return value == null ? 0 : Math.trunc(value)
}

function text(row: Record<string, unknown>, name: string): string | null {
    const value = pick(row, name)
    if (typeof value !== 'string') return null
    const trimmed = value.trim()
    return trimmed || null
}

function flag(row: Record<string, unknown>, name: string): boolean {
    const value = pick(row, name)
    return value === true || value === 1 || value === 'true'
}

function enumText(value: unknown): string {
    if (typeof value === 'string') return value.toLowerCase()
    if (typeof value === 'number' && Number.isFinite(value)) return String(value)
    return ''
}

function mode(value: unknown): WalletModeName {
    const textValue = enumText(value)
    return textValue === 'real' || textValue === '1' ? 'real' : 'paper'
}

function tradeKind(value: unknown): TradeKindName {
    const textValue = enumText(value)
    if (textValue === 'settlement' || textValue === '1') return 'settlement'
    return 'fill'
}

function side(value: unknown): TradeSideName | null {
    const textValue = enumText(value)
    if (!textValue) return null
    if (textValue === 'sell' || textValue === '1') return 'sell'
    if (textValue === 'buy' || textValue === '0') return 'buy'
    return null
}

function status(value: unknown): 'open' | 'closed' {
    return enumText(value) === 'closed' ? 'closed' : 'open'
}

function list<T>(value: unknown, map: (row: Record<string, unknown>) => T | null): T[] {
    if (!Array.isArray(value)) return []
    const rows: T[] = []
    for (const item of value) {
        const record = asRecord(item)
        if (!record) continue
        const mapped = map(record)
        if (mapped) rows.push(mapped)
    }
    return rows
}

function parseTotals(value: unknown): PnlTotals {
    const row = asRecord(value) ?? {}
    return {
        wallets: count(row, 'wallets'),
        baseCurrency: text(row, 'baseCurrency'),
        mixedCurrencies: flag(row, 'mixedCurrencies'),
        totalValue: amount(row, 'totalValue'),
        valueChange: amount(row, 'valueChange'),
        cash: amount(row, 'cash'),
        swept: amount(row, 'swept'),
        openExposure: amount(row, 'openExposure'),
        unmarkedCost: amount(row, 'unmarkedCost'),
        positionsValue: amount(row, 'positionsValue'),
        valuedAtCost: flag(row, 'valuedAtCost'),
        realizedPnl: amount(row, 'realizedPnl'),
        unrealizedPnl: maybe(row, 'unrealizedPnl'),
        unrealizedChange: maybe(row, 'unrealizedChange'),
        pnl: maybe(row, 'pnl'),
        fees: amount(row, 'fees'),
        deposits: amount(row, 'deposits'),
        withdrawals: amount(row, 'withdrawals'),
        sweeps: amount(row, 'sweeps'),
        winRate: maybe(row, 'winRate'),
        wins: count(row, 'wins'),
        losses: count(row, 'losses'),
        scratches: count(row, 'scratches'),
        tradeCount: count(row, 'tradeCount'),
        openPositions: count(row, 'openPositions'),
    }
}

function parseEquityPoint(row: Record<string, unknown>): EquityPoint | null {
    const at = readTime(pick(row, 'at'))
    if (!at) return null
    return {
        at,
        totalValue: amount(row, 'totalValue'),
        cash: amount(row, 'cash'),
        positionsValue: amount(row, 'positionsValue'),
        openExposure: amount(row, 'openExposure'),
        unmarkedCost: amount(row, 'unmarkedCost'),
        swept: amount(row, 'swept'),
        unrealizedPnl: maybe(row, 'unrealizedPnl'),
        valuedAtCost: flag(row, 'valuedAtCost'),
    }
}

function parseWalletCard(row: Record<string, unknown>): WalletCardModel {
    return {
        id: count(row, 'id'),
        key: text(row, 'key') ?? '',
        name: text(row, 'name') ?? '',
        mode: mode(pick(row, 'mode')),
        baseCurrency: text(row, 'baseCurrency') ?? 'USD',
        isArchived: flag(row, 'isArchived'),
        sortOrder: count(row, 'sortOrder'),
        totalValue: amount(row, 'totalValue'),
        valueChange: amount(row, 'valueChange'),
        cash: amount(row, 'cash'),
        swept: amount(row, 'swept'),
        openExposure: amount(row, 'openExposure'),
        unmarkedCost: amount(row, 'unmarkedCost'),
        positionsValue: amount(row, 'positionsValue'),
        valuedAtCost: flag(row, 'valuedAtCost'),
        realizedPnl: amount(row, 'realizedPnl'),
        unrealizedPnl: maybe(row, 'unrealizedPnl'),
        unrealizedChange: maybe(row, 'unrealizedChange'),
        pnl: maybe(row, 'pnl'),
        fees: amount(row, 'fees'),
        deposits: amount(row, 'deposits'),
        withdrawals: amount(row, 'withdrawals'),
        sweeps: amount(row, 'sweeps'),
        winRate: maybe(row, 'winRate'),
        wins: count(row, 'wins'),
        losses: count(row, 'losses'),
        scratches: count(row, 'scratches'),
        tradeCount: count(row, 'tradeCount'),
        openPositions: count(row, 'openPositions'),
        lastActivityAt: readTime(pick(row, 'lastActivityAt')),
        sparkline: list(pick(row, 'sparkline'), parseEquityPoint),
    }
}

export function parseSummary(body: unknown): SummaryResponse {
    const row = asRecord(body) ?? {}
    return {
        totals: parseTotals(pick(row, 'totals')),
        modes: list(pick(row, 'modes'), (item) => ({
            mode: text(item, 'mode') ?? '',
            totals: parseTotals(pick(item, 'totals')),
        })),
        wallets: list(pick(row, 'wallets'), parseWalletCard),
    }
}

function parseWalletEquity(row: Record<string, unknown>): WalletEquity {
    return {
        wallet: text(row, 'wallet') ?? '',
        name: text(row, 'name') ?? '',
        mode: mode(pick(row, 'mode')),
        points: list(pick(row, 'points'), parseEquityPoint),
    }
}

export function parseEquity(body: unknown): EquityResponse {
    const row = asRecord(body) ?? {}
    return {
        wallets: list(pick(row, 'wallets'), parseWalletEquity),
        total: list(pick(row, 'total'), parseEquityPoint),
    }
}

function parseDay(row: Record<string, unknown>): PnlDay | null {
    const date = readTime(pick(row, 'date'))
    if (!date) return null
    return {
        date,
        realizedPnl: amount(row, 'realizedPnl'),
        unrealizedChange: maybe(row, 'unrealizedChange'),
        pnl: maybe(row, 'pnl'),
        fees: amount(row, 'fees'),
        deposits: amount(row, 'deposits'),
        withdrawals: amount(row, 'withdrawals'),
        sweeps: amount(row, 'sweeps'),
        totalValue: amount(row, 'totalValue'),
        valueChange: amount(row, 'valueChange'),
        cash: amount(row, 'cash'),
        positionsValue: amount(row, 'positionsValue'),
        openExposure: amount(row, 'openExposure'),
        unmarkedCost: amount(row, 'unmarkedCost'),
        valuedAtCost: flag(row, 'valuedAtCost'),
        trades: count(row, 'trades'),
    }
}

export function parsePnlDaily(body: unknown): PnlDailyResponse {
    const row = asRecord(body) ?? {}
    return {
        results: list(pick(row, 'results'), parseDay),
        wallets: list(pick(row, 'wallets'), (item) => ({
            wallet: text(item, 'wallet') ?? '',
            name: text(item, 'name') ?? '',
            mode: mode(pick(item, 'mode')),
            days: list(pick(item, 'days'), parseDay),
        })),
    }
}

function parseSymbol(row: Record<string, unknown>): SymbolPnlRow | null {
    const symbol = text(row, 'symbol')
    if (!symbol) return null
    return {
        symbol,
        realizedPnl: amount(row, 'realizedPnl'),
        unrealizedPnl: maybe(row, 'unrealizedPnl'),
        unrealizedChange: maybe(row, 'unrealizedChange'),
        pnl: maybe(row, 'pnl'),
        fees: amount(row, 'fees'),
        trades: count(row, 'trades'),
        wins: count(row, 'wins'),
        losses: count(row, 'losses'),
        qty: amount(row, 'qty'),
        marketValue: amount(row, 'marketValue'),
        valuedAtCost: flag(row, 'valuedAtCost'),
    }
}

export function parseSymbols(body: unknown): SymbolPnlResponse {
    const row = asRecord(body) ?? {}
    return { results: list(pick(row, 'results'), parseSymbol) }
}

export function parseFees(body: unknown): FeesResponse {
    const row = asRecord(body) ?? {}
    return {
        total: amount(row, 'total'),
        days: list(pick(row, 'days'), (item) => {
            const date = readTime(pick(item, 'date'))
            if (!date) return null
            return { date, fees: amount(item, 'fees') }
        }),
    }
}

function parsePosition(row: Record<string, unknown>): PositionRow | null {
    const symbol = text(row, 'symbol')
    if (!symbol) return null
    return {
        wallet: text(row, 'wallet') ?? '',
        walletName: text(row, 'walletName') ?? '',
        mode: mode(pick(row, 'mode')),
        symbol,
        status: status(pick(row, 'status')),
        qty: amount(row, 'qty'),
        avgPrice: amount(row, 'avgPrice'),
        costBasis: amount(row, 'costBasis'),
        lastMarkPrice: maybe(row, 'lastMarkPrice'),
        lastMarkAt: readTime(pick(row, 'lastMarkAt')),
        markIsStale: flag(row, 'markIsStale'),
        markStaleAfterMinutes: count(row, 'markStaleAfterMinutes'),
        unrealizedPnl: maybe(row, 'unrealizedPnl'),
        unrealizedPnlPercent: maybe(row, 'unrealizedPnlPercent'),
        realizedPnl: amount(row, 'realizedPnl'),
        feesPaid: amount(row, 'feesPaid'),
        marketValue: amount(row, 'marketValue'),
        valuedAtCost: flag(row, 'valuedAtCost'),
        targetPrice: maybe(row, 'targetPrice'),
        distanceToTarget: maybe(row, 'distanceToTarget'),
        distanceToTargetPercent: maybe(row, 'distanceToTargetPercent'),
    }
}

export function parsePositions(body: unknown): PositionsResponse {
    const row = asRecord(body) ?? {}
    return {
        markStaleAfterMinutes: count(row, 'markStaleAfterMinutes'),
        results: list(pick(row, 'results'), parsePosition),
    }
}

export function parseTradeRow(row: Record<string, unknown>): TradeRow | null {
    const id = count(row, 'id')
    const symbol = text(row, 'symbol')
    if (!id || !symbol) return null
    return {
        id,
        wallet: text(row, 'wallet') ?? '',
        walletName: text(row, 'walletName') ?? '',
        mode: mode(pick(row, 'mode')),
        symbol,
        title: text(row, 'title'),
        side: side(pick(row, 'side')) ?? 'buy',
        kind: tradeKind(pick(row, 'kind')),
        qty: amount(row, 'qty'),
        price: amount(row, 'price'),
        fee: amount(row, 'fee'),
        executedAt: readTime(pick(row, 'executedAt')) ?? '',
        recordedAt: readTime(pick(row, 'recordedAt')) ?? '',
        note: text(row, 'note'),
        status: status(pick(row, 'status')),
        buyPrice: amount(row, 'buyPrice'),
        currentOrExitPrice: maybe(row, 'currentOrExitPrice'),
        priceKind: text(row, 'priceKind') ?? '',
        exitPrice: maybe(row, 'exitPrice'),
        lastMarkPrice: maybe(row, 'lastMarkPrice'),
        lastMarkAt: readTime(pick(row, 'lastMarkAt')),
        realizedPnl: amount(row, 'realizedPnl'),
        unrealizedPnl: maybe(row, 'unrealizedPnl'),
        pnl: maybe(row, 'pnl'),
        pnlPercent: maybe(row, 'pnlPercent'),
        remainingQty: amount(row, 'remainingQty'),
        valuedAtCost: flag(row, 'valuedAtCost'),
        targetPrice: maybe(row, 'targetPrice'),
        orderRef: text(row, 'orderRef'),
        clientId: text(row, 'clientId') ?? '',
    }
}

function page<T>(body: unknown, map: (row: Record<string, unknown>) => T | null): { total: number; skip: number; take: number; results: T[] } {
    const row = asRecord(body) ?? {}
    return {
        total: count(row, 'total'),
        skip: count(row, 'skip'),
        take: count(row, 'take'),
        results: list(pick(row, 'results'), map),
    }
}

export function parseTrades(body: unknown): TradesResponse {
    return page(body, parseTradeRow)
}

function parseFill(row: Record<string, unknown>): TradeFill | null {
    const symbol = text(row, 'symbol')
    if (!symbol) return null
    return {
        tradeId: count(row, 'tradeId'),
        side: side(pick(row, 'side')) ?? 'buy',
        symbol,
        qty: amount(row, 'qty'),
        price: amount(row, 'price'),
        buyFee: amount(row, 'buyFee'),
        sellFee: amount(row, 'sellFee'),
        cost: amount(row, 'cost'),
        proceeds: amount(row, 'proceeds'),
        realizedPnl: amount(row, 'realizedPnl'),
        executedAt: readTime(pick(row, 'executedAt')) ?? '',
    }
}

function parseMarker(row: Record<string, unknown>): ChartMarkerPoint | null {
    const at = readTime(pick(row, 'at'))
    if (!at) return null
    return {
        tradeId: count(row, 'tradeId'),
        side: side(pick(row, 'side')) ?? 'buy',
        at,
        price: amount(row, 'price'),
        qty: amount(row, 'qty'),
    }
}

function parseMark(row: Record<string, unknown>): PriceMarkRow | null {
    const at = readTime(pick(row, 'at'))
    const symbol = text(row, 'symbol')
    if (!at || !symbol) return null
    return {
        id: count(row, 'id'),
        clientId: text(row, 'clientId'),
        symbol,
        price: amount(row, 'price'),
        at,
        source: text(row, 'source'),
        wallet: text(row, 'wallet'),
        recordedAt: readTime(pick(row, 'recordedAt')) ?? '',
    }
}

export function parseTradeDetail(body: unknown): TradeDetailResponse {
    const row = asRecord(body) ?? {}
    const result = asRecord(pick(row, 'result'))
    const trade = result ? parseTradeRow(result) : null
    if (!trade) throw new LedgerReadError(500, 'ReadFailed', 'The trade response had no result.')
    return {
        result: trade,
        fills: list(pick(row, 'fills'), parseFill),
        markers: list(pick(row, 'markers'), parseMarker),
        priceMarks: list(pick(row, 'priceMarks'), parseMark),
        hasPriceMarks: flag(row, 'hasPriceMarks'),
        chartFrom: readTime(pick(row, 'chartFrom')) ?? '',
        chartTo: readTime(pick(row, 'chartTo')) ?? '',
    }
}

export function parseWalletInfo(row: Record<string, unknown>): WalletInfo | null {
    const key = text(row, 'key')
    if (!key) return null
    return {
        id: count(row, 'id'),
        key,
        name: text(row, 'name') ?? key,
        mode: mode(pick(row, 'mode')),
        venue: text(row, 'venue') ?? '',
        baseCurrency: text(row, 'baseCurrency') ?? 'USD',
        note: text(row, 'note') ?? '',
        isArchived: flag(row, 'isArchived'),
        sortOrder: count(row, 'sortOrder'),
        createdAt: readTime(pick(row, 'createdAt')),
    }
}

export function parseWallets(body: unknown): WalletsResponse {
    const row = asRecord(body) ?? {}
    return { results: list(pick(row, 'results'), parseWalletInfo) }
}

export function parseWallet(body: unknown): WalletResponse {
    const row = asRecord(body) ?? {}
    const result = asRecord(pick(row, 'result'))
    const wallet = result ? parseWalletInfo(result) : null
    if (!wallet) throw new LedgerReadError(500, 'ReadFailed', 'The wallet response had no result.')
    return { result: wallet }
}

function parseBalancePosition(row: Record<string, unknown>): PositionBalance | null {
    const symbol = text(row, 'symbol')
    if (!symbol) return null
    return {
        symbol,
        qty: amount(row, 'qty'),
        costBasis: amount(row, 'costBasis'),
        avgPrice: amount(row, 'avgPrice'),
        lastMarkPrice: maybe(row, 'lastMarkPrice'),
        lastMarkAt: readTime(pick(row, 'lastMarkAt')),
        unrealizedPnl: maybe(row, 'unrealizedPnl'),
        unrealizedPnlPercent: maybe(row, 'unrealizedPnlPercent'),
        marketValue: amount(row, 'marketValue'),
        valuedAtCost: flag(row, 'valuedAtCost'),
        realizedPnl: amount(row, 'realizedPnl'),
        feesPaid: amount(row, 'feesPaid'),
    }
}

function parseBalances(row: Record<string, unknown>): WalletBalances | null {
    const key = text(row, 'key')
    if (!key) return null
    return {
        id: count(row, 'id'),
        key,
        name: text(row, 'name') ?? key,
        mode: mode(pick(row, 'mode')),
        baseCurrency: text(row, 'baseCurrency') ?? 'USD',
        isArchived: flag(row, 'isArchived'),
        cash: amount(row, 'cash'),
        swept: amount(row, 'swept'),
        fees: amount(row, 'fees'),
        realizedPnl: amount(row, 'realizedPnl'),
        unrealizedPnl: maybe(row, 'unrealizedPnl'),
        positionsValue: amount(row, 'positionsValue'),
        totalValue: amount(row, 'totalValue'),
        cashIsNegative: flag(row, 'cashIsNegative'),
        hasDeposits: flag(row, 'hasDeposits'),
        valuedAtCost: flag(row, 'valuedAtCost'),
        positions: list(pick(row, 'positions'), parseBalancePosition),
    }
}

export function parseWalletBalances(body: unknown): WalletBalancesResponse {
    const row = asRecord(body) ?? {}
    const result = asRecord(pick(row, 'result'))
    const balances = result ? parseBalances(result) : null
    if (!balances) throw new LedgerReadError(500, 'ReadFailed', 'The balance response had no result.')
    return { result: balances }
}

function parseHistory(row: Record<string, unknown>): HistoryEntry | null {
    const at = readTime(pick(row, 'at'))
    if (!at) return null
    return {
        wallet: text(row, 'wallet') ?? '',
        kind: text(row, 'kind') ?? '',
        id: count(row, 'id'),
        clientId: text(row, 'clientId') ?? '',
        at,
        symbol: text(row, 'symbol'),
        side: side(pick(row, 'side')),
        qty: maybe(row, 'qty'),
        price: maybe(row, 'price'),
        fee: maybe(row, 'fee'),
        amount: maybe(row, 'amount'),
        currency: text(row, 'currency'),
        title: text(row, 'title'),
        note: text(row, 'note'),
        orderRef: text(row, 'orderRef'),
        targetPrice: maybe(row, 'targetPrice'),
        recordedAt: readTime(pick(row, 'recordedAt')) ?? '',
    }
}

export function parseWalletHistory(body: unknown): WalletHistoryResponse {
    return page(body, parseHistory)
}

export function parsePriceMarks(body: unknown): PriceMarksResponse {
    return page(body, parseMark)
}

function parsePredictionRow(row: Record<string, unknown>): PredictionPnlRow | null {
    const wallet = text(row, 'wallet')
    if (!wallet) return null
    return {
        wallet,
        name: text(row, 'name') ?? wallet,
        symbol: text(row, 'symbol') ?? '',
        bought: amount(row, 'bought'),
        sold: amount(row, 'sold'),
        fees: amount(row, 'fees'),
        realizedPnl: amount(row, 'realizedPnl'),
        settlement: text(row, 'settlement') ?? '',
    }
}

function parsePredictionTotal(value: unknown): PredictionPnlTotal {
    const row = asRecord(value) ?? {}
    return {
        wallets: count(row, 'wallets'),
        bought: amount(row, 'bought'),
        sold: amount(row, 'sold'),
        fees: amount(row, 'fees'),
        realizedPnl: amount(row, 'realizedPnl'),
    }
}

export function parsePredictionPnl(body: unknown): PredictionPnlResponse {
    const row = asRecord(body) ?? {}
    return {
        wallets: list(pick(row, 'wallets'), parsePredictionRow),
        total: parsePredictionTotal(pick(row, 'total')),
    }
}

export function readFailure(body: unknown, status: number): LedgerReadError {
    const record = asRecord(body)
    const statusRecord = record ? asRecord(pick(record, 'responseStatus')) : null
    const code = statusRecord ? (text(statusRecord, 'errorCode') ?? '') : ''
    const message = statusRecord ? (text(statusRecord, 'message') ?? '') : ''
    if (status === 401 || status === 403) {
        return new LedgerReadError(status, code || 'Unauthorized', 'Sign in as an admin to read the ledger.')
    }
    return new LedgerReadError(status, code || 'ReadFailed', message || `The ledger read failed (${status}).`)
}
