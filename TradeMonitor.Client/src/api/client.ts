import { LedgerReadError } from '@/api/errors'
import { readFailure } from '@/api/parse'
import {
    parseEquity,
    parseFees,
    parsePnlDaily,
    parsePositions,
    parsePriceMarks,
    parseSummary,
    parseSymbols,
    parseTradeDetail,
    parseContract,
    parseOscillation,
    parsePredictionBookings,
    parsePredictionPnl,
    parseTrades,
    parseWallet,
    parseWalletBalances,
    parseWalletHistory,
    parseWallets,
} from '@/api/parse'
import type {
    EquityResponse,
    FeesResponse,
    LedgerQuery,
    PnlDailyResponse,
    PositionsResponse,
    ContractQuery,
    ContractResponse,
    OscillationQuery,
    OscillationResponse,
    PredictionBookingResponse,
    PredictionPnlResponse,
    PriceMarksResponse,
    SummaryResponse,
    SymbolPnlResponse,
    TradeDetailResponse,
    TradesResponse,
    WalletBalancesResponse,
    WalletHistoryResponse,
    WalletResponse,
    WalletsResponse,
} from '@/api/types'

export interface LedgerTransport {
    get(path: string, signal?: AbortSignal): Promise<{ status: number; body: unknown }>
}

export interface LedgerClient {
    summary(query?: LedgerQuery, signal?: AbortSignal): Promise<SummaryResponse>
    equity(query?: LedgerQuery, signal?: AbortSignal): Promise<EquityResponse>
    pnlDaily(query?: LedgerQuery, signal?: AbortSignal): Promise<PnlDailyResponse>
    pnlBySymbol(query?: LedgerQuery, signal?: AbortSignal): Promise<SymbolPnlResponse>
    fees(query?: LedgerQuery, signal?: AbortSignal): Promise<FeesResponse>
    positions(query?: LedgerQuery, signal?: AbortSignal): Promise<PositionsResponse>
    trades(query?: LedgerQuery, signal?: AbortSignal): Promise<TradesResponse>
    trade(id: number, signal?: AbortSignal): Promise<TradeDetailResponse>
    wallets(query?: LedgerQuery, signal?: AbortSignal): Promise<WalletsResponse>
    wallet(key: string, signal?: AbortSignal): Promise<WalletResponse>
    walletBalances(key: string, signal?: AbortSignal): Promise<WalletBalancesResponse>
    walletHistory(key: string, query?: LedgerQuery, signal?: AbortSignal): Promise<WalletHistoryResponse>
    priceMarks(query?: LedgerQuery, signal?: AbortSignal): Promise<PriceMarksResponse>
    predictions(query?: LedgerQuery, signal?: AbortSignal): Promise<PredictionPnlResponse>
    predictionBookings(signal?: AbortSignal): Promise<PredictionBookingResponse>
    oscillation(query?: OscillationQuery, signal?: AbortSignal): Promise<OscillationResponse>
    contract(query: ContractQuery, signal?: AbortSignal): Promise<ContractResponse>
}

export function withQuery(path: string, query: LedgerQuery = {}): string {
    const params = new URLSearchParams()
    const fields: [keyof LedgerQuery, string][] = [
        ['mode', 'mode'],
        ['wallet', 'wallet'],
        ['from', 'from'],
        ['to', 'to'],
        ['symbol', 'symbol'],
        ['status', 'status'],
        ['side', 'side'],
        ['outcome', 'outcome'],
        ['q', 'q'],
        ['orderBy', 'orderBy'],
        ['kind', 'kind'],
        ['take', 'take'],
        ['skip', 'skip'],
    ]
    for (const [field, name] of fields) {
        const value = query[field]
        if (value == null || value === '') continue
        params.set(name, String(value))
    }
    if (query.includeArchived) params.set('includeArchived', 'true')
    if (query.descending != null) params.set('descending', query.descending ? 'true' : 'false')
    const qs = params.toString()
    return qs ? `${path}?${qs}` : path
}

