import { onMounted, onUnmounted, ref, watch, type Ref } from 'vue'
import { ledger, type LedgerClient } from '@/api/client'
import { LedgerReadError } from '@/api/errors'
import type { Filters } from '@/lib/filters'
import { REFRESH_MS, startPolling } from '@/lib/refresh'
import { latestActivity, rememberWalletNames, setRecordedAt } from '@/lib/walletNames'
import { buildWalletDetail, buildWalletList, summaryQuery, type WalletDetailModel, type WalletListModel } from '@/lib/walletView'
import { useFilterQuery } from '@/composables/useFilterQuery'

export interface PageState<T> {
    status: Ref<'loading' | 'ready' | 'error'>
    error: Ref<string | null>
    errorStatus: Ref<number>
    model: Ref<T | null>
    refresh: () => Promise<void>
}

async function loadList(client: LedgerClient, filters: Filters, now: Date): Promise<WalletListModel> {
    const query = summaryQuery(filters, now)
    const [directory, summary] = await Promise.all([client.wallets({ includeArchived: true }), client.summary(query)])
    return buildWalletList(directory.results, summary.wallets, filters)
}

async function loadDetail(client: LedgerClient, key: string, filters: Filters, now: Date): Promise<WalletDetailModel> {
    const bounds = summaryQuery({ ...filters, wallet: key, mode: 'all' }, now)
    const [wallet, summary, equity, daily, fees, positions, trades, deposits, withdrawals, sweeps, balances] = await Promise.all([
        client.wallet(key),
        client.summary(bounds),
        client.equity(bounds),
        client.pnlDaily(bounds),
        client.fees(bounds),
        client.positions({ wallet: key, includeArchived: true }),
        client.trades({ wallet: key, from: bounds.from, to: bounds.to, take: 50 }),
        client.walletHistory(key, { from: bounds.from, to: bounds.to, kind: 'deposit', take: 200 }),
        client.walletHistory(key, { from: bounds.from, to: bounds.to, kind: 'withdrawal', take: 200 }),
        client.walletHistory(key, { from: bounds.from, to: bounds.to, kind: 'sweep', take: 200 }),
        client.walletBalances(key),
    ])
    return buildWalletDetail({
        wallet: wallet.result,
        card: summary.wallets.find((item) => item.key === key) ?? summary.wallets[0] ?? null,
        equity: equity.wallets.find((row) => row.wallet === key)?.points ?? equity.total,
        days: daily.wallets.find((row) => row.wallet === key)?.days ?? daily.results,
        feeTotal: fees.total,
        feeDays: fees.days,
        positions: positions.results,
        staleAfterMinutes: positions.markStaleAfterMinutes,
        trades: trades.results,
        history: [...deposits.results, ...withdrawals.results, ...sweeps.results],
        balances: balances.result,
        range: filters.range,
        now,
    })
}

function usePage<T>(load: (filters: Filters, now: Date) => Promise<T>, watchSources: (() => unknown)[]): PageState<T> {
    const { filters } = useFilterQuery()
    const status = ref<'loading' | 'ready' | 'error'>('loading')
    const error = ref<string | null>(null)
    const errorStatus = ref(0)
    const model = ref<T | null>(null) as Ref<T | null>
    let ticket = 0
    let stop = () => {}

    async function refresh() {
        const mine = ++ticket
        try {
            const next = await load(filters.current(), new Date())
            if (mine !== ticket) return
            model.value = next
            status.value = 'ready'
            error.value = null
            errorStatus.value = 0
        } catch (err) {
            if (mine !== ticket) return
            const failure = err instanceof LedgerReadError ? err : null
            error.value = failure?.message ?? 'Could not load wallets.'
            errorStatus.value = failure?.status ?? 0
            if (!model.value) status.value = 'error'
        }
    }

    watch(watchSources, () => {
        void refresh()
    })

    onMounted(() => {
        void refresh()
        stop = startPolling(() => {
            void refresh()
        }, REFRESH_MS)
    })
    onUnmounted(() => stop())

    return { status, error, errorStatus, model, refresh }
}

export function useWalletList(): PageState<WalletListModel> {
    const { filters } = useFilterQuery()
    const page = usePage((current, now) => loadList(ledger, current, now), [() => filters.mode, () => filters.range, () => filters.wallet])
    watch(page.model, (model) => {
        if (!model) return
        rememberWalletNames(model.wallets)
        setRecordedAt(latestActivity(model.wallets.map((wallet) => wallet.lastActivityAt)))
    })
    return page
}

export function useWalletDetail(key: () => string): PageState<WalletDetailModel> {
    const { filters } = useFilterQuery()
    const page = usePage((current, now) => loadDetail(ledger, key(), { ...current, wallet: key() }, now), [() => filters.range, key])
    watch(page.model, (model) => {
        if (!model) return
        rememberWalletNames([{ key: model.key, name: model.name }])
        setRecordedAt(model.lastActivityAt)
    })
    return page
}
