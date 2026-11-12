<script setup lang="ts">
import MoneyCell from '@/components/MoneyCell.vue'
import PctCell from '@/components/PctCell.vue'
import TimeCell from '@/components/TimeCell.vue'
import { formatPrice, formatQty } from '@/lib/format'
import { DASH, type WalletPositionView } from '@/lib/walletView'

defineProps<{
    open: WalletPositionView[]
    closed: WalletPositionView[]
    currency?: string
    staleAfterMinutes?: number
}>()
</script>

<template>
    <div class="space-y-6">
        <section
            data-testid="open-positions"
            class="min-w-0 overflow-hidden rounded-2xl border border-gray-200 bg-white dark:border-gray-800 dark:bg-white/[0.03]"
        >
            <div class="px-4 py-4 md:px-5">
                <h2 class="text-base font-semibold text-gray-800 dark:text-white/90">Open positions</h2>
                <p class="mt-1 text-sm text-gray-500 dark:text-gray-400">
                    A mark older than {{ staleAfterMinutes ?? 15 }} minutes is stale. Unrealized P&L is a dash when the position has no mark.
                </p>
            </div>
            <p v-if="open.length === 0" class="px-5 py-8 text-sm text-gray-500 dark:text-gray-400">No open positions.</p>
            <div v-else class="max-w-full overflow-x-auto">
                <table class="w-full text-sm">
                    <thead>
                        <tr
                            class="border-b border-gray-100 text-left text-xs font-medium tracking-wide text-gray-500 uppercase dark:border-gray-800 dark:text-gray-400"
                        >
                            <th class="px-4 py-3 font-medium">Symbol</th>
                            <th class="px-4 py-3 text-end font-medium">Qty</th>
                            <th class="px-4 py-3 text-end font-medium">Avg price</th>
                            <th class="px-4 py-3 text-end font-medium">Last mark</th>
                            <th class="px-4 py-3 font-medium">Marked</th>
                            <th class="px-4 py-3 text-end font-medium">Unrealized</th>
                            <th class="px-4 py-3 text-end font-medium">Cost</th>
                            <th class="px-4 py-3 text-end font-medium">Target</th>
                            <th class="px-4 py-3 text-end font-medium">Distance</th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr v-for="position in open" :key="position.symbol" class="border-b border-gray-50 last:border-0 dark:border-gray-800/80">
                            <td class="px-4 py-3 font-medium whitespace-nowrap text-gray-900 dark:text-white">{{ position.symbol }}</td>
                            <td class="px-4 py-3 text-end tabular-nums">{{ formatQty(position.qty) }}</td>
                            <td class="px-4 py-3 text-end tabular-nums">{{ formatPrice(position.avgPrice, currency) }}</td>
                            <td class="px-4 py-3 text-end">
                                <span v-if="position.lastMark == null" class="text-warning-600 dark:text-warning-400">No price mark</span>
                                <MoneyCell v-else :value="position.lastMark" :signed="false" :currency="currency" />
                            </td>
                            <td class="px-4 py-3 whitespace-nowrap text-gray-500 dark:text-gray-400">
                                <span
                                    v-if="position.stale"
                                    class="me-2 rounded-full bg-warning-50 px-2 py-0.5 text-xs font-medium text-warning-700 dark:bg-warning-500/15 dark:text-warning-400"
                                >
                                    Stale
                                </span>
                                <TimeCell v-if="position.lastMarkAt" :value="position.lastMarkAt" />
                                <span v-else>{{ DASH }}</span>
                            </td>
                            <td class="px-4 py-3 text-end" data-testid="unrealized">
                                <span v-if="position.unrealizedPnl == null" class="text-gray-400">{{ DASH }}</span>
                                <span v-else class="inline-flex items-center justify-end gap-2">
                                    <MoneyCell :value="position.unrealizedPnl" :currency="currency" />
                                    <PctCell :value="position.unrealizedPnlPercent" />
                                </span>
                            </td>
                            <td class="px-4 py-3 text-end"><MoneyCell :value="position.costBasis" :signed="false" :currency="currency" /></td>
                            <td class="px-4 py-3 text-end">
                                <MoneyCell v-if="position.targetPrice != null" :value="position.targetPrice" :signed="false" :currency="currency" />
                                <span v-else class="text-gray-400">{{ DASH }}</span>
                            </td>
                            <td class="px-4 py-3 text-end" data-testid="distance">
                                <span v-if="position.targetPrice == null || position.distanceToTarget == null" class="text-gray-400">{{ DASH }}</span>
                                <span v-else class="inline-flex items-center justify-end gap-2">
                                    <MoneyCell :value="position.distanceToTarget" :currency="currency" />
                                    <PctCell :value="position.distanceToTargetPercent" />
                                </span>
                            </td>
                        </tr>
                    </tbody>
                </table>
            </div>
        </section>

        <section
            data-testid="closed-positions"
            class="min-w-0 overflow-hidden rounded-2xl border border-gray-200 bg-white dark:border-gray-800 dark:bg-white/[0.03]"
        >
            <div class="px-4 py-4 md:px-5">
                <h2 class="text-base font-semibold text-gray-800 dark:text-white/90">Closed positions</h2>
                <p class="mt-1 text-sm text-gray-500 dark:text-gray-400">Realized P&L after fees for symbols that are flat.</p>
            </div>
            <p v-if="closed.length === 0" class="px-5 py-8 text-sm text-gray-500 dark:text-gray-400">No closed positions.</p>
            <div v-else class="max-w-full overflow-x-auto">
                <table class="w-full text-sm">
                    <thead>
                        <tr
                            class="border-b border-gray-100 text-left text-xs font-medium tracking-wide text-gray-500 uppercase dark:border-gray-800 dark:text-gray-400"
                        >
                            <th class="px-4 py-3 font-medium">Symbol</th>
                            <th class="px-4 py-3 text-end font-medium">Qty</th>
                            <th class="px-4 py-3 text-end font-medium">Realized</th>
                            <th class="px-4 py-3 text-end font-medium">Fees</th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr v-for="position in closed" :key="position.symbol" class="border-b border-gray-50 last:border-0 dark:border-gray-800/80">
                            <td class="px-4 py-3 font-medium text-gray-900 dark:text-white">{{ position.symbol }}</td>
                            <td class="px-4 py-3 text-end tabular-nums">{{ formatQty(position.qty) }}</td>
                            <td class="px-4 py-3 text-end"><MoneyCell :value="position.realizedPnl" :currency="currency" /></td>
                            <td class="px-4 py-3 text-end"><MoneyCell :value="position.feesPaid" :signed="false" :currency="currency" /></td>
                        </tr>
                    </tbody>
                </table>
            </div>
        </section>
    </div>
</template>
