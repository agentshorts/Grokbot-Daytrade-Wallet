<script setup lang="ts">
import MoneyCell from '@/components/MoneyCell.vue'
import PctCell from '@/components/PctCell.vue'
import TimeCell from '@/components/TimeCell.vue'
import type { PositionRecord } from '@/lib/book'
import { formatPrice, formatQty, UNKNOWN_AMOUNT } from '@/lib/format'

defineProps<{ positions: PositionRecord[] }>()

function pnlPct(position: PositionRecord): number | null {
    if (position.unrealizedPnl == null || position.avgPrice === 0) return null
    return (position.unrealizedPnl / (position.avgPrice * position.qty)) * 100
}
</script>

<template>
    <p v-if="positions.length === 0" class="px-5 py-8 text-sm text-gray-500 dark:text-gray-400">No open positions.</p>
    <ul v-else class="divide-y divide-gray-100 md:hidden dark:divide-gray-800">
        <li v-for="position in positions" :key="`${position.walletKey}-${position.symbol}`" class="space-y-1 px-4 py-3 text-sm">
            <p class="font-medium text-gray-900 dark:text-white">{{ position.symbol }}</p>
            <p class="text-gray-500 dark:text-gray-400">{{ formatQty(position.qty) }} @ {{ formatPrice(position.avgPrice) }}</p>
            <p v-if="position.unmarked" class="text-warning-600 dark:text-warning-400">No price mark</p>
            <p v-else class="flex flex-wrap gap-x-3">
                <MoneyCell :value="position.lastMark" :signed="false" />
                <MoneyCell :value="position.unrealizedPnl" />
            </p>
        </li>
    </ul>
    <div v-if="positions.length" class="hidden max-w-full overflow-x-auto md:block">
        <table class="w-full text-sm">
            <thead>
                <tr
                    class="border-b border-gray-100 text-left text-xs font-medium tracking-wide text-gray-500 uppercase dark:border-gray-800 dark:text-gray-400"
                >
                    <th class="px-4 py-3 font-medium">Symbol</th>
                    <th class="px-4 py-3 text-end font-medium">Qty</th>
                    <th class="px-4 py-3 text-end font-medium">Avg price</th>
                    <th class="px-4 py-3 text-end font-medium">Last mark</th>
                    <th class="px-4 py-3 text-end font-medium">Unrealized</th>
                    <th class="px-4 py-3 font-medium">Marked</th>
                </tr>
            </thead>
            <tbody>
                <tr
                    v-for="position in positions"
                    :key="`row-${position.walletKey}-${position.symbol}`"
                    class="border-b border-gray-50 last:border-0 dark:border-gray-800/80"
                >
                    <td class="px-4 py-3">
                        <span class="font-medium text-gray-900 dark:text-white">{{ position.symbol }}</span>
                        <span class="mt-0.5 block text-xs text-gray-400">{{ position.title }}</span>
                    </td>
                    <td class="px-4 py-3 text-end tabular-nums">{{ formatQty(position.qty) }}</td>
                    <td class="px-4 py-3 text-end tabular-nums">{{ formatPrice(position.avgPrice) }}</td>
                    <td class="px-4 py-3 text-end">
                        <span v-if="position.unmarked" class="text-warning-600 dark:text-warning-400">No price mark</span>
                        <MoneyCell v-else :value="position.lastMark" :signed="false" />
                    </td>
                    <td class="px-4 py-3 text-end">
                        <span v-if="position.unmarked" class="text-gray-400">{{ UNKNOWN_AMOUNT }}</span>
                        <span v-else class="inline-flex items-center justify-end gap-2">
                            <MoneyCell :value="position.unrealizedPnl" />
                            <PctCell :value="pnlPct(position)" />
                        </span>
                    </td>
                    <td class="px-4 py-3 whitespace-nowrap text-gray-500 dark:text-gray-400">
                        <TimeCell :value="position.lastMarkAt" />
                    </td>
                </tr>
            </tbody>
        </table>
    </div>
</template>
