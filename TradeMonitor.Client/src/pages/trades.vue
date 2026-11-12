<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import FilterSelect from '@/components/shell/FilterSelect.vue'
import SegmentedControl from '@/components/shell/SegmentedControl.vue'
import StatePanel from '@/components/shell/StatePanel.vue'
import TradesTable from '@/components/TradesTable.vue'
import { useFilterQuery } from '@/composables/useFilterQuery'
import { rangeBounds, type LedgerQuery, type TradeRow, type WalletInfo } from '@/api'
import { explainLedgerError, useLedger } from '@/composables/useLedger'
import { serializeFilters, type Filters, type LedgerMode } from '@/lib/filters'
import { buildTradeCsv, downloadCsv } from '@/lib/tradeCsv'
import { modeLabel, parseTradeQuery, writeTradeQuery, type TradeQueryState, type TradeSort } from '@/lib/tradeQuery'
import { REFRESH_MS, startPolling } from '@/lib/refresh'

const PAGE_SIZE = 25
const EXPORT_TAKE = 1000
/** The ledger read API rejects a take above this, so the symbol list pages at the same size. */
const SYMBOL_TAKE = 1000

const route = useRoute()
const router = useRouter()
const client = useLedger()
const { filters, setRange } = useFilterQuery()

const rows = ref<TradeRow[]>([])
const total = ref(0)
const status = ref<'loading' | 'ready' | 'error'>('loading')
const error = ref<string | null>(null)
const errorCode = ref<string | null>(null)
const exportError = ref<string | null>(null)
const exporting = ref(false)
const searchDraft = ref('')
const wallets = ref<WalletInfo[]>([])
const walletsSettled = ref(false)
const symbols = ref<string[]>([])
let ticket = 0
let walletTicket = 0
let symbolTicket = 0
let loadedSymbolScope = ''
let stopPolling = () => {}

const tradeState = computed(() => parseTradeQuery(route.query))
const pageCount = computed(() => Math.max(1, Math.ceil(total.value / PAGE_SIZE)))
const pageFrom = computed(() => (total.value === 0 ? 0 : (tradeState.value.page - 1) * PAGE_SIZE + 1))
const pageTo = computed(() => Math.min(total.value, (tradeState.value.page - 1) * PAGE_SIZE + rows.value.length))
const filtered = computed(() => {
    const state = tradeState.value
    return !!(state.symbol || state.side || state.outcome || state.q || filters.wallet || filters.mode !== 'all' || filters.range !== 'all')
})
const walletChoices = computed(() => wallets.value.filter((wallet) => filters.mode === 'all' || wallet.mode === filters.mode))
const walletOptions = computed(() => {
    const options = walletChoices.value.map((wallet) => ({ value: wallet.key, label: walletOptionLabel(wallet) }))
    const selected = filters.wallet
    if (selected && !options.some((option) => option.value === selected)) options.push({ value: selected, label: selected })
    return options
})
const symbolValue = computed(() => {
    const current = tradeState.value.symbol
    if (!current) return ''
    return symbols.value.find((symbol) => symbol.toLowerCase() === current.toLowerCase()) ?? current
})
const symbolOptions = computed(() => {
    const options = symbols.value.map((symbol) => ({ value: symbol, label: symbol }))
    const selected = symbolValue.value
    if (selected && !options.some((option) => option.value.toLowerCase() === selected.toLowerCase())) options.push({ value: selected, label: selected })
    return options
})

const listKey = computed(() =>
    JSON.stringify({
        mode: filters.mode,
        range: filters.range,
        wallet: filters.wallet,
        symbol: tradeState.value.symbol,
        side: tradeState.value.side,
        outcome: tradeState.value.outcome,
        q: tradeState.value.q,
        orderBy: tradeState.value.orderBy,
        descending: tradeState.value.descending,
        page: tradeState.value.page,
    }),
)

const sideOptions = [
    { value: 'any', label: 'Any' },
    { value: 'buy', label: 'Buy' },
    { value: 'sell', label: 'Sell' },
]
const outcomeOptions = [
    { value: 'any', label: 'Any' },
    { value: 'won', label: 'Won' },
    { value: 'lost', label: 'Lost' },
    { value: 'open', label: 'Open' },
    { value: 'closed', label: 'Closed' },
]

function listQuery(now = new Date()): LedgerQuery {
    const bounds = rangeBounds(filters.current().range, now)
    const state = tradeState.value
    return {
        wallet: filters.wallet,
        mode: filters.mode === 'all' ? null : filters.mode,
        symbol: state.symbol,
        side: state.side,
        outcome: state.outcome,
        q: state.q,
        from: bounds.from ?? null,
        to: bounds.to ?? null,
        orderBy: state.orderBy,
        descending: state.descending,
        skip: (Math.max(state.page, 1) - 1) * PAGE_SIZE,
        take: PAGE_SIZE,
    }
}

