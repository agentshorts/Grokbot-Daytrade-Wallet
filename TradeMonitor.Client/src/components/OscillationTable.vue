<script setup lang="ts">
import type { OscillationRow } from '@/api'
import Sparkline from '@/components/Sparkline.vue'
import { formatQty } from '@/lib/format'
import { contractName } from '@/lib/predictionContract'

defineProps<{ rows: OscillationRow[] }>()

function cents(value: number | null): string {
    if (value == null || !Number.isFinite(value)) return 'n/a'
    const rounded = Math.round(value * 10) / 10
    return Number.isInteger(rounded) ? `${rounded}c` : `${rounded.toFixed(1)}c`
}

function percent(value: number | null): string {
    if (value == null || !Number.isFinite(value)) return 'n/a'
    return `${Math.round(value)}%`
}

function rowName(row: OscillationRow): string {
    return contractName([row.wallet, row.productId, row.ticker, row.name], row.closeAt, 'America/Chicago', row.name || row.ticker)
}

function remaining(closeAt: string | null): string {
    if (!closeAt) return ''
    const end = Date.parse(closeAt)
    if (Number.isNaN(end)) return ''
    const delta = end - Date.now()
    if (delta <= 0) return 'Closed'
    const minutes = Math.max(0, Math.round(delta / 60000))
    const hours = Math.floor(minutes / 60)
    const mins = minutes % 60
    if (hours >= 48) return `in ${Math.floor(hours / 24)}d ${hours % 24}h`
    if (hours >= 1) return `in ${hours}h ${mins}m`
    return `in ${mins}m`
}

function sourceLabel(source: string): string {
    if (source === 'coinbase') return 'Coinbase'
    if (source === 'kalshi') return 'Kalshi'
    return 'No market data'
}

function rising(row: OscillationRow): boolean {
    const line = row.sparkline
    if (line.length < 2) return true
    return line[line.length - 1]! >= line[0]!
}
</script>

<template>
    <div class="overflow-x-auto rounded-2xl border border-gray-200 bg-white dark:border-gray-800 dark:bg-white/[0.03]">
        <table class="min-w-full text-sm" data-testid="oscillation-table">
            <thead class="text-start text-gray-500 dark:text-gray-400">
                <tr>
                    <th class="px-4 py-3 font-medium">Contract</th>
                    <th class="px-4 py-3 font-medium">Price</th>
                    <th class="px-4 py-3 font-medium">High</th>
                    <th class="px-4 py-3 font-medium">Low</th>
                    <th class="px-4 py-3 font-medium">Range</th>
                    <th class="px-4 py-3 font-medium">Reversals</th>
                    <th class="px-4 py-3 font-medium">Path</th>
                    <th class="px-4 py-3 font-medium">Closes</th>
                </tr>
            </thead>
            <tbody>
                <tr
                    v-for="row in rows"
                    :key="row.ticker + (row.wallet || '')"
                    class="border-t border-gray-100 dark:border-gray-800"
                    :data-testid="`oscillation-${row.ticker}`"
                >
                    <td class="px-4 py-3">
                        <div class="flex flex-wrap items-center gap-2">
                            <RouterLink
                                v-if="row.wallet"
                                :to="`/wallet/${encodeURIComponent(row.wallet)}`"
                                class="font-medium text-gray-900 hover:text-brand-600 dark:text-white/90"
                            >
                                {{ rowName(row) }}
                            </RouterLink>
                            <span v-else class="font-medium text-gray-900 dark:text-white/90">{{ rowName(row) }}</span>
                            <span
                                v-if="row.oscillating"
                                data-testid="oscillation-flag"
                                class="rounded-full bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-800 dark:bg-amber-400/15 dark:text-amber-200"
                                >Swinging</span
                            >
                        </div>
                        <p class="text-xs text-gray-500 dark:text-gray-400">{{ row.ticker }}</p>
                        <p v-if="row.qty > 0" class="text-xs text-gray-500 dark:text-gray-400">Open {{ formatQty(row.qty) }}</p>
                        <p v-if="row.error" class="text-xs text-error-600">{{ row.error }}</p>
                    </td>
                    <td class="px-4 py-3 tabular-nums">{{ cents(row.priceCents) }}</td>
                    <td class="px-4 py-3 tabular-nums">{{ cents(row.highCents) }}</td>
                    <td class="px-4 py-3 tabular-nums">{{ cents(row.lowCents) }}</td>
                    <td class="px-4 py-3 tabular-nums">{{ cents(row.rangeCents) }} ({{ percent(row.rangePercent) }})</td>
                    <td class="px-4 py-3 tabular-nums">{{ row.reversals }}</td>
                    <td class="w-36 px-4 py-3">
                        <Sparkline :values="row.sparkline" :positive="rising(row)" />
                        <p class="text-xs text-gray-500">{{ sourceLabel(row.source) }}</p>
                    </td>
                    <td class="px-4 py-3">
                        <p>{{ row.closeCentral || 'Unknown' }}</p>
                        <p v-if="remaining(row.closeAt)" class="text-xs text-gray-500">
                            {{ remaining(row.closeAt) }}
                        </p>
                    </td>
                </tr>
            </tbody>
        </table>
    </div>
</template>
