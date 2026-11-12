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

export function fetchTransport(fetchImpl?: typeof fetch): LedgerTransport {
    return {
        async get(path: string) {
            const request = fetchImpl ?? globalThis.fetch
            const response = await request(path, {
                method: 'GET',
                credentials: 'same-origin',
                redirect: 'manual',
                headers: { Accept: 'application/json' },
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

/** Empty while custom read routes stay at the site root. Set to `/api` when the host moves them. */
const ledgerPrefix = ''

function readPath(path: string): string {
    return `${ledgerPrefix}${path}`
}

function walletPath(key: string, rest = ''): string {
    return readPath(`/wallets/${encodeURIComponent(key)}${rest}`)
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
        summary: (query) => request(transport, readPath('/summary'), query, parseSummary),
        equity: (query) => request(transport, readPath('/equity'), query, parseEquity),
        pnlDaily: (query) => request(transport, readPath('/pnl/daily'), query, parsePnlDaily),
        pnlBySymbol: (query) => request(transport, readPath('/pnl/symbols'), query, parseSymbols),
        fees: (query) => request(transport, readPath('/fees'), query, parseFees),
        positions: (query) => request(transport, readPath('/positions'), query, parsePositions),
        trades: (query) => request(transport, readPath('/trades'), query, parseTrades),
        trade: (id) => {
            if (!Number.isInteger(id) || id <= 0) return Promise.reject(new LedgerReadError(404, 'UnknownTrade', `Unknown trade '${id}'.`))
            return request(transport, readPath(`/trades/${id}`), {}, parseTradeDetail)
        },
        wallets: (query) => request(transport, readPath('/wallets'), query, parseWallets),
        wallet: (key) => request(transport, walletPath(key), {}, parseWallet),
        walletBalances: (key) => request(transport, walletPath(key, '/balances'), {}, parseWalletBalances),
        walletHistory: (key, query) => request(transport, walletPath(key, '/history'), query, parseWalletHistory),
        priceMarks: (query) => request(transport, readPath('/price-marks'), query, parsePriceMarks),
    }
}

export const ledger = createLedgerClient()
