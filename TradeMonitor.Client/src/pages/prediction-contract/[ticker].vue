<script setup lang="ts">
import { computed, defineAsyncComponent, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import type { ContractDetail } from '@/api'
import ChartPending from '@/components/shell/ChartPending.vue'
import MoneyCell from '@/components/MoneyCell.vue'
import StatePanel from '@/components/shell/StatePanel.vue'
import { explainLedgerError, useLedger } from '@/composables/useLedger'
import { useTheme } from '@/composables/useTheme'
import { centsText, formatAvgCost, formatSideQuote, formatStrikeLabel, noLineNote, toContractMarkers, toContractSeries } from '@/lib/contractChart'
import { formatQty, formatTimeCt } from '@/lib/format'
import { loadPriceChart } from '@/lib/loadPriceChart'
import { readOscillationPrefs } from '@/lib/oscillationPrefs'
import { browserStorage } from '@/lib/predictionContract'
import { REFRESH_MS, startPolling } from '@/lib/refresh'

const PriceChart = defineAsyncComponent({
    loader: loadPriceChart,
    loadingComponent: ChartPending,
    delay: 0,
})

const route = useRoute()
const router = useRouter()
const client = useLedger()
const { isDark } = useTheme()

const ranges = [
    { id: '1', label: '1h', hours: 1, all: false },
    { id: '6', label: '6h', hours: 6, all: false },
    { id: '24', label: '24h', hours: 24, all: false },
    { id: 'all', label: 'All', hours: 168, all: true },
]

const hours = ref(6)
const cents = ref(5)
const all = ref(false)
const status = ref<'loading' | 'ready' | 'error'>('loading')
const error = ref<string | null>(null)
const model = ref<ContractDetail | null>(null)
let ticket = 0
let stop = () => {}

const ticker = computed(() => {
    const params = route.params
    const raw = 'ticker' in params ? params.ticker : ''
    const text = typeof raw === 'string' ? raw : Array.isArray(raw) ? (raw[0] ?? '') : ''
    try {
        return decodeURIComponent(text)
    } catch {
        return text
    }
})

const choices = computed(() => {
    if (all.value || ranges.some((item) => !item.all && item.hours === hours.value)) return ranges
    return [...ranges.slice(0, 3), { id: String(hours.value), label: `${hours.value}h`, hours: hours.value, all: false }, ranges[3]!]
})

const series = computed(() => toContractSeries(model.value?.points ?? []))
const markers = computed(() => toContractMarkers(model.value?.marks ?? [], series.value.yes))
const strike = computed(() => (model.value ? formatStrikeLabel(model.value.strike, model.value.direction) : ''))
const note = computed(() => (model.value ? noLineNote(model.value) : ''))

function readRoute() {
    const prefs = readOscillationPrefs(browserStorage())
    const hoursQuery = Number(route.query.hours)
    const centsQuery = Number(route.query.cents)
    all.value = route.query.all === '1' || route.query.all === 'true'
    hours.value = all.value ? 168 : Number.isFinite(hoursQuery) && hoursQuery > 0 ? hoursQuery : prefs.lookbackHours
    cents.value = Number.isFinite(centsQuery) && centsQuery > 0 && centsQuery <= 50 ? centsQuery : prefs.thresholdCents
}

function writeQuery() {
    const query = { ...route.query }
    if (all.value) {
        delete query.hours
        query.all = '1'
    } else {
        delete query.all
        query.hours = String(hours.value)
    }
    query.cents = String(cents.value)
    void router.replace({ query })
}

function sourceLabel(source: string): string {
    if (source === 'coinbase') return 'Coinbase'
    if (source === 'kalshi') return 'Kalshi'
    return 'No market data'
}

function statusLabel(value: string | null): string {
    if (!value) return 'Unknown'
    return value.slice(0, 1).toUpperCase() + value.slice(1)
}

function percent(value: number | null): string {
    if (value == null || !Number.isFinite(value)) return 'n/a'
    return `${Math.round(value)}%`
}

function formatCents(value: number | null): string {
    if (value == null || !Number.isFinite(value)) return 'n/a'
    return `${centsText(value)}c`
}

function activeRange(choice: { hours: number; all: boolean }): boolean {
    return choice.all ? all.value : !all.value && choice.hours === hours.value
}

function selectRange(choice: { hours: number; all: boolean }) {
    all.value = choice.all
    hours.value = choice.hours
    writeQuery()
    void refresh()
}

function onThreshold(event: Event) {
    const value = Number((event.target as HTMLInputElement).value)
    if (!Number.isFinite(value) || value <= 0 || value > 50) return
    cents.value = Math.round(value * 10) / 10
    writeQuery()
    void refresh()
}

async function refresh() {
    const current = ticker.value
    if (!current) {
        status.value = 'error'
        error.value = 'Enter a prediction ticker.'
        return
    }
    const mine = ++ticket
    try {
        const response = await client.contract({
            ticker: current,
            lookbackHours: all.value ? undefined : hours.value,
            thresholdCents: cents.value,
            allAvailable: all.value || undefined,
        })
        if (mine !== ticket) return
        model.value = response.result
        status.value = 'ready'
        error.value = null
    } catch (err) {
        if (mine !== ticket) return
        error.value = explainLedgerError(err).message
        if (!model.value) status.value = 'error'
    }
}

watch(ticker, () => {
    void refresh()
})

onMounted(() => {
    readRoute()
    void refresh()
    stop = startPolling(() => {
        void refresh()
    }, REFRESH_MS)
})
onUnmounted(() => stop())
</script>

<template>
    <div data-testid="contract-detail" class="space-y-6">
        <div class="flex flex-wrap items-start justify-between gap-3">
            <div>
                <RouterLink
                    data-testid="contract-back"
                    class="text-sm font-medium text-gray-500 hover:text-brand-600 dark:text-gray-400"
                    :to="{ path: '/predictions', query: { view: 'oscillation' } }"
                >
                    Oscillation
                </RouterLink>
                <h1 data-testid="contract-title" class="mt-1 text-xl font-semibold text-gray-900 dark:text-white/90">
                    {{ model?.title || ticker }}
                </h1>
                <p v-if="model?.subtitle" class="mt-1 text-sm text-gray-500 dark:text-gray-400">{{ model.subtitle }}</p>
            </div>
            <p v-if="model" class="text-sm text-gray-500 dark:text-gray-400">{{ sourceLabel(model.source) }}</p>
        </div>

        <StatePanel v-if="status === 'loading' && !model" tone="loading" title="Loading contract" />
        <StatePanel v-else-if="status === 'error' && !model" tone="error" title="Could not load this contract" :body="error || 'Try again in a moment.'">
            <button type="button" class="rounded-lg bg-brand-500 px-3 py-1.5 text-sm font-medium text-white" @click="refresh()">Try again</button>
        </StatePanel>
        <template v-else-if="model">
            <div class="flex flex-wrap gap-x-4 gap-y-1 text-sm text-gray-600 dark:text-gray-300">
                <span class="font-mono text-xs">{{ model.ticker }}</span>
                <span v-if="model.underlying">{{ model.underlying }}</span>
                <span v-if="strike">{{ strike }}</span>
                <span data-testid="contract-status">{{ statusLabel(model.status) }}</span>
                <span
                    v-if="model.oscillating"
                    class="rounded-full bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-800 dark:bg-amber-400/15 dark:text-amber-200"
                    >Swinging</span
                >
            </div>
            <p class="text-sm text-gray-500 dark:text-gray-400">
                Closes {{ model.closeCentral || 'at an unknown time' }}<span v-if="model.closeEastern"> ({{ model.closeEastern }})</span>. The hour on the
                ticker is Eastern.
            </p>

            <div class="flex flex-wrap items-end justify-between gap-3">
                <div class="flex flex-wrap gap-2" data-testid="contract-ranges">
                    <button
                        v-for="choice in choices"
                        :key="choice.id"
                        type="button"
                        :data-testid="`contract-range-${choice.id}`"
                        class="rounded-lg border px-3 py-1.5 text-sm font-medium"
                        :class="
                            activeRange(choice)
                                ? 'border-brand-500 bg-brand-50 text-brand-700 dark:bg-brand-500/10 dark:text-brand-200'
                                : 'border-gray-200 text-gray-700 dark:border-gray-700 dark:text-gray-200'
                        "
                        @click="selectRange(choice)"
                    >
                        {{ choice.label }}
                    </button>
                </div>
                <label class="text-xs font-medium text-gray-500">
                    Reversal threshold
                    <span class="mt-1 flex items-center gap-2">
                        <input
                            data-testid="contract-threshold"
                            class="w-20 rounded-lg border border-gray-200 bg-white px-2 py-1.5 text-sm dark:border-gray-700 dark:bg-gray-950"
                            type="number"
                            min="0.5"
                            max="50"
                            step="0.5"
                            :value="cents"
                            @change="onThreshold"
                        />
                        <span>c</span>
                    </span>
                </label>
            </div>

            <section class="rounded-2xl border border-gray-200 bg-white p-4 dark:border-gray-800 dark:bg-white/[0.03]">
                <div class="mb-3 flex flex-wrap items-center justify-between gap-2">
                    <div class="flex gap-4 text-xs text-gray-500 dark:text-gray-400">
                        <span class="inline-flex items-center gap-2"><span class="h-0.5 w-4 bg-[#465fff]"></span> YES</span>
                        <span class="inline-flex items-center gap-2"><span class="h-0.5 w-4 bg-[#f79009]"></span> NO</span>
                    </div>
                    <p v-if="note" data-testid="contract-note" class="text-xs text-gray-500 dark:text-gray-400">{{ note }}</p>
                </div>
                <PriceChart
                    data-testid="contract-chart"
                    :points="series.yes"
                    :compare="series.no"
                    :markers="markers"
                    :dark="isDark"
                    :empty="series.yes.length === 0"
                    empty-label="No price history in this window."
                />
                <p v-if="model.error" class="mt-2 text-xs text-error-600">{{ model.error }}</p>
            </section>

            <dl class="grid grid-cols-2 gap-3 sm:grid-cols-4" data-testid="contract-stats">
                <div class="rounded-xl border border-gray-200 px-3 py-2 dark:border-gray-800">
                    <dt class="text-xs text-gray-500">YES</dt>
                    <dd data-testid="contract-yes" class="text-sm font-medium tabular-nums">
                        {{ formatSideQuote({ cents: model.yesCents, bidCents: model.yesBidCents, askCents: model.yesAskCents, label: model.yesLabel }) }}
                    </dd>
                </div>
                <div class="rounded-xl border border-gray-200 px-3 py-2 dark:border-gray-800">
                    <dt class="text-xs text-gray-500">NO</dt>
                    <dd data-testid="contract-no" class="text-sm font-medium tabular-nums">
                        {{ formatSideQuote({ cents: model.noCents, bidCents: model.noBidCents, askCents: model.noAskCents, label: model.noLabel }) }}
                    </dd>
                </div>
                <div class="rounded-xl border border-gray-200 px-3 py-2 dark:border-gray-800">
                    <dt class="text-xs text-gray-500">High</dt>
                    <dd class="text-sm font-medium tabular-nums">{{ formatCents(model.highCents) }}</dd>
                </div>
                <div class="rounded-xl border border-gray-200 px-3 py-2 dark:border-gray-800">
                    <dt class="text-xs text-gray-500">Low</dt>
                    <dd class="text-sm font-medium tabular-nums">{{ formatCents(model.lowCents) }}</dd>
                </div>
                <div class="rounded-xl border border-gray-200 px-3 py-2 dark:border-gray-800">
                    <dt class="text-xs text-gray-500">Swing</dt>
                    <dd class="text-sm font-medium tabular-nums">{{ formatCents(model.rangeCents) }} ({{ percent(model.rangePercent) }})</dd>
                </div>
                <div class="rounded-xl border border-gray-200 px-3 py-2 dark:border-gray-800">
                    <dt class="text-xs text-gray-500">Reversals</dt>
                    <dd data-testid="contract-reversals" class="text-sm font-medium tabular-nums">{{ model.reversals }}</dd>
                </div>
                <div v-if="model.volume != null" class="rounded-xl border border-gray-200 px-3 py-2 dark:border-gray-800">
                    <dt class="text-xs text-gray-500">Volume</dt>
                    <dd class="text-sm font-medium tabular-nums">{{ formatQty(model.volume) }}</dd>
                </div>
                <div v-if="model.openInterest != null" class="rounded-xl border border-gray-200 px-3 py-2 dark:border-gray-800">
                    <dt class="text-xs text-gray-500">Open interest</dt>
                    <dd class="text-sm font-medium tabular-nums">{{ formatQty(model.openInterest) }}</dd>
                </div>
            </dl>

            <section v-if="model.holdings.length === 0" data-testid="contract-empty-holding" class="text-sm text-gray-500 dark:text-gray-400">
                No open pm- wallet holds this contract.
            </section>
            <section v-else class="space-y-3" data-testid="contract-holdings">
                <article
                    v-for="holding in model.holdings"
                    :key="holding.wallet"
                    data-testid="contract-holding"
                    class="rounded-2xl border border-gray-200 bg-white p-4 dark:border-gray-800 dark:bg-white/[0.03]"
                >
                    <div class="flex flex-wrap items-baseline justify-between gap-2">
                        <RouterLink
                            :to="`/wallet/${encodeURIComponent(holding.wallet)}`"
                            class="font-medium text-gray-900 hover:text-brand-600 dark:text-white/90"
                        >
                            {{ holding.name }}
                        </RouterLink>
                        <p class="text-sm text-gray-500">{{ holding.side }}</p>
                    </div>
                    <dl class="mt-3 grid grid-cols-2 gap-3 text-sm sm:grid-cols-4">
                        <div>
                            <dt class="text-xs text-gray-500">Open qty</dt>
                            <dd class="tabular-nums">{{ formatQty(holding.qty) }}</dd>
                        </div>
                        <div>
                            <dt class="text-xs text-gray-500">Avg cost</dt>
                            <dd class="tabular-nums">{{ formatAvgCost(holding.avgCost) }}</dd>
                        </div>
                        <div>
                            <dt class="text-xs text-gray-500">Unrealized P&L</dt>
                            <dd><MoneyCell :value="holding.unrealizedPnl" /></dd>
                        </div>
                    </dl>
                    <ul v-if="holding.fills.length > 0" class="mt-3 space-y-1 text-xs text-gray-500 dark:text-gray-400">
                        <li v-for="fill in holding.fills" :key="fill.tradeId">
                            {{ fill.side === 'sell' ? 'Sell' : 'Buy' }} {{ formatQty(fill.qty) }} @ {{ centsText(fill.price * 100) }}c
                            <span v-if="formatTimeCt(fill.at)">, {{ formatTimeCt(fill.at) }}</span>
                        </li>
                    </ul>
                </article>
            </section>
        </template>
    </div>
</template>

<route lang="yaml">
path: /predictions/contract/:ticker
</route>
