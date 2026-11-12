import { isUnknownWallet, ledger, rangeBounds, toDashboardView, type LedgerClient, type LedgerQuery } from '@/api'
import type { DashboardView, SeriesGrain } from '@/lib/book'
import type { Filters } from '@/lib/filters'

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
export async function loadDashboard(filters: Filters, now = new Date(), client: LedgerClient = ledger): Promise<DashboardView> {
    const listed = await client.wallets()
    const catalog = listed.results
    const bounds = rangeBounds(filters.range, now)
    const query: LedgerQuery = {
        mode: filters.mode,
        wallet: filters.wallet,
        from: bounds.from,
        to: bounds.to,
    }
    const grain: SeriesGrain = filters.range === 'today' ? 'hour' : 'day'
    try {
        const [summary, equity, daily, symbols, fees, positions, trades, marks] = await Promise.all([
            client.summary(query),
            client.equity(query),
            client.pnlDaily(query),
            client.pnlBySymbol({ ...query, take: SYMBOL_TAKE }),
            client.fees(query),
            client.positions({ mode: filters.mode, wallet: filters.wallet, status: 'open' }),
            client.trades({ ...query, take: TRADE_TAKE, orderBy: 'time', descending: true }),
            client.priceMarks({ wallet: filters.wallet, from: bounds.from, to: bounds.to, take: MARK_TAKE }),
        ])
        return toDashboardView({ catalog, summary, equity, daily, symbols, fees, positions, trades, marks }, { range: filters.range, now })
    } catch (err) {
        if (isUnknownWallet(err)) return unmatched(catalog, now, grain)
        throw err
    }
}
