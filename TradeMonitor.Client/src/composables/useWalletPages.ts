import { onMounted, onUnmounted, ref, shallowRef, watch, type Ref } from 'vue'
import type { LedgerClient } from '@/api/client'
import { LedgerReadError } from '@/api/errors'
import { createAbortGate, isAbortError } from '@/lib/abort'
import { useLedger } from '@/composables/useLedger'
import type { Filters } from '@/lib/filters'
import { REFRESH_MS, startPolling } from '@/lib/refresh'
import { latestActivity, rememberWalletNames, setRecordedAt } from '@/lib/walletNames'
import { useShowArchived } from '@/composables/useUiState'
import { buildWalletDetail, buildWalletList, summaryQuery, type WalletDetailModel, type WalletListModel } from '@/lib/walletView'
import { includeArchivedQuery } from '@/lib/visibleWallets'
import { useFilterQuery } from '@/composables/useFilterQuery'

export interface PageState<T> {
    status: Ref<'loading' | 'ready' | 'error'>
    error: Ref<string | null>
    errorStatus: Ref<number>
    model: Ref<T | null>
    refresh: () => Promise<void>
}

async function loadList(client: LedgerClient, filters: Filters, now: Date, showArchived: boolean, signal?: AbortSignal): Promise<WalletListModel> {
    const archived = includeArchivedQuery(showArchived)
    const query = summaryQuery(filters, now, showArchived)
    const [directory, summary, positions] = await Promise.all([
        client.wallets(archived, signal),
        client.summary(query, signal),
        client.positions({ ...archived, status: 'open' }, signal),
    ])
    return buildWalletList(directory.results, summary.wallets, filters, positions.results, now, showArchived)
}

async function loadDetail(client: LedgerClient, key: string, filters: Filters, now: Date, signal?: AbortSignal): Promise<WalletDetailModel> {
    const bounds = summaryQuery({ ...filters, wallet: key, mode: 'all' }, now)
    const [wallet, summary, equity, daily, fees, positions, trades, deposits, withdrawals, sweeps, balances] = await Promise.all([
        client.wallet(key, signal),
        client.summary(bounds, signal),
        client.equity(bounds, signal),
        client.pnlDaily(bounds, signal),
        client.fees(bounds, signal),
        client.positions({ wallet: key, includeArchived: true }, signal),
        client.trades({ wallet: key, from: bounds.from, to: bounds.to, take: 50 }, signal),
        client.walletHistory(key, { from: bounds.from, to: bounds.to, kind: 'deposit', take: 200 }, signal),
        client.walletHistory(key, { from: bounds.from, to: bounds.to, kind: 'withdrawal', take: 200 }, signal),
        client.walletHistory(key, { from: bounds.from, to: bounds.to, kind: 'sweep', take: 200 }, signal),
        client.walletBalances(key, signal),
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

function usePage<T>(load: (filters: Filters, now: Date, signal?: AbortSignal) => Promise<T>, watchSources: (() => unknown)[]): PageState<T> {
    const { filters } = useFilterQuery()
    const status = ref<'loading' | 'ready' | 'error'>('loading')
    const error = ref<string | null>(null)
    const errorStatus = ref(0)
    const model = shallowRef<T | null>(null) as Ref<T | null>
    let ticket = 0
    let stop = () => {}
    const reads = createAbortGate()

    async function refresh() {
        const mine = ++ticket
        const signal = reads.next()
        try {
            const next = await load(filters.current(), new Date(), signal)
            if (mine !== ticket || signal.aborted) return
            model.value = next
            status.value = 'ready'
            error.value = null
            errorStatus.value = 0
        } catch (err) {
            if (mine !== ticket || signal.aborted || isAbortError(err)) return
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
    onUnmounted(() => {
        stop()
        reads.abort()
    })

    return { status, error, errorStatus, model, refresh }
}

export function useWalletList(): PageState<WalletListModel> {
    const client = useLedger()
    const { filters } = useFilterQuery()
    const showArchived = useShowArchived()
    const page = usePage((current, now, signal) => loadList(client, current, now, showArchived.value, signal), [
        () => filters.mode,
        () => filters.range,
        () => filters.wallet,
        () => showArchived.value,
    ])
    watch(page.model, (model) => {
        if (!model) return
        rememberWalletNames(model.wallets)
        setRecordedAt(latestActivity(model.wallets.map((wallet) => wallet.lastActivityAt)))
    })
    return page
}

export function useWalletDetail(key: () => string): PageState<WalletDetailModel> {
    const client = useLedger()
    const { filters } = useFilterQuery()
    const page = usePage((current, now, signal) => loadDetail(client, key(), { ...current, wallet: key() }, now, signal), [() => filters.range, key])
    watch(page.model, (model) => {
        if (!model) return
        rememberWalletNames([{ key: model.key, name: model.name }])
        setRecordedAt(model.lastActivityAt)
    })
    return page
}
