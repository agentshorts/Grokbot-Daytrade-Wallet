<script setup lang="ts">
import { computed, defineAsyncComponent, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import AmountCell from '@/components/AmountCell.vue'
import ChartPending from '@/components/shell/ChartPending.vue'
import MoneyCell from '@/components/MoneyCell.vue'
import PctCell from '@/components/PctCell.vue'
import TimeCell from '@/components/TimeCell.vue'
import { useTheme } from '@/composables/useTheme'
import { formatPrice, formatQty } from '@/lib/format'
import type { TradeDetailResponse, TradeFill, TradeRow } from '@/api'
import { explainLedgerError, useLedger } from '@/composables/useLedger'
import { chartMarkersAttr, chartPointsAttr, tradeChartModel } from '@/lib/tradeChart'
import { loadPriceChart } from '@/lib/loadPriceChart'
import { modeLabel, parseTradeQuery, priceKindTitle, sideLabel, statusLabel, tradeKindLabel, writeTradeQuery } from '@/lib/tradeQuery'
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

const detail = ref<TradeDetailResponse | null>(null)
const status = ref<'idle' | 'loading' | 'ready' | 'error'>('idle')
const error = ref<string | null>(null)
const closeButton = ref<HTMLButtonElement | null>(null)
let ticket = 0
let stopPolling = () => {}

const tradeId = computed(() => parseTradeQuery(route.query).tradeId)
const chart = computed(() => (detail.value ? tradeChartModel(detail.value) : { points: [], markers: [], empty: true }))
const trade = computed(() => detail.value?.result ?? null)

const fills = computed<TradeFill[]>(() => {
    if (!detail.value) return []
    if (detail.value.fills.length > 0) return detail.value.fills
    return [fillFromTrade(detail.value.result)]
})

const matchedCost = computed(() => fills.value.reduce((sum, fill) => sum + fill.cost, 0))
const proceeds = computed(() => fills.value.reduce((sum, fill) => sum + fill.proceeds, 0))

function fillFromTrade(row: TradeRow): TradeFill {
    return {
        tradeId: row.id,
        side: row.side,
        symbol: row.symbol,
        qty: row.qty,
        price: row.price,
        buyFee: row.side === 'buy' ? row.fee : 0,
        sellFee: row.side === 'sell' ? row.fee : 0,
        cost: row.side === 'buy' ? row.qty * row.price : 0,
        proceeds: row.side === 'sell' ? row.qty * row.price : 0,
        realizedPnl: row.realizedPnl,
        executedAt: row.executedAt,
    }
}

async function load(id: number | null, options?: { silent?: boolean }) {
    const mine = ++ticket
    if (id == null) {
        detail.value = null
        error.value = null
        status.value = 'idle'
        return
    }
    if (!options?.silent) {
        status.value = 'loading'
        error.value = null
        if (detail.value?.result.id !== id) detail.value = null
    }
    try {
        const next = await client.trade(id)
        if (mine !== ticket) return
        detail.value = next
        status.value = 'ready'
        error.value = null
    } catch (err) {
        if (mine !== ticket) return
        error.value = explainLedgerError(err).message
        if (!options?.silent || detail.value?.result.id !== id) {
            detail.value = null
            status.value = 'error'
        }
    }
}

function close() {
    void router.replace({ query: writeTradeQuery(route.query, { tradeId: null }) })
}

function onKey(event: KeyboardEvent) {
    if (event.key === 'Escape' && tradeId.value != null) {
        event.preventDefault()
        close()
    }
}

watch(
    tradeId,
    (id) => {
        void load(id)
        if (id != null) {
            document.body.style.overflow = 'hidden'
            void nextFocus()
        } else {
            document.body.style.overflow = ''
        }
    },
    { immediate: true },
)

async function nextFocus() {
    await nextTick()
    closeButton.value?.focus()
}

onMounted(() => {
    document.addEventListener('keydown', onKey)
    stopPolling = startPolling(() => {
        if (tradeId.value != null) void load(tradeId.value, { silent: true })
    }, REFRESH_MS)
})

onUnmounted(() => {
    document.removeEventListener('keydown', onKey)
    document.body.style.overflow = ''
    stopPolling()
})
</script>

<template>
    <div v-if="tradeId != null" class="fixed inset-0 z-[70]" role="presentation">
        <button type="button" class="absolute inset-0 bg-gray-900/40" aria-label="Close trade" @click="close" />
        <aside
            class="absolute inset-y-0 end-0 flex w-full max-w-2xl flex-col overflow-y-auto bg-white shadow-xl dark:bg-gray-900"
            role="dialog"
            aria-modal="true"
            aria-labelledby="trade-drawer-title"
            data-testid="trade-drawer"
        >
            <header
                class="sticky top-0 z-10 flex items-start justify-between gap-4 border-b border-gray-200 bg-white px-5 py-4 dark:border-gray-800 dark:bg-gray-900"
            >
                <div class="min-w-0">
                    <p class="text-xs font-medium tracking-wide text-gray-400 uppercase">Trade {{ tradeId }}</p>
                    <h2 id="trade-drawer-title" class="truncate text-lg font-semibold text-gray-900 dark:text-white">
                        {{ trade?.symbol || 'Trade' }}
                    </h2>
                    <p v-if="trade?.title" class="truncate text-sm text-gray-500 dark:text-gray-400">{{ trade.title }}</p>
                </div>
                <button
                    ref="closeButton"
                    type="button"
                    class="rounded-lg border border-gray-200 px-3 py-1.5 text-sm font-medium text-gray-700 hover:bg-gray-50 dark:border-gray-700 dark:text-gray-200 dark:hover:bg-white/5"
                    data-testid="close-trade"
                    @click="close"
                >
                    Close
                </button>
            </header>

            <div v-if="status === 'loading' && !trade" class="px-5 py-10" role="status">
                <div class="h-6 w-40 animate-pulse rounded bg-gray-100 dark:bg-gray-800" />
                <p class="mt-4 text-sm text-gray-500 dark:text-gray-400">Loading trade</p>
            </div>
            <div v-else-if="status === 'error'" class="px-5 py-10" role="alert">
                <h3 class="text-base font-semibold text-gray-900 dark:text-white">Could not load this trade</h3>
                <p class="mt-2 text-sm text-gray-500 dark:text-gray-400">{{ error || 'Try again in a moment.' }}</p>
                <button type="button" class="mt-4 rounded-lg bg-brand-500 px-3 py-1.5 text-sm font-medium text-white" @click="load(tradeId)">Try again</button>
            </div>
            <div v-else-if="trade" class="space-y-6 px-5 py-5">
                <dl class="grid grid-cols-2 gap-4 sm:grid-cols-3">
                    <div>
                        <dt class="text-xs font-medium tracking-wide text-gray-400 uppercase">Wallet</dt>
                        <dd class="mt-1 text-sm text-gray-800 dark:text-gray-100">{{ trade.walletName || trade.wallet }}</dd>
                        <dd class="text-xs text-gray-400">{{ modeLabel(trade.mode) }}</dd>
                    </div>
                    <div>
                        <dt class="text-xs font-medium tracking-wide text-gray-400 uppercase">Side</dt>
                        <dd class="mt-1 text-sm font-medium" :class="trade.side === 'buy' ? 'text-success-600' : 'text-error-500'">
                            {{ sideLabel(trade.side) }} {{ formatQty(trade.qty) }}
                        </dd>
                    </div>
                    <div>
                        <dt class="text-xs font-medium tracking-wide text-gray-400 uppercase">Kind</dt>
                        <dd class="mt-1 text-sm text-gray-800 dark:text-gray-100" data-testid="trade-kind">{{ tradeKindLabel(trade.kind) }}</dd>
                    </div>
                    <div>
                        <dt class="text-xs font-medium tracking-wide text-gray-400 uppercase">Time</dt>
                        <dd class="mt-1 text-sm text-gray-800 dark:text-gray-100"><TimeCell :value="trade.executedAt" /></dd>
                    </div>
                    <div>
                        <dt class="text-xs font-medium tracking-wide text-gray-400 uppercase">Buy price</dt>
                        <dd class="mt-1 text-sm text-gray-800 dark:text-gray-100" data-testid="buy-price">{{ formatPrice(trade.buyPrice) }}</dd>
                    </div>
                    <div>
                        <dt class="text-xs font-medium tracking-wide text-gray-400 uppercase">{{ priceKindTitle(trade.priceKind) }}</dt>
                        <dd class="mt-1 text-sm text-gray-800 dark:text-gray-100" data-testid="mark-price">
                            <template v-if="trade.currentOrExitPrice != null">{{ formatPrice(trade.currentOrExitPrice) }}</template>
                            <span v-else class="text-gray-400">-</span>
                        </dd>
                    </div>
                    <div>
                        <dt class="text-xs font-medium tracking-wide text-gray-400 uppercase">Fees</dt>
                        <dd class="mt-1 text-sm" data-testid="trade-fees"><MoneyCell :value="trade.fee" :signed="false" /></dd>
                    </div>
                    <div>
                        <dt class="text-xs font-medium tracking-wide text-gray-400 uppercase">Realized P&L</dt>
                        <dd class="mt-1 text-sm" data-testid="realized-pnl"><AmountCell :value="trade.realizedPnl" /></dd>
                    </div>
                    <div>
                        <dt class="text-xs font-medium tracking-wide text-gray-400 uppercase">Unrealized P&L</dt>
                        <dd class="mt-1 text-sm" data-testid="unrealized-pnl"><AmountCell :value="trade.unrealizedPnl" /></dd>
                    </div>
                    <div>
                        <dt class="text-xs font-medium tracking-wide text-gray-400 uppercase">P&L</dt>
                        <dd class="mt-1 text-sm">
                            <AmountCell :value="trade.pnl" />
                            <span v-if="trade.pnlPercent == null" class="ms-2 text-gray-400">-</span>
                            <PctCell v-else class="ms-2" :value="trade.pnlPercent" />
                        </dd>
                    </div>
                    <div>
                        <dt class="text-xs font-medium tracking-wide text-gray-400 uppercase">Matched cost</dt>
                        <dd class="mt-1 text-sm"><MoneyCell :value="matchedCost" :signed="false" /></dd>
                    </div>
                    <div>
                        <dt class="text-xs font-medium tracking-wide text-gray-400 uppercase">Proceeds</dt>
                        <dd class="mt-1 text-sm"><MoneyCell :value="proceeds" :signed="false" /></dd>
                    </div>
                    <div v-if="trade.targetPrice != null">
                        <dt class="text-xs font-medium tracking-wide text-gray-400 uppercase">Target</dt>
                        <dd class="mt-1 text-sm text-gray-800 dark:text-gray-100" data-testid="target-price">{{ formatPrice(trade.targetPrice) }}</dd>
                    </div>
                    <div>
                        <dt class="text-xs font-medium tracking-wide text-gray-400 uppercase">Status</dt>
                        <dd class="mt-1 text-sm text-gray-800 dark:text-gray-100">{{ statusLabel(trade.status) }}</dd>
                    </div>
                </dl>

                <p v-if="trade.unrealizedPnl == null && trade.status === 'open'" class="text-sm text-gray-500 dark:text-gray-400">
                    No price mark, so unrealized P&L is unknown.
                </p>

                <section>
                    <h3 class="text-sm font-semibold text-gray-800 dark:text-white/90">Note</h3>
                    <p v-if="trade.note" class="mt-2 text-sm text-gray-700 dark:text-gray-200" data-testid="trade-note">{{ trade.note }}</p>
                    <p v-else class="mt-2 text-sm text-gray-400">No note recorded</p>
                </section>

                <section
                    data-testid="trade-chart"
                    :data-empty="chart.empty ? 'true' : 'false'"
                    :data-points="chartPointsAttr(chart.points)"
                    :data-markers="chartMarkersAttr(chart.markers)"
                >
                    <h3 class="text-sm font-semibold text-gray-800 dark:text-white/90">Price</h3>
                    <p
                        v-if="chart.empty"
                        class="mt-3 rounded-xl bg-gray-50 px-4 py-10 text-center text-sm text-gray-500 dark:bg-white/[0.03] dark:text-gray-400"
                    >
                        No price marks recorded
                    </p>
                    <div v-else class="mt-3">
                        <PriceChart :points="chart.points" :markers="chart.markers" :dark="isDark" />
                    </div>
                </section>

                <section data-testid="trade-fills">
                    <h3 class="text-sm font-semibold text-gray-800 dark:text-white/90">Fills</h3>
                    <div class="mt-3 overflow-x-auto">
                        <table class="w-full min-w-[720px] text-sm">
                            <thead>
                                <tr class="border-b border-gray-100 text-left text-xs font-medium tracking-wide text-gray-500 uppercase dark:border-gray-800">
                                    <th class="py-2 pe-3 font-medium">Time</th>
                                    <th class="py-2 pe-3 font-medium">Side</th>
                                    <th class="py-2 pe-3 text-end font-medium">Qty</th>
                                    <th class="py-2 pe-3 text-end font-medium">Price</th>
                                    <th class="py-2 pe-3 text-end font-medium">Buy fee</th>
                                    <th class="py-2 pe-3 text-end font-medium">Sell fee</th>
                                    <th class="py-2 pe-3 text-end font-medium">Cost</th>
                                    <th class="py-2 pe-3 text-end font-medium">Proceeds</th>
                                    <th class="py-2 text-end font-medium">Realized</th>
                                </tr>
                            </thead>
                            <tbody>
                                <tr
                                    v-for="(fill, index) in fills"
                                    :key="`${fill.tradeId}-${index}`"
                                    class="border-b border-gray-50 last:border-0 dark:border-gray-800"
                                    data-testid="trade-fill"
                                >
                                    <td class="py-2 pe-3 whitespace-nowrap"><TimeCell :value="fill.executedAt" /></td>
                                    <td class="py-2 pe-3">{{ sideLabel(fill.side) }}</td>
                                    <td class="py-2 pe-3 text-end tabular-nums">{{ formatQty(fill.qty) }}</td>
                                    <td class="py-2 pe-3 text-end tabular-nums">{{ formatPrice(fill.price) }}</td>
                                    <td class="py-2 pe-3 text-end"><MoneyCell :value="fill.buyFee" :signed="false" /></td>
                                    <td class="py-2 pe-3 text-end"><MoneyCell :value="fill.sellFee" :signed="false" /></td>
                                    <td class="py-2 pe-3 text-end"><MoneyCell :value="fill.cost" :signed="false" /></td>
                                    <td class="py-2 pe-3 text-end"><MoneyCell :value="fill.proceeds" :signed="false" /></td>
                                    <td class="py-2 text-end"><AmountCell :value="fill.realizedPnl" /></td>
                                </tr>
                            </tbody>
                        </table>
                    </div>
                </section>
            </div>
        </aside>
    </div>
</template>