async function load(initial = false) {
    const mine = ++ticket
    if (initial) {
        rows.value = []
        total.value = 0
        status.value = 'loading'
        error.value = null
        errorCode.value = null
    }
    try {
        const page = await client.trades(listQuery())
        if (mine !== ticket) return
        rows.value = page.results
        total.value = page.total
        status.value = 'ready'
        error.value = null
        errorCode.value = null
    } catch (err) {
        if (mine !== ticket) return
        const explained = explainLedgerError(err)
        error.value = explained.message
        errorCode.value = explained.code
        if (rows.value.length === 0) status.value = 'error'
    }
}

function replaceQuery(patch: Partial<TradeQueryState>, shell?: Partial<Filters>) {
    const nextFilters = { ...filters.current(), ...shell }
    void router.replace({ query: serializeFilters(writeTradeQuery(route.query, patch), nextFilters) })
}

function applySearch() {
    const q = searchDraft.value.trim()
    replaceQuery({ q: q || null, page: 1 })
}

function onWallet(value: string) {
    const wallet = value || null
    if (wallet === filters.wallet) return
    replaceQuery({ page: 1 }, { wallet })
}

function onSymbol(value: string) {
    const symbol = value || null
    if (symbol === tradeState.value.symbol) return
    replaceQuery({ symbol, page: 1 })
}

function onSide(value: string) {
    replaceQuery({ side: value === 'buy' || value === 'sell' ? value : null, page: 1 })
}

function onOutcome(value: string) {
    const outcome = value === 'won' || value === 'lost' || value === 'open' || value === 'closed' ? value : null
    replaceQuery({ outcome, page: 1 })
}

function onSort(order: TradeSort) {
    const current = tradeState.value
    if (current.orderBy === order) replaceQuery({ orderBy: order, descending: !current.descending, page: 1 })
    else replaceQuery({ orderBy: order, descending: true, page: 1 })
}

function setPage(page: number) {
    replaceQuery({ page })
}

function openTrade(id: number) {
    replaceQuery({ tradeId: id })
}

function clearFilters() {
    searchDraft.value = ''
    replaceQuery({ symbol: null, side: null, outcome: null, q: null, orderBy: 'time', descending: true, page: 1 }, { wallet: null })
}

function symbolScope(): { wallet: string | null; mode: string | null } {
    return {
        wallet: filters.wallet,
        mode: filters.mode === 'all' ? null : filters.mode,
    }
}

function sameSymbolScope(left: { wallet: string | null; mode: string | null }, right: { wallet: string | null; mode: string | null }) {
    return left.wallet === right.wallet && left.mode === right.mode
}

/** Distinct symbols for the selected wallet, or for the current mode when no wallet is selected. */
async function loadSymbols() {
    if (filters.wallet && !walletsSettled.value) return
    if (walletFallsOutOfMode(wallets.value, filters.mode, filters.wallet)) {
        symbolTicket += 1
        return
    }
    const mine = ++symbolTicket
    const scope = symbolScope()
    const key = `${scope.wallet ?? ''}\n${scope.mode ?? ''}`
    if (key !== loadedSymbolScope) {
        loadedSymbolScope = key
        symbols.value = []
    }
    try {
        const rows: TradeRow[] = []
        let skip = 0
        let total = Number.POSITIVE_INFINITY
        while (rows.length < total) {
            const page = await client.trades({
                wallet: scope.wallet,
                mode: scope.mode,
                orderBy: 'symbol',
                descending: false,
                skip,
                take: SYMBOL_TAKE,
            })
            if (mine !== symbolTicket || !sameSymbolScope(scope, symbolScope())) return
            total = page.total
            if (page.results.length === 0) break
            rows.push(...page.results)
            skip += page.results.length
        }
        if (mine !== symbolTicket || !sameSymbolScope(scope, symbolScope())) return
        const next = distinctTradeSymbols(rows)
        symbols.value = next
        const current = tradeState.value.symbol
        if (current && !next.some((symbol) => symbol.toLowerCase() === current.toLowerCase())) {
            replaceQuery({ symbol: null, page: 1 })
        }
    } catch {
        if (mine !== symbolTicket) return
    }
}

