<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import type { PredictionBookingResponse, PredictionPnlResponse, PredictionPnlRow } from '@/api'
import HideClosedContracts from '@/components/HideClosedContracts.vue'
import OscillationView from '@/components/OscillationView.vue'
import MoneyCell from '@/components/MoneyCell.vue'
import CollapseSection from '@/components/shell/CollapseSection.vue'
import StatePanel from '@/components/shell/StatePanel.vue'
import TextFilter from '@/components/shell/TextFilter.vue'
import { explainLedgerError, useLedger } from '@/composables/useLedger'
import { useHideClosedContracts } from '@/composables/useHideClosedContracts'
import { useShowArchived, useViewFilter } from '@/composables/useUiState'
import { formatQty, formatTimeCt } from '@/lib/format'
import { contractName, filterClosedContracts, isContractClosed, readContractTicker, viewerTimeZone } from '@/lib/predictionContract'
import { settlementLabel } from '@/lib/predictionView'
import { filterRows } from '@/lib/textFilter'
import { REFRESH_MS, startPolling } from '@/lib/refresh'
import { includeArchivedQuery } from '@/lib/visibleWallets'

const client = useLedger()
const route = useRoute()
const oscillation = computed(() => route.query.view === 'oscillation')
const status = ref<'loading' | 'ready' | 'error'>('loading')
const error = ref<string | null>(null)
const model = ref<PredictionPnlResponse | null>(null)
const bookings = ref<PredictionBookingResponse | null>(null)
const bookingError = ref<string | null>(null)
const hideClosed = useHideClosedContracts()
const filterText = useViewFilter('predictions')
const showArchived = useShowArchived()
let stop = () => {}

function rowClosed(row: PredictionPnlRow): boolean {
    return isContractClosed({ settlement: row.settlement, closeAt: row.closeAt, now: new Date() })
}

function rowName(row: PredictionPnlRow): string {
    return contractName([row.wallet, row.symbol, row.name], row.closeAt ?? null, viewerTimeZone(), row.name)
}

function rowAsset(row: PredictionPnlRow): string {
    return readContractTicker(row.wallet, row.symbol, row.name)?.asset ?? 'Contracts'
}

const shownRows = computed(() => filterClosedContracts(model.value?.wallets ?? [], hideClosed.value, rowClosed))
const matchedRows = computed(() =>
    filterRows(shownRows.value, filterText.value, (row) => [row.wallet, row.name, row.symbol, row.settlement, rowAsset(row), rowName(row)]),
)
const closedCount = computed(() => (model.value?.wallets ?? []).filter(rowClosed).length)
const groups = computed(() => {
    const assets = [...new Set(matchedRows.value.map(rowAsset))].sort((a, b) => a.localeCompare(b))
    return assets.map((asset) => ({
        asset,
        rows: matchedRows.value
            .filter((row) => rowAsset(row) === asset)
            .slice()
            .sort((a, b) => (a.closeAt ?? '').localeCompare(b.closeAt ?? '') || rowName(a).localeCompare(rowName(b))),
    }))
})
const bookingRows = computed(() => bookings.value?.results ?? [])
const visibleBookings = computed(() =>
    filterRows(bookingRows.value, filterText.value, (row) => [row.productId, row.wallet, row.status, row.detail]),
)
const shownTotal = computed(() => {
    const rows = matchedRows.value
    return {
        wallets: rows.length,
        bought: rows.reduce((sum, row) => sum + row.bought, 0),
        sold: rows.reduce((sum, row) => sum + row.sold, 0),
        fees: rows.reduce((sum, row) => sum + row.fees, 0),
        realizedPnl: rows.reduce((sum, row) => sum + row.realizedPnl, 0),
    }
})

function bookingLabel(status: string): string {
    if (status === 'booked') return 'Booked'
    if (status === 'problem') return 'Problem'
    return 'Seen'
}

async function refresh() {
    try {
        model.value = await client.predictions(includeArchivedQuery(showArchived.value))
        status.value = 'ready'
        error.value = null
    } catch (err) {
        error.value = explainLedgerError(err).message
        if (!model.value) status.value = 'error'
    }
    if (!client.predictionBookings) return
    try {
        bookings.value = await client.predictionBookings()
        bookingError.value = null
    } catch (err) {
        bookingError.value = explainLedgerError(err).message
    }
}

watch(showArchived, () => {
    void refresh()
})