export function fetchTransport(fetchImpl?: typeof fetch): LedgerTransport {
    return {
        async get(path: string, signal?: AbortSignal) {
            const request = fetchImpl ?? globalThis.fetch
            const response = await request(path, {
                method: 'GET',
                credentials: 'same-origin',
                redirect: 'manual',
                headers: { Accept: 'application/json' },
                ...(signal ? { signal } : {}),
            })
            if (response.type === 'opaqueredirect' || (response.status >= 300 && response.status < 400)) {
                return {
                    status: 401,
                    body: { responseStatus: { errorCode: 'Unauthorized', message: 'Sign in as an admin to read the ledger.' } },
                }
            }
            const text = await response.text()
            if (!text) return { status: response.status, body: null }
            try {
                return { status: response.status, body: JSON.parse(text) as unknown }
            } catch {
                const status = response.status >= 400 ? response.status : 502
                return { status, body: { responseStatus: { errorCode: 'ReadFailed', message: 'The ledger response was not JSON.' } } }
            }
        },
    }
}

/** Ledger reads live under /api so the Vue app can own /, /trades, /wallets, and /wallet/:key. */
const ledgerPrefix = '/api'

function readPath(path: string): string {
    return `${ledgerPrefix}${path}`
}

function walletPath(key: string, rest = ''): string {
    return readPath(`/wallets/${encodeURIComponent(key)}${rest}`)
}

async function request<T>(transport: LedgerTransport, path: string, query: LedgerQuery | undefined, parse: (body: unknown) => T, signal?: AbortSignal): Promise<T> {
    const { status, body } = await transport.get(withQuery(path, query), signal)
    if (status >= 400) throw readFailure(body, status)
    return parse(body)
}

/** GET-only client for `/api/summary`, `/api/pnl/*`, `/api/positions`, `/api/fees`, `/api/trades`, and wallet reads. */
export function createLedgerClient(transport: LedgerTransport = fetchTransport()): LedgerClient {
    return {
        summary: (query, signal) => request(transport, readPath('/summary'), query, parseSummary, signal),
        equity: (query, signal) => request(transport, readPath('/equity'), query, parseEquity, signal),
        pnlDaily: (query, signal) => request(transport, readPath('/pnl/daily'), query, parsePnlDaily, signal),
        pnlBySymbol: (query, signal) => request(transport, readPath('/pnl/symbols'), query, parseSymbols, signal),
        fees: (query, signal) => request(transport, readPath('/fees'), query, parseFees, signal),
        positions: (query, signal) => request(transport, readPath('/positions'), query, parsePositions, signal),
        trades: (query, signal) => request(transport, readPath('/trades'), query, parseTrades, signal),
        trade: (id, signal) => {
            if (!Number.isInteger(id) || id <= 0) return Promise.reject(new LedgerReadError(404, 'UnknownTrade', `Unknown trade '${id}'.`))
            return request(transport, readPath(`/trades/${id}`), {}, parseTradeDetail, signal)
        },
        wallets: (query, signal) => request(transport, readPath('/wallets'), query, parseWallets, signal),
        wallet: (key, signal) => request(transport, walletPath(key), {}, parseWallet, signal),
        walletBalances: (key, signal) => request(transport, walletPath(key, '/balances'), {}, parseWalletBalances, signal),
        walletHistory: (key, query, signal) => request(transport, walletPath(key, '/history'), query, parseWalletHistory, signal),
        priceMarks: (query, signal) => request(transport, readPath('/price-marks'), query, parsePriceMarks, signal),
        predictions: (query, signal) => request(transport, readPath('/predictions'), query, parsePredictionPnl, signal),
        predictionBookings: (signal) => request(transport, readPath('/predictions/booking'), {}, parsePredictionBookings, signal),
        oscillation: (query = {}, signal) => {
            const params = new URLSearchParams()
            if (query.lookbackHours != null) params.set('lookbackHours', String(query.lookbackHours))
            if (query.thresholdCents != null) params.set('thresholdCents', String(query.thresholdCents))
            if (query.ticker) params.set('ticker', query.ticker)
            const qs = params.toString()
            return request(transport, readPath('/predictions/oscillation') + (qs ? `?${qs}` : ''), {}, parseOscillation, signal)
        },
        contract: (query, signal) => {
            const params = new URLSearchParams()
            params.set('ticker', query.ticker)
            if (query.lookbackHours != null) params.set('lookbackHours', String(query.lookbackHours))
            if (query.thresholdCents != null) params.set('thresholdCents', String(query.thresholdCents))
            if (query.allAvailable) params.set('allAvailable', 'true')
            return request(transport, readPath('/predictions/contract') + `?${params.toString()}`, {}, parseContract, signal)
        },
    }
}

export const ledger = createLedgerClient()
