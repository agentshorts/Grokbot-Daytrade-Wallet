import { isUnknownWallet, ledger, rangeBounds, toDashboardView, type LedgerClient, type LedgerQuery } from '@/api'
import type { DashboardView, SeriesGrain } from '@/lib/book'
import type { Filters } from '@/lib/filters'
import { includeArchivedQuery } from '@/lib/visibleWallets'

const TRADE_TAKE = 200
const SYMBOL_TAKE = 100
const MARK_TAKE = 1000

function unmatched(catalog: { key: string; name: string }[], now: Date, grain: SeriesGrain): DashboardView {
    const walletNames: Record<string, string> = {}
    for (const wallet of catalog) walletNames[wallet.key] = wallet.name
    return {
        emptyBook: catalog.length === 0,
        noMatches: catalog.length > 0,
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
        grain,
    }
}

/** Reads the ledger for the current URL filters. An unknown wallet slug is an empty match, not a thrown error. */
export async function loadDashboard(
    filters: Filters,
    now = new Date(),
    client: LedgerClient = ledger,
    showArchived = false,
    signal?: AbortSignal,
): Promise<DashboardView> {
    const archived = includeArchivedQuery(showArchived)
    const bounds = rangeBounds(filters.range, now)
    const query: LedgerQuery = {
        mode: filters.mode,
        wallet: filters.wallet,
        from: bounds.from,
        to: bounds.to,
        ...archived,
    }
    const grain: SeriesGrain = filters.range === 'today' ? 'hour' : 'day'
    const wallets = client.wallets(archived, signal)
    try {
        const [listed, summary, equity, daily, symbols, fees, positions, trades, marks] = await Promise.all([
            wallets,
            client.summary(query, signal),
            client.equity(query, signal),
            client.pnlDaily(query, signal),
            client.pnlBySymbol({ ...query, take: SYMBOL_TAKE }, signal),
            client.fees(query, signal),
            client.positions({ mode: filters.mode, wallet: filters.wallet, status: 'open', ...archived }, signal),
            client.trades({ ...query, take: TRADE_TAKE, orderBy: 'time', descending: true }, signal),
            client.priceMarks({ wallet: filters.wallet, from: bounds.from, to: bounds.to, take: MARK_TAKE }, signal),
        ])
        return toDashboardView({ catalog: listed.results, summary, equity, daily, symbols, fees, positions, trades, marks }, { range: filters.range, now, showArchived })
    } catch (err) {
        if (!isUnknownWallet(err)) throw err
        const listed = await wallets
        return unmatched(listed.results, now, grain)
    }
}
