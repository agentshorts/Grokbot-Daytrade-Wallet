<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import type { OscillationResponse } from '@/api'
import OscillationFields from '@/components/OscillationFields.vue'
import OscillationTable from '@/components/OscillationTable.vue'
import CollapseSection from '@/components/shell/CollapseSection.vue'
import StatePanel from '@/components/shell/StatePanel.vue'
import TextFilter from '@/components/shell/TextFilter.vue'
import { explainLedgerError, useLedger } from '@/composables/useLedger'
import { useViewFilter } from '@/composables/useUiState'
import { readOscillationPrefs, writeOscillationPrefs, type OscillationPrefs } from '@/lib/oscillationPrefs'
import { browserStorage } from '@/lib/predictionContract'
import { REFRESH_MS, startPolling } from '@/lib/refresh'
import { filterRows } from '@/lib/textFilter'

const client = useLedger()
const prefs = ref<OscillationPrefs>(readOscillationPrefs(browserStorage()))
const filterText = useViewFilter('oscillation')
const ticker = ref('')
const activeTicker = ref('')
const popover = ref(false)
const popoverRoot = ref<HTMLElement | null>(null)
const status = ref<'loading' | 'ready' | 'error'>('loading')
const error = ref<string | null>(null)
const model = ref<OscillationResponse | null>(null)
let stop = () => {}

const rows = computed(() => model.value?.results ?? [])
const matchedRows = computed(() =>
    filterRows(rows.value, filterText.value, (row) => [row.ticker, row.productId, row.wallet, row.name, row.series, row.yesLabel, row.noLabel]),
)

function save(next: OscillationPrefs) {
    prefs.value = next
    writeOscillationPrefs(browserStorage(), next)
    void refresh()
}

function lookup() {
    activeTicker.value = ticker.value.trim()
    void refresh()
}

function clearLookup() {
    ticker.value = ''
    activeTicker.value = ''
    void refresh()
}

async function refresh() {
    try {
        model.value = await client.oscillation({
            lookbackHours: prefs.value.lookbackHours,
            thresholdCents: prefs.value.thresholdCents,
            ticker: activeTicker.value || undefined,
        })
        status.value = 'ready'
        error.value = null
    } catch (err) {
        error.value = explainLedgerError(err).message
        if (!model.value) status.value = 'error'
    }
}

function onPointerDown(event: PointerEvent) {
    if (!popover.value || !popoverRoot.value) return
    if (event.target instanceof Node && popoverRoot.value.contains(event.target)) return
    // Close after the threshold field commits its change. Blur runs after pointerdown.
    setTimeout(() => {
        popover.value = false
    }, 0)
}

onMounted(() => {
    document.addEventListener('pointerdown', onPointerDown)
    void refresh()
    stop = startPolling(() => {
        void refresh()
    }, REFRESH_MS)
})
onUnmounted(() => {
    document.removeEventListener('pointerdown', onPointerDown)
    stop()
})
</script>

<template>
    <div data-testid="oscillation" class="space-y-4">
        <CollapseSection id="oscillation-lookup" label="lookup">
            <template #heading>
                <h2 class="text-sm font-semibold text-gray-900 dark:text-white">Lookup</h2>
            </template>
            <div class="flex flex-wrap items-end justify-between gap-3">
                <p class="max-w-2xl text-sm text-gray-500 dark:text-gray-400">
                    Open pm- contracts, most reversals first. YES and NO are the live quote. A contract is flagged when price changes direction at least twice
                    by the threshold. Close times are Central. The hour printed on the ticker is Eastern.
                </p>
                <div ref="popoverRoot" class="relative">
                    <button
                        type="button"
                        data-testid="oscillation-inputs"
                        class="rounded-lg border border-gray-200 px-3 py-1.5 text-sm font-medium text-gray-700 dark:border-gray-700 dark:text-gray-200"
                        :aria-expanded="popover"
                        @click="popover = !popover"
                    >
                        {{ prefs.lookbackHours }}h, {{ prefs.thresholdCents }}c
                    </button>
                    <div
                        v-if="popover"
                        data-testid="oscillation-popover"
                        class="absolute end-0 top-full z-30 mt-1 w-56 rounded-xl border border-gray-200 bg-white p-3 shadow-lg dark:border-gray-700 dark:bg-gray-900"
                        @click.stop
                    >
                        <OscillationFields :lookback-hours="prefs.lookbackHours" :threshold-cents="prefs.thresholdCents" @change="save" />
                    </div>
                </div>
            </div>

            <form class="flex flex-wrap items-end gap-2" @submit.prevent="lookup">
                <label class="min-w-0 flex-1 text-xs font-medium text-gray-500">
                    Ticker or product id
                    <input
                        v-model="ticker"
                        data-testid="oscillation-search"
                        class="mt-1 w-full rounded-lg border border-gray-200 bg-white px-3 py-2 text-sm dark:border-gray-700 dark:bg-gray-950"
                        placeholder="KXBTCD-26NOV1617-T64999.99-KALSHI"
                        autocomplete="off"
                    />
                </label>
                <button type="submit" data-testid="oscillation-lookup-submit" class="rounded-lg bg-brand-500 px-3 py-2 text-sm font-medium text-white">
                    Look up
                </button>
                <button v-if="activeTicker" type="button" class="rounded-lg px-3 py-2 text-sm text-gray-600 dark:text-gray-300" @click="clearLookup">
                    Clear
                </button>
            </form>
        </CollapseSection>

        <TextFilter v-model="filterText" view="oscillation" placeholder="Filter by ticker, wallet, or name" />

        <StatePanel v-if="status === 'loading' && !model" tone="loading" title="Loading oscillation" />
        <StatePanel v-else-if="status === 'error' && !model" tone="error" title="Could not load oscillation" :body="error || 'Try again in a moment.'">
            <button type="button" class="rounded-lg bg-brand-500 px-3 py-1.5 text-sm font-medium text-white" @click="refresh()">Try again</button>
        </StatePanel>
        <template v-else-if="model">
            <p v-if="model.lookupError" data-testid="oscillation-lookup-error" class="text-sm text-error-600" role="alert">
                {{ model.lookupError }}
            </p>
            <section v-if="model.lookup" data-testid="oscillation-lookup" class="space-y-2">
                <h2 class="text-sm font-semibold text-gray-900 dark:text-white">Lookup</h2>
                <OscillationTable :rows="[model.lookup]" :lookback-hours="prefs.lookbackHours" :threshold-cents="prefs.thresholdCents" />
            </section>
            <p v-if="rows.length === 0" class="text-sm text-gray-500 dark:text-gray-400">
                No open prediction contracts. Look up a ticker to analyze one that is not in a wallet.
            </p>
            <p v-else-if="matchedRows.length === 0" class="text-sm text-gray-500 dark:text-gray-400" data-testid="filter-empty-oscillation">
                Nothing matches this filter.
            </p>
            <OscillationTable v-else :rows="matchedRows" :lookback-hours="prefs.lookbackHours" :threshold-cents="prefs.thresholdCents" />
        </template>
    </div>
</template>
