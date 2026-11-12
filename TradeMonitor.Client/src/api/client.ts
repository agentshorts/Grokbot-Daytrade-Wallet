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
    get(path: string): Promise<{ status: number; body: unknown }>
}

export interface LedgerClient {
    summary(query?: LedgerQuery): Promise<SummaryResponse>
    equity(query?: LedgerQuery): Promise<EquityResponse>
    pnlDaily(query?: LedgerQuery): Promise<PnlDailyResponse>
    pnlBySymbol(query?: LedgerQuery): Promise<SymbolPnlResponse>
    fees(query?: LedgerQuery): Promise<FeesResponse>
    positions(query?: LedgerQuery): Promise<PositionsResponse>
    trades(query?: LedgerQuery): Promise<TradesResponse>
    trade(id: number): Promise<TradeDetailResponse>
    wallets(query?: LedgerQuery): Promise<WalletsResponse>
    wallet(key: string): Promise<WalletResponse>
    walletBalances(key: string): Promise<WalletBalancesResponse>
    walletHistory(key: string, query?: LedgerQuery): Promise<WalletHistoryResponse>
    priceMarks(query?: LedgerQuery): Promise<PriceMarksResponse>
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

export function fetchTransport(fetchImpl: typeof fetch = fetch): LedgerTransport {
    return {
        async get(path: string) {
            const response = await fetchImpl(path, {
                method: 'GET',
                credentials: 'same-origin',
                headers: { Accept: 'application/json' },
            })
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

function walletPath(key: string, rest = ''): string {
    return `/wallets/${encodeURIComponent(key)}${rest}`
}

async function request<T>(transport: LedgerTransport, path: string, query: LedgerQuery | undefined, parse: (body: unknown) => T): Promise<T> {
    const { status, body } = await transport.get(withQuery(path, query))
    if (status >= 400) throw readFailure(body, status)
    return parse(body)
}

// Custom read routes are at the site root. `/api/{RequestName}` is an alias.
// `/api/summary` is not a route on this host; the SPA fallback would answer it.
/** GET-only client for `/summary`, `/pnl/*`, `/positions`, `/fees`, `/trades`, and wallet reads. */
export function createLedgerClient(transport: LedgerTransport = fetchTransport()): LedgerClient {
    return {
        summary: (query) => request(transport, '/summary', query, parseSummary),
        equity: (query) => request(transport, '/equity', query, parseEquity),
        pnlDaily: (query) => request(transport, '/pnl/daily', query, parsePnlDaily),
        pnlBySymbol: (query) => request(transport, '/pnl/symbols', query, parseSymbols),
        fees: (query) => request(transport, '/fees', query, parseFees),
        positions: (query) => request(transport, '/positions', query, parsePositions),
        trades: (query) => request(transport, '/trades', query, parseTrades),
        trade: (id) => {
            if (!Number.isInteger(id) || id <= 0) return Promise.reject(new LedgerReadError(404, 'UnknownTrade', `Unknown trade '${id}'.`))
            return request(transport, `/trades/${id}`, {}, parseTradeDetail)
        },
        wallets: (query) => request(transport, '/wallets', query, parseWallets),
        wallet: (key) => request(transport, walletPath(key), {}, parseWallet),
        walletBalances: (key) => request(transport, walletPath(key, '/balances'), {}, parseWalletBalances),
        walletHistory: (key, query) => request(transport, walletPath(key, '/history'), query, parseWalletHistory),
        priceMarks: (query) => request(transport, '/price-marks', query, parsePriceMarks),
    }
}

export const ledger = createLedgerClient()
