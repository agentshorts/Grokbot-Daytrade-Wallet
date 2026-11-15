<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import type { PredictionPnlResponse, PredictionPnlRow } from '@/api'
import HideClosedContracts from '@/components/HideClosedContracts.vue'
import MoneyCell from '@/components/MoneyCell.vue'
import StatePanel from '@/components/shell/StatePanel.vue'
import { explainLedgerError, useLedger } from '@/composables/useLedger'
import { useHideClosedContracts } from '@/composables/useHideClosedContracts'
import { formatQty } from '@/lib/format'
import { contractName, filterClosedContracts, isContractClosed, readContractTicker, viewerTimeZone } from '@/lib/predictionContract'
import { settlementLabel } from '@/lib/predictionView'
import { REFRESH_MS, startPolling } from '@/lib/refresh'

const client = useLedger()
const status = ref<'loading' | 'ready' | 'error'>('loading')
const error = ref<string | null>(null)
const model = ref<PredictionPnlResponse | null>(null)
const hideClosed = useHideClosedContracts()
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
const closedCount = computed(() => (model.value?.wallets ?? []).filter(rowClosed).length)
const groups = computed(() => {
    const assets = [...new Set(shownRows.value.map(rowAsset))].sort((a, b) => a.localeCompare(b))
    return assets.map((asset) => ({
        asset,
        rows: shownRows.value
            .filter((row) => rowAsset(row) === asset)
            .slice()
            .sort((a, b) => (a.closeAt ?? '').localeCompare(b.closeAt ?? '') || rowName(a).localeCompare(rowName(b))),
    }))
})
const shownTotal = computed(() => {
    const rows = shownRows.value
    return {
        wallets: rows.length,
        bought: rows.reduce((sum, row) => sum + row.bought, 0),
        sold: rows.reduce((sum, row) => sum + row.sold, 0),
        fees: rows.reduce((sum, row) => sum + row.fees, 0),
        realizedPnl: rows.reduce((sum, row) => sum + row.realizedPnl, 0),
    }
})

async function refresh() {
    try {
        model.value = await client.predictions()
        status.value = 'ready'
        error.value = null
    } catch (err) {
        error.value = explainLedgerError(err).message
        if (!model.value) status.value = 'error'
    }
}

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
        <div>
            <h1 class="text-xl font-semibold text-gray-900 dark:text-white/90">Prediction markets</h1>
            <p class="mt-1 text-sm text-gray-500 dark:text-gray-400">
                Realized P&L for each contract wallet, and the total across them. Fees are included in the P&L.
            </p>
        </div>

        <StatePanel v-if="status === 'loading' && !model" tone="loading" title="Loading prediction P&L" />
        <StatePanel v-else-if="status === 'error' && !model" tone="error" title="Could not load prediction P&L" :body="error || 'Try again in a moment.'">
            <button type="button" class="rounded-lg bg-brand-500 px-3 py-1.5 text-sm font-medium text-white" @click="refresh()">Try again</button>
        </StatePanel>
        <StatePanel v-else-if="model && model.wallets.length === 0" title="No prediction wallets" body="A pm- wallet recorded through MCP will show up here." />
        <div v-else-if="model" class="space-y-3">
            <div v-if="closedCount > 0" class="flex justify-end">
                <HideClosedContracts v-model="hideClosed" :count="closedCount" />
            </div>
            <p v-if="shownRows.length === 0" class="text-sm text-gray-500 dark:text-gray-400">Closed contracts are hidden.</p>
            <section v-else class="overflow-x-auto rounded-2xl border border-gray-200 bg-white dark:border-gray-800 dark:bg-white/[0.03]">
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
        </div>
    </div>
</template>