onMounted(() => {
    void refresh()
    stop = startPolling(() => {
        void refresh()
    }, REFRESH_MS)
})
onUnmounted(() => stop())
</script>

<template>
    <div data-testid="predictions" class="space-y-6">
        <div class="flex flex-wrap items-start justify-between gap-3">
            <div>
                <h1 class="text-xl font-semibold text-gray-900 dark:text-white/90">{{ oscillation ? 'Oscillation' : 'Prediction markets' }}</h1>
                <p v-if="!oscillation" class="mt-1 text-sm text-gray-500 dark:text-gray-400">
                    Realized P&L for each contract wallet, and the total across them. Fees are included in the P&L.
                </p>
            </div>
            <RouterLink
                v-if="!oscillation"
                data-testid="open-oscillation"
                class="rounded-lg border border-gray-200 px-3 py-1.5 text-sm font-medium text-gray-700 hover:bg-gray-50 dark:border-gray-700 dark:text-gray-200 dark:hover:bg-white/5"
                :to="{ path: '/predictions', query: { view: 'oscillation' } }"
            >
                Oscillation
            </RouterLink>
            <RouterLink
                v-else
                data-testid="close-oscillation"
                class="rounded-lg border border-gray-200 px-3 py-1.5 text-sm font-medium text-gray-700 hover:bg-gray-50 dark:border-gray-700 dark:text-gray-200 dark:hover:bg-white/5"
                to="/predictions"
            >
                P&L
            </RouterLink>
        </div>

        <OscillationView v-if="oscillation" />
        <StatePanel v-else-if="status === 'loading' && !model" tone="loading" title="Loading prediction P&L" />
        <StatePanel v-else-if="status === 'error' && !model" tone="error" title="Could not load prediction P&L" :body="error || 'Try again in a moment.'">
            <button type="button" class="rounded-lg bg-brand-500 px-3 py-1.5 text-sm font-medium text-white" @click="refresh()">Try again</button>
        </StatePanel>
        <StatePanel v-else-if="model && model.wallets.length === 0" title="No prediction wallets" body="A Coinbase prediction fill, or a pm- wallet, shows up here." />
        <div v-else-if="model" class="space-y-3">
            <div class="flex flex-wrap items-end justify-between gap-3">
                <TextFilter v-model="filterText" view="predictions" placeholder="Filter by contract, ticker, wallet, or settlement" />
                <HideClosedContracts v-if="closedCount > 0" v-model="hideClosed" :count="closedCount" />
            </div>
            <p v-if="shownRows.length === 0" class="text-sm text-gray-500 dark:text-gray-400">Closed contracts are hidden.</p>
            <p
                v-else-if="filterText.trim() && matchedRows.length === 0"
                class="text-sm text-gray-500 dark:text-gray-400"
                data-testid="filter-empty-predictions"
            >
                Nothing matches this filter.
            </p>
            <CollapseSection v-else id="predictions-table" label="contracts" data-testid="prediction-table-section">
                <template #heading>
                    <h2 class="text-sm font-semibold text-gray-900 dark:text-white">Contracts</h2>
                </template>
                <section class="overflow-x-auto rounded-2xl border border-gray-200 bg-white dark:border-gray-800 dark:bg-white/[0.03]">
                    <table class="min-w-full text-sm" data-testid="prediction-table">
                        <thead class="text-start text-gray-500 dark:text-gray-400">
                            <tr>
                                <th class="px-4 py-3 font-medium">Contract</th>
                                <th class="px-4 py-3 font-medium">Bought</th>
                                <th class="px-4 py-3 font-medium">Sold</th>
                                <th class="px-4 py-3 font-medium">Fees</th>
                                <th class="px-4 py-3 font-medium">Realized P&L</th>
                                <th class="px-4 py-3 font-medium">Settlement</th>
                            </tr>
                        </thead>
                        <tbody>
                            <template v-for="group in groups" :key="group.asset">
                                <tr v-if="groups.length > 1 || group.asset !== 'Contracts'" class="border-t border-gray-100 dark:border-gray-800">
                                    <td colspan="6" class="px-4 py-2 text-xs font-semibold tracking-wide text-gray-500 uppercase dark:text-gray-400">
                                        {{ group.asset }}
                                    </td>
                                </tr>
                                <tr
                                    v-for="row in group.rows"
                                    :key="row.wallet"
                                    class="border-t border-gray-100 dark:border-gray-800"
                                    :data-testid="`prediction-${row.wallet}`"
                                >
                                    <td class="px-4 py-3">
                                        <RouterLink
                                            :to="`/wallet/${encodeURIComponent(row.wallet)}`"
                                            class="font-medium text-gray-900 hover:text-brand-600 dark:text-white/90"
                                            :title="row.symbol || row.wallet"
                                        >
                                            {{ rowName(row) }}
                                        </RouterLink>
                                        <p class="text-xs text-gray-500 dark:text-gray-400" :title="row.symbol || row.wallet">{{ row.symbol || row.wallet }}</p>
                                    </td>
                                    <td class="px-4 py-3 tabular-nums">{{ formatQty(row.bought) }}</td>
                                    <td class="px-4 py-3 tabular-nums">{{ formatQty(row.sold) }}</td>
                                    <td class="px-4 py-3"><MoneyCell :value="row.fees" :signed="false" /></td>
                                    <td class="px-4 py-3"><MoneyCell :value="row.realizedPnl" /></td>
                                    <td class="px-4 py-3">{{ settlementLabel(row.settlement) }}</td>
                                </tr>
                            </template>
                        </tbody>
                        <tfoot class="border-t border-gray-200 font-medium dark:border-gray-700" data-testid="prediction-total">
                            <tr>
                                <td class="px-4 py-3">Total ({{ shownTotal.wallets }})</td>
                                <td class="px-4 py-3 tabular-nums">{{ formatQty(shownTotal.bought) }}</td>
                                <td class="px-4 py-3 tabular-nums">{{ formatQty(shownTotal.sold) }}</td>
                                <td class="px-4 py-3"><MoneyCell :value="shownTotal.fees" :signed="false" /></td>
                                <td class="px-4 py-3"><MoneyCell :value="shownTotal.realizedPnl" /></td>
                                <td class="px-4 py-3"></td>
                            </tr>
                        </tfoot>
                    </table>
                </section>
            </CollapseSection>
        </div>
        <div v-if="!oscillation && (bookingRows.length || bookingError)" class="space-y-3" data-testid="prediction-booking">
            <TextFilter
                v-if="!model || model.wallets.length === 0"
                v-model="filterText"
                view="predictions"
                placeholder="Filter by contract, ticker, wallet, or settlement"
            />
            <p v-if="bookingError" class="text-sm text-error-600 dark:text-error-400" data-testid="prediction-booking-error">{{ bookingError }}</p>
            <p
                v-else-if="filterText.trim() && visibleBookings.length === 0"
                class="text-sm text-gray-500 dark:text-gray-400"
                data-testid="filter-empty-booking"
            >
                Nothing matches this filter.
            </p>
            <CollapseSection v-else id="prediction-booking" label="contract booking" data-testid="prediction-booking-section">
                <template #heading>
                    <h2 class="text-sm font-semibold text-gray-900 dark:text-white">Contract booking</h2>
                </template>
                <section class="overflow-x-auto rounded-2xl border border-gray-200 bg-white dark:border-gray-800 dark:bg-white/[0.03]">
                    <table class="min-w-full text-sm" data-testid="prediction-booking-table">
                        <thead class="text-start text-gray-500 dark:text-gray-400">
                            <tr>
                                <th class="px-4 py-3 font-medium">Product</th>
                                <th class="px-4 py-3 font-medium">Status</th>
                                <th class="px-4 py-3 font-medium">Wallet</th>
                                <th class="px-4 py-3 font-medium">Last seen</th>
                                <th class="px-4 py-3 font-medium">Detail</th>
                            </tr>
                        </thead>
                        <tbody>
                            <tr
                                v-for="row in visibleBookings"
                                :key="row.productId"
                                class="border-t border-gray-100 dark:border-gray-800"
                                :data-testid="`booking-${row.productId}`"
                            >
                                <td class="px-4 py-3 font-medium text-gray-900 dark:text-white/90">{{ row.productId }}</td>
                                <td class="px-4 py-3">{{ bookingLabel(row.status) }}</td>
                                <td class="px-4 py-3">{{ row.wallet || '' }}</td>
                                <td class="px-4 py-3">{{ formatTimeCt(row.lastSeen) }}</td>
                                <td class="px-4 py-3">{{ row.detail || '' }}</td>
                            </tr>
                        </tbody>
                    </table>
                </section>
            </CollapseSection>
        </div>
    </div>
</template>