async function loadWallets() {
    const mine = ++walletTicket
    try {
        const page = await client.wallets({ includeArchived: true })
        if (mine !== walletTicket) return
        wallets.value = page.results
    } catch {
        if (mine !== walletTicket) return
    } finally {
        if (mine === walletTicket) walletsSettled.value = true
    }
}

async function exportCsv() {
    exporting.value = true
    exportError.value = null
    try {
        const base = listQuery()
        const all: TradeRow[] = []
        let skip = 0
        let count = Number.POSITIVE_INFINITY
        while (all.length < count) {
            const page = await client.trades({ ...base, skip, take: EXPORT_TAKE })
            count = page.total
            if (page.results.length === 0) break
            all.push(...page.results)
            skip += page.results.length
        }
        downloadCsv('trades.csv', buildTradeCsv(all))
    } catch (err) {
        exportError.value = explainLedgerError(err).message
    } finally {
        exporting.value = false
    }
}

watch(
    listKey,
    () => {
        void load(true)
    },
    { immediate: true },
)

watch(
    tradeState,
    (state) => {
        searchDraft.value = state.q ?? ''
    },
    { immediate: true },
)

watch(
    () => [filters.mode, filters.range, filters.wallet] as const,
    (next, prev) => {
        if (!prev) return
        if (walletFallsOutOfMode(wallets.value, next[0], next[2])) {
            replaceQuery({ page: 1 }, { wallet: null })
            return
        }
        if (next[0] === prev[0] && next[1] === prev[1] && next[2] === prev[2]) return
        if (tradeState.value.page > 1) replaceQuery({ page: 1 })
    },
)

watch(wallets, () => {
    if (walletFallsOutOfMode(wallets.value, filters.mode, filters.wallet)) replaceQuery({ page: 1 }, { wallet: null })
})

watch(
    () => [filters.mode, filters.wallet, walletsSettled.value] as const,
    () => {
        void loadSymbols()
    },
    { immediate: true },
)

onMounted(() => {
    void loadWallets()
    stopPolling = startPolling(() => {
        void load(false)
        void loadWallets()
        void loadSymbols()
    }, REFRESH_MS)
})

onUnmounted(() => stopPolling())

function walletOptionLabel(wallet: WalletInfo): string {
    const name = wallet.name.trim() || wallet.key
    const tag = modeLabel(wallet.mode)
    return wallet.isArchived ? `${name} · ${tag} · Archived` : `${name} · ${tag}`
}

function walletFallsOutOfMode(rows: WalletInfo[], mode: LedgerMode, selected: string | null): boolean {
    if (!selected || mode === 'all') return false
    const match = rows.find((wallet) => wallet.key === selected)
    return !!match && match.mode !== mode
}

function distinctTradeSymbols(rows: TradeRow[]): string[] {
    const seen = new Map<string, string>()
    for (const row of rows) {
        const symbol = row.symbol.trim()
        if (!symbol) continue
        const key = symbol.toLowerCase()
        if (!seen.has(key)) seen.set(key, symbol)
    }
    return [...seen.values()].sort((a, b) => a.localeCompare(b))
}
</script>

