<script setup lang="ts">
import AmountCell from '@/components/AmountCell.vue'
import MoneyCell from '@/components/MoneyCell.vue'
import PctCell from '@/components/PctCell.vue'
import TimeCell from '@/components/TimeCell.vue'
import { formatPrice, formatQty } from '@/lib/format'
import { modeLabel, priceKindLabel, sideLabel, statusLabel, type TradeSort } from '@/lib/tradeQuery'
import type { TradeRow } from '@/api'

const props = defineProps<{
    trades: TradeRow[]
    selectedId: number | null
    orderBy: TradeSort
    descending: boolean
}>()

const emit = defineEmits<{
    select: [id: number]
    sort: [order: TradeSort]
}>()

function ariaSort(order: TradeSort): 'ascending' | 'descending' | 'none' {
    if (props.orderBy !== order) return 'none'
    return props.descending ? 'descending' : 'ascending'
}

function open(id: number) {
    emit('select', id)
}
</script>

<template>
    <div class="max-w-full overflow-x-auto">
        <table class="w-full min-w-[1100px] text-sm" data-testid="trades-table">
            <caption class="sr-only">
                Trades
            </caption>
            <thead>
                <tr
                    class="border-b border-gray-100 text-left text-xs font-medium tracking-wide text-gray-500 uppercase dark:border-gray-800 dark:text-gray-400"
                >
                    <th class="px-4 py-3 font-medium" scope="col" :aria-sort="ariaSort('time')">
                        <button type="button" class="inline-flex items-center gap-1" @click="emit('sort', 'time')">
                            Time
                            <span v-if="orderBy === 'time'" aria-hidden="true">{{ descending ? '↓' : '↑' }}</span>
                        </button>
                    </th>
                    <th class="px-4 py-3 font-medium" scope="col" :aria-sort="ariaSort('wallet')">
                        <button type="button" class="inline-flex items-center gap-1" @click="emit('sort', 'wallet')">Wallet</button>
                    </th>
                    <th class="px-4 py-3 font-medium" scope="col">Mode</th>
                    <th class="px-4 py-3 font-medium" scope="col" :aria-sort="ariaSort('symbol')">
                        <button type="button" class="inline-flex items-center gap-1" @click="emit('sort', 'symbol')">Symbol</button>
                    </th>
                    <th class="px-4 py-3 font-medium" scope="col">Side</th>
                    <th class="px-4 py-3 text-end font-medium" scope="col" :aria-sort="ariaSort('qty')">
                        <button type="button" class="inline-flex items-center gap-1" @click="emit('sort', 'qty')">Qty</button>
                    </th>
                    <th class="px-4 py-3 text-end font-medium" scope="col" :aria-sort="ariaSort('price')">
                        <button type="button" class="inline-flex items-center gap-1" @click="emit('sort', 'price')">Buy price</button>
                    </th>
                    <th class="px-4 py-3 text-end font-medium" scope="col">Exit / mark</th>
                    <th class="px-4 py-3 text-end font-medium" scope="col" :aria-sort="ariaSort('pnl')">
                        <button type="button" class="inline-flex items-center gap-1" @click="emit('sort', 'pnl')">P&L</button>
                    </th>
                    <th class="px-4 py-3 text-end font-medium" scope="col">P&L %</th>
                    <th class="px-4 py-3 text-end font-medium" scope="col" :aria-sort="ariaSort('fee')">
                        <button type="button" class="inline-flex items-center gap-1" @click="emit('sort', 'fee')">Fees</button>
                    </th>
                    <th class="px-4 py-3 font-medium" scope="col">Status</th>
                    <th class="px-4 py-3 font-medium" scope="col">Note</th>
                </tr>
            </thead>
            <tbody>
                <tr
                    v-for="trade in trades"
                    :key="trade.id"
                    tabindex="0"
                    class="cursor-pointer border-b border-gray-50 last:border-0 hover:bg-gray-50 focus-visible:outline-2 focus-visible:outline-offset-[-2px] focus-visible:outline-brand-500 dark:border-gray-800/80 dark:hover:bg-white/[0.03]"
                    :class="selectedId === trade.id ? 'bg-brand-50/60 dark:bg-brand-500/10' : ''"
                    :data-testid="`trade-row-${trade.id}`"
                    :aria-selected="selectedId === trade.id"
                    :aria-label="`Open ${trade.symbol}`"
                    @click="open(trade.id)"
                    @keydown.enter.prevent="open(trade.id)"
                    @keydown.space.prevent="open(trade.id)"
                >
                    <td class="px-4 py-3 whitespace-nowrap text-gray-600 dark:text-gray-300"><TimeCell :value="trade.executedAt" /></td>
                    <td class="px-4 py-3 whitespace-nowrap text-gray-800 dark:text-gray-100">{{ trade.walletName || trade.wallet }}</td>
                    <td class="px-4 py-3 whitespace-nowrap text-gray-600 dark:text-gray-300">{{ modeLabel(trade.mode) }}</td>
                    <td class="px-4 py-3 whitespace-nowrap">
                        <span class="font-medium text-gray-900 dark:text-white">{{ trade.symbol }}</span>
                        <span v-if="trade.title" class="mt-0.5 block text-xs text-gray-400">{{ trade.title }}</span>
                    </td>
                    <td class="px-4 py-3 text-xs font-medium tracking-wide uppercase" :class="trade.side === 'buy' ? 'text-success-600' : 'text-error-500'">
                        {{ sideLabel(trade.side) }}
                    </td>
                    <td class="px-4 py-3 text-end tabular-nums text-gray-700 dark:text-gray-200">{{ formatQty(trade.qty) }}</td>
                    <td class="px-4 py-3 text-end tabular-nums text-gray-700 dark:text-gray-200">{{ formatPrice(trade.buyPrice) }}</td>
                    <td class="px-4 py-3 text-end" :data-testid="`exit-${trade.id}`">
                        <template v-if="trade.currentOrExitPrice != null">
                            <span class="tabular-nums text-gray-700 dark:text-gray-200">{{ formatPrice(trade.currentOrExitPrice) }}</span>
                            <span v-if="priceKindLabel(trade.priceKind)" class="mt-0.5 block text-xs text-gray-400">{{ priceKindLabel(trade.priceKind) }}</span>
                        </template>
                        <span v-else class="text-gray-400">-</span>
                    </td>
                    <td class="px-4 py-3 text-end" :data-testid="`pnl-${trade.id}`"><AmountCell :value="trade.pnl" /></td>
                    <td class="px-4 py-3 text-end" :data-testid="`pnl-pct-${trade.id}`">
                        <span v-if="trade.pnlPercent == null" class="text-gray-400">-</span>
                        <PctCell v-else :value="trade.pnlPercent" />
                    </td>
                    <td class="px-4 py-3 text-end"><MoneyCell :value="trade.fee" :signed="false" /></td>
                    <td class="px-4 py-3 whitespace-nowrap">
                        <span
                            class="rounded-full px-2 py-0.5 text-xs font-medium"
                            :class="
                                trade.status === 'open' ? 'bg-warning-400/15 text-warning-600' : 'bg-gray-100 text-gray-600 dark:bg-white/5 dark:text-gray-300'
                            "
                        >
                            {{ statusLabel(trade.status) }}
                        </span>
                    </td>
                    <td class="max-w-48 truncate px-4 py-3 text-gray-500 dark:text-gray-400" :title="trade.note ?? ''">{{ trade.note }}</td>
                </tr>
            </tbody>
        </table>
    </div>
</template>
