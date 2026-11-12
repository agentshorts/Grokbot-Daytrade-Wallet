<script setup lang="ts">
import MoneyCell from '@/components/MoneyCell.vue'
import TimeCell from '@/components/TimeCell.vue'
import type { TradeRecord } from '@/lib/book'
import { formatPrice, formatQty } from '@/lib/format'

defineProps<{ trades: TradeRecord[]; selectedSymbol?: string }>()
const emit = defineEmits<{ select: [trade: TradeRecord] }>()

function pnlText(trade: TradeRecord) {
    if (trade.pnl != null) return null
    return trade.status === 'open' ? 'Open' : 'Matched'
}
</script>

<template>
    <p v-if="trades.length === 0" class="px-5 py-8 text-sm text-gray-500 dark:text-gray-400">No trades in this range</p>
    <ul v-else class="divide-y divide-gray-100 md:hidden dark:divide-gray-800">
        <li v-for="trade in trades" :key="trade.id">
            <button
                type="button"
                class="flex w-full flex-col gap-2 px-4 py-3 text-start focus-visible:outline-2 focus-visible:outline-offset-[-2px] focus-visible:outline-brand-500"
                :class="selectedSymbol === trade.symbol ? 'bg-brand-50/60 dark:bg-brand-500/10' : ''"
                @click="emit('select', trade)"
            >
                <span class="flex items-center justify-between gap-3">
                    <span class="font-medium text-gray-900 dark:text-white">{{ trade.symbol }}</span>
                    <span class="text-xs font-medium tracking-wide uppercase" :class="trade.side === 'buy' ? 'text-success-600' : 'text-error-500'">
                        {{ trade.side }}
                    </span>
                </span>
                <span class="flex flex-wrap gap-x-3 gap-y-1 text-sm text-gray-500 dark:text-gray-400">
                    <span>{{ trade.walletName }}</span>
                    <span>{{ formatQty(trade.qty) }} @ {{ formatPrice(trade.price) }}</span>
                    <MoneyCell v-if="trade.pnl != null" :value="trade.pnl" />
                    <span v-else>{{ pnlText(trade) }}</span>
                </span>
                <TimeCell class="text-xs text-gray-500 dark:text-gray-400" :value="trade.executedAt" />
            </button>
        </li>
    </ul>
    <div v-if="trades.length" class="hidden max-w-full overflow-x-auto md:block">
        <table class="w-full text-sm">
            <thead>
                <tr
                    class="border-b border-gray-100 text-left text-xs font-medium tracking-wide text-gray-500 uppercase dark:border-gray-800 dark:text-gray-400"
                >
                    <th class="px-4 py-3 font-medium">Time</th>
                    <th class="px-4 py-3 font-medium">Wallet</th>
                    <th class="px-4 py-3 font-medium">Symbol</th>
                    <th class="px-4 py-3 font-medium">Side</th>
                    <th class="px-4 py-3 text-end font-medium">Qty</th>
                    <th class="px-4 py-3 text-end font-medium">Price</th>
                    <th class="px-4 py-3 text-end font-medium">P&L</th>
                    <th class="px-4 py-3 text-end font-medium">Fees</th>
                </tr>
            </thead>
            <tbody>
                <tr
                    v-for="trade in trades"
                    :key="`row-${trade.id}`"
                    class="cursor-pointer border-b border-gray-50 last:border-0 hover:bg-gray-50 dark:border-gray-800/80 dark:hover:bg-white/[0.03]"
                    :class="selectedSymbol === trade.symbol ? 'bg-brand-50/50 dark:bg-brand-500/10' : ''"
                    @click="emit('select', trade)"
                >
                    <td class="px-4 py-3 whitespace-nowrap text-gray-600 dark:text-gray-300"><TimeCell :value="trade.executedAt" /></td>
                    <td class="px-4 py-3 whitespace-nowrap">
                        <span class="text-gray-800 dark:text-gray-100">{{ trade.walletName }}</span>
                        <span class="mt-0.5 block text-xs text-gray-400">{{ trade.mode === 'real' ? 'Real' : 'Paper' }}</span>
                    </td>
                    <td class="px-4 py-3 whitespace-nowrap">
                        <span class="font-medium text-gray-900 dark:text-white">{{ trade.symbol }}</span>
                        <span class="mt-0.5 block text-xs text-gray-400">{{ trade.title }}</span>
                    </td>
                    <td class="px-4 py-3 text-xs font-medium tracking-wide uppercase" :class="trade.side === 'buy' ? 'text-success-600' : 'text-error-500'">
                        {{ trade.side }}
                    </td>
                    <td class="px-4 py-3 text-end tabular-nums text-gray-700 dark:text-gray-200">{{ formatQty(trade.qty) }}</td>
                    <td class="px-4 py-3 text-end tabular-nums text-gray-700 dark:text-gray-200">{{ formatPrice(trade.price) }}</td>
                    <td class="px-4 py-3 text-end">
                        <MoneyCell v-if="trade.pnl != null" :value="trade.pnl" />
                        <span v-else class="text-gray-400">{{ pnlText(trade) }}</span>
                    </td>
                    <td class="px-4 py-3 text-end"><MoneyCell :value="trade.fee" :signed="false" /></td>
                </tr>
            </tbody>
        </table>
    </div>
</template>