<template>
    <div data-testid="trades" class="space-y-6">
        <div class="flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
            <div>
                <h1 class="text-xl font-semibold text-gray-900 dark:text-white/90">Trades</h1>
                <p class="mt-1 text-sm text-gray-500 dark:text-gray-400">
                    Recorded fills. Times are CT. Open a row for the lot, the fees, and the price chart.
                </p>
            </div>
            <button
                type="button"
                class="rounded-lg border border-gray-200 px-3 py-1.5 text-sm font-medium text-gray-700 disabled:opacity-50 dark:border-gray-700 dark:text-gray-200"
                data-testid="export-csv"
                :disabled="exporting || status !== 'ready' || total === 0"
                @click="exportCsv"
            >
                {{ exporting ? 'Exporting' : 'Export CSV' }}
            </button>
        </div>

        <p v-if="error && status === 'ready'" class="rounded-xl border border-error-200 bg-error-25 px-4 py-3 text-sm text-error-700" role="alert">
            {{ error }}
            <button type="button" class="ms-2 font-medium underline" @click="load(false)">Try again</button>
        </p>
        <p v-if="exportError" class="text-sm text-error-600" role="alert">{{ exportError }}</p>

        <div class="flex flex-col gap-3 rounded-2xl border border-gray-200 bg-white p-4 dark:border-gray-800 dark:bg-white/[0.03]">
            <div class="flex flex-col gap-3 lg:flex-row lg:flex-wrap lg:items-end">
                <FilterSelect
                    label="Wallet"
                    test-id="filter-wallet"
                    empty-label="All wallets"
                    :value="filters.wallet ?? ''"
                    :options="walletOptions"
                    @change="onWallet"
                />
                <FilterSelect
                    label="Symbol"
                    test-id="filter-symbol"
                    empty-label="All symbols"
                    :value="symbolValue"
                    :options="symbolOptions"
                    @change="onSymbol"
                />
                <form class="flex min-w-0 flex-1 items-end gap-2" data-testid="search-form" @submit.prevent="applySearch">
                    <label class="block min-w-0 flex-1 text-sm">
                        <span class="mb-1 block text-xs font-medium tracking-wide text-gray-400 uppercase">Search</span>
                        <input
                            v-model="searchDraft"
                            type="search"
                            class="w-full rounded-lg border border-gray-200 bg-white px-3 py-1.5 text-sm dark:border-gray-700 dark:bg-gray-900"
                            aria-label="Search trades"
                            data-testid="filter-search"
                            placeholder="Symbol, title, note, order"
                        />
                    </label>
                    <button type="submit" class="rounded-lg bg-brand-500 px-3 py-1.5 text-sm font-medium text-white">Search</button>
                </form>
            </div>
            <div class="flex flex-col gap-3 sm:flex-row sm:flex-wrap sm:items-center">
                <SegmentedControl label="Side" test-id="side-filter" :value="tradeState.side ?? 'any'" :options="sideOptions" @select="onSide" />
                <SegmentedControl
                    label="Won or lost"
                    test-id="outcome-filter"
                    :value="tradeState.outcome ?? 'any'"
                    :options="outcomeOptions"
                    @select="onOutcome"
                />
                <button
                    v-if="tradeState.symbol || tradeState.side || tradeState.outcome || tradeState.q || filters.wallet"
                    type="button"
                    class="text-sm font-medium text-brand-600"
                    data-testid="clear-filters"
                    @click="clearFilters"
                >
                    Clear filters
                </button>
            </div>
        </div>

        <StatePanel v-if="status === 'loading'" tone="loading" title="Loading trades" />
        <StatePanel v-else-if="status === 'error'" tone="error" title="Could not load trades" :body="error || 'Try again in a moment.'">
            <RouterLink v-if="errorCode === 'auth'" to="/signin" class="text-sm font-medium text-brand-600">Sign in</RouterLink>
            <button v-else type="button" class="rounded-lg bg-brand-500 px-3 py-1.5 text-sm font-medium text-white" @click="load(true)">Try again</button>
        </StatePanel>
        <StatePanel v-else-if="total === 0" title="No trades in this view" :body="filtered ? 'Nothing matches these filters.' : 'No trades recorded yet.'">
            <button
                v-if="filters.range !== 'all'"
                type="button"
                class="rounded-lg border border-gray-200 px-3 py-1.5 text-sm font-medium dark:border-gray-700"
                @click="setRange('all')"
            >
                Show all dates
            </button>
        </StatePanel>
        <section v-else class="overflow-hidden rounded-2xl border border-gray-200 bg-white dark:border-gray-800 dark:bg-white/[0.03]">
            <TradesTable
                :trades="rows"
                :selected-id="tradeState.tradeId"
                :order-by="tradeState.orderBy"
                :descending="tradeState.descending"
                @select="openTrade"
                @sort="onSort"
            />
            <div
                class="flex flex-col gap-3 border-t border-gray-100 px-4 py-3 sm:flex-row sm:items-center sm:justify-between dark:border-gray-800"
                data-testid="pager"
            >
                <p class="text-sm text-gray-500 dark:text-gray-400" data-testid="page-status">Showing {{ pageFrom }}-{{ pageTo }} of {{ total }}</p>
                <div class="flex items-center gap-2">
                    <button
                        type="button"
                        class="rounded-lg border border-gray-200 px-3 py-1.5 text-sm font-medium disabled:opacity-40 dark:border-gray-700"
                        data-testid="page-prev"
                        :disabled="tradeState.page <= 1"
                        @click="setPage(tradeState.page - 1)"
                    >
                        Previous
                    </button>
                    <span class="text-sm text-gray-500">Page {{ Math.min(tradeState.page, pageCount) }} of {{ pageCount }}</span>
                    <button
                        type="button"
                        class="rounded-lg border border-gray-200 px-3 py-1.5 text-sm font-medium disabled:opacity-40 dark:border-gray-700"
                        data-testid="page-next"
                        :disabled="tradeState.page >= pageCount"
                        @click="setPage(tradeState.page + 1)"
                    >
                        Next
                    </button>
                </div>
            </div>
        </section>
    </div>
</template>
