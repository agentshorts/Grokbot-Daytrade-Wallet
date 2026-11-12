<script setup lang="ts">
import { computed, ref } from 'vue'
import type { LiveBook, LiveCosting, LiveOrderType, LiveSide } from '@/api/live'
import type { WalletInfo } from '@/api/types'
import { formatMenuPrice } from '@/lib/chartMenu'
import { formatMoney, formatPct, formatQty, formatTimeCt } from '@/lib/format'
import { averageQuote, lotTotals } from '@/lib/lotQuote'
import type { PanelTab, PanelWidth } from '@/lib/livePanel'

const props = defineProps<{
    width: PanelWidth
    tab: PanelTab
    wallets: WalletInfo[]
    walletKey: string
    book: LiveBook | null
    price: number | null
    qty: number
    averageQty: number
    busy: boolean
    status: string
    error: string
    auth: boolean
}>()

const emit = defineEmits<{
    width: [value: PanelWidth]
    tab: [value: PanelTab]
    wallet: [key: string]
    qty: [value: number]
    'average-qty': [value: number]
    create: [payload: { name: string; cash: number }]
    buy: []
    'quick-sell': [tradeId: number]
    average: []
    place: [payload: { side: LiveSide; type: LiveOrderType; trigger: number | null; costing: LiveCosting; lotId: number | null }]
    cancel: [id: number]
    reset: []
}>()

const walletName = ref('')
const walletCash = ref('1000')
const side = ref<LiveSide>('Buy')
const orderType = ref<LiveOrderType>('Limit')
const trigger = ref('')
const costing = ref<LiveCosting>('Fifo')
const lotId = ref('')

const collapsed = computed(() => props.width === 'collapsed')
const simulated = computed(() => props.book?.simulated === true || props.wallets.find((item) => item.key === props.walletKey)?.mode === 'real')
const lots = computed(() => props.book?.lots ?? [])
const closedLots = computed(() => props.book?.closedLots ?? [])
const settlements = computed(() => props.book?.settlements ?? [])
const totals = computed(() => lotTotals(lots.value, closedLots.value.reduce((sum, lot) => sum + lot.realizedPnl, 0)))
const orders = computed(() => (props.book?.orders ?? []).filter((order) => order.status.toLowerCase() === 'open' || order.status === ''))
const average = computed(() => averageQuote(props.book?.average.openQty ?? 0, props.book?.average.openCost ?? 0, props.averageQty, props.price))
const buyCost = computed(() => (props.price == null ? null : props.qty * props.price))
const types = computed(() => (side.value === 'Buy' ? (['Market', 'Limit'] as LiveOrderType[]) : (['Market', 'Limit', 'StopLoss', 'TakeProfit'] as LiveOrderType[])))

const preview = computed(() => {
    const px = orderType.value === 'Market' ? props.price : Number(trigger.value)
    if (side.value === 'Buy' || px == null || Number.isNaN(px) || px <= 0) return null
    if (costing.value === 'Lot') {
        const lot = lots.value.find((item) => item.tradeId === Number(lotId.value))
        if (!lot || lot.qty <= 0) return null
        const qty = Math.min(props.qty, lot.qty)
        return qty * px - (lot.cost * qty) / lot.qty
    }
    if (costing.value === 'Average') return averageQuote(props.book?.average.openQty ?? 0, props.book?.average.openCost ?? 0, props.qty, px).profit
    const ordered = [...lots.value].sort((a, b) => (a.executedAt ?? '').localeCompare(b.executedAt ?? ''))
    let left = props.qty
    let cost = 0
    let taken = 0
    for (const lot of ordered) {
        if (left <= 0) break
        const slice = Math.min(lot.qty, left)
        cost += (lot.cost * slice) / lot.qty
        taken += slice
        left -= slice
    }
    return taken === 0 ? null : taken * px - cost
})

function pnlClass(value: number | null | undefined) {
    if (value == null) return 'text-gray-500'
    return value >= 0 ? 'text-success-600' : 'text-error-500'
}

function setWidth(value: PanelWidth) {
    emit('width', value)
    if (value !== 'collapsed') emit('tab', props.tab)
}

function openTab(value: PanelTab) {
    if (props.width === 'collapsed') emit('width', 'normal')
    emit('tab', value)
}

function onSide(value: LiveSide) {
    side.value = value
    if (value === 'Buy' && (orderType.value === 'StopLoss' || orderType.value === 'TakeProfit')) orderType.value = 'Limit'
}

function createWallet() {
    const cash = Number(walletCash.value)
    if (!walletName.value.trim() || !Number.isFinite(cash)) return
    emit('create', { name: walletName.value.trim(), cash })
}

function placeOrder() {
    const level = orderType.value === 'Market' ? null : Number(trigger.value)
    emit('place', {
        side: side.value,
        type: orderType.value,
        trigger: level,
        costing: side.value === 'Buy' ? 'Fifo' : costing.value,
        lotId: costing.value === 'Lot' ? Number(lotId.value) : null,
    })
}

function orderLabel(type: LiveOrderType) {
    if (type === 'StopLoss') return 'Stop-loss'
    if (type === 'TakeProfit') return 'Take-profit'
    return type
}
</script>

<template>
    <aside
        data-testid="trading-panel"
        class="flex shrink-0 border-t border-gray-200 bg-white dark:border-gray-800 dark:bg-gray-900 lg:border-s lg:border-t-0"
        :class="
            width === 'collapsed'
                ? 'h-12 w-full flex-row lg:h-full lg:w-12 lg:flex-col'
                : width === 'expanded'
                  ? 'h-auto w-full flex-col lg:h-full lg:w-1/2 lg:min-w-[520px]'
                  : 'h-auto w-full flex-col lg:h-full lg:w-[380px]'
        "
    >
        <div class="flex items-center gap-1 border-b border-gray-200 p-2 dark:border-gray-800" :class="collapsed ? 'h-full border-b-0 lg:h-auto lg:flex-col lg:border-b-0' : ''">
            <button type="button" data-testid="panel-collapsed" class="rounded-md px-2 py-1 text-xs font-medium text-gray-500" title="Collapse" @click="setWidth('collapsed')">│</button>
            <button type="button" data-testid="panel-normal" class="rounded-md px-2 py-1 text-xs font-medium text-gray-500" title="Normal" @click="setWidth('normal')">▮</button>
            <button type="button" data-testid="panel-expanded" class="rounded-md px-2 py-1 text-xs font-medium text-gray-500" title="Expand" @click="setWidth('expanded')">▬</button>
            <button v-if="collapsed" type="button" data-testid="tab-quick" class="rounded-md px-2 py-1 text-xs font-semibold text-success-600" @click="openTab('quick')">Q</button>
            <button v-if="collapsed" type="button" data-testid="tab-orders" class="rounded-md px-2 py-1 text-xs font-semibold text-error-500" @click="openTab('orders')">O</button>
            <button v-if="collapsed" type="button" data-testid="tab-lots" class="rounded-md px-2 py-1 text-xs font-semibold text-gray-600" @click="openTab('lots')">L</button>
            <template v-if="!collapsed">
                <button type="button" data-testid="tab-quick" class="rounded-lg px-3 py-1 text-sm font-semibold" :class="tab === 'quick' ? 'bg-success-600 text-white' : 'text-gray-600'" @click="openTab('quick')">Quick</button>
                <button type="button" data-testid="tab-orders" class="rounded-lg px-3 py-1 text-sm font-semibold" :class="tab === 'orders' ? 'bg-gray-900 text-white dark:bg-white dark:text-gray-900' : 'text-gray-600'" @click="openTab('orders')">
                    Orders
                    <span v-if="orders.length" class="ms-1 rounded-full bg-white/20 px-1.5 text-xs">{{ orders.length }}</span>
                </button>
                <button type="button" data-testid="tab-lots" class="rounded-lg px-3 py-1 text-sm font-semibold" :class="tab === 'lots' ? 'bg-brand-500 text-white' : 'text-gray-600'" @click="openTab('lots')">Lots</button>
            </template>
        </div>

        <div v-if="!collapsed" class="min-h-0 flex-1 space-y-3 overflow-y-auto p-3">
            <p v-if="simulated" data-testid="simulation-banner" class="rounded-lg bg-warning-400/15 px-3 py-2 text-sm font-semibold text-warning-600">
                Simulation, not saved
                <button type="button" class="ms-2 underline" :disabled="busy" @click="emit('reset')">Reset</button>
            </p>
            <p v-if="auth" class="text-sm text-gray-600">
                <RouterLink to="/signin" class="font-medium text-brand-500">Sign in</RouterLink>
                as an admin to trade.
            </p>
            <p v-if="error" class="rounded-lg bg-error-25 px-3 py-2 text-sm text-error-600" role="alert">{{ error }}</p>
            <p v-if="status" data-testid="live-status" class="text-sm text-gray-600 dark:text-gray-300">{{ status }}</p>

            <div class="flex items-center justify-between gap-2 text-sm">
                <span class="text-gray-500">Cash {{ formatMoney(book?.cash ?? 0, { signed: false }) }}</span>
                <span :class="pnlClass(book?.realizedPnl)">{{ formatMoney(book?.realizedPnl ?? 0) }} realized</span>
            </div>

            <template v-if="tab === 'quick'">
                <details class="rounded-lg border border-gray-200 px-3 py-2 text-sm dark:border-gray-700">
                    <summary class="cursor-pointer font-medium">New paper wallet</summary>
                    <div class="mt-2 flex gap-2">
                        <input v-model="walletName" class="min-w-0 flex-1 rounded-lg border border-gray-200 px-2 py-1.5 dark:border-gray-700 dark:bg-gray-950" placeholder="Name" />
                        <input v-model="walletCash" class="w-24 rounded-lg border border-gray-200 px-2 py-1.5 dark:border-gray-700 dark:bg-gray-950" inputmode="decimal" />
                        <button type="button" class="rounded-lg bg-brand-500 px-3 py-1.5 font-medium text-white" :disabled="busy" @click="createWallet">Create</button>
                    </div>
                </details>

                <div class="flex items-end gap-2">
                    <label class="block flex-1 text-xs font-medium text-gray-500">
                        Buy qty
                        <input
                            data-testid="buy-qty"
                            class="mt-1 w-full rounded-lg border border-gray-200 px-2 py-1.5 text-sm dark:border-gray-700 dark:bg-gray-950"
                            type="number"
                            min="0"
                            step="any"
                            :value="qty"
                            @input="emit('qty', Number(($event.target as HTMLInputElement).value))"
                        />
                    </label>
                    <button type="button" data-testid="quick-buy" class="rounded-lg bg-success-600 px-4 py-2 text-sm font-semibold text-white disabled:opacity-50" :disabled="busy || !walletKey" @click="emit('buy')">
                        Buy {{ buyCost == null ? '' : formatMoney(buyCost, { signed: false }) }}
                    </button>
                </div>

                <div class="overflow-x-auto">
                    <table class="w-full text-left text-sm">
                        <thead class="text-xs text-gray-500">
                            <tr>
                                <th class="py-1 pe-2 font-medium">Qty</th>
                                <th class="py-1 pe-2 font-medium">Buy</th>
                                <th class="py-1 pe-2 font-medium">Now</th>
                                <th class="py-1 pe-2 font-medium">P&L</th>
                                <th class="py-1 font-medium"></th>
                            </tr>
                        </thead>
                        <tbody>
                            <tr v-for="lot in lots" :key="lot.tradeId" data-testid="lot-row">
                                <td class="py-1 pe-2">{{ formatQty(lot.qty) }}</td>
                                <td class="py-1 pe-2">{{ formatMenuPrice(lot.buyPrice) }}</td>
                                <td class="py-1 pe-2">{{ price == null ? '' : formatMenuPrice(price) }}</td>
                                <td class="py-1 pe-2 font-medium" :class="pnlClass(lot.unrealizedPnl)">
                                    {{ formatMoney(lot.unrealizedPnl) }}
                                    <span class="block text-xs">{{ formatPct(lot.unrealizedPnlPercent) }}</span>
                                </td>
                                <td class="py-1 text-end">
                                    <button type="button" data-testid="quick-sell" class="rounded-md bg-error-500 px-2 py-1 text-xs font-semibold text-white" :disabled="busy" @click="emit('quick-sell', lot.tradeId)">
                                        Quick sell
                                    </button>
                                </td>
                            </tr>
                            <tr v-if="lots.length === 0">
                                <td colspan="5" class="py-3 text-gray-500">No open lots for this product.</td>
                            </tr>
                        </tbody>
                    </table>
                </div>

                <div class="rounded-xl border border-gray-200 p-3 dark:border-gray-700">
                    <p class="text-sm text-gray-600 dark:text-gray-300">
                        Sell at average
                        <span class="font-medium text-gray-900 dark:text-white">{{ formatMenuPrice(average.avgCost) }}</span>
                        <span class="ms-2 font-semibold" :class="pnlClass(average.profit)">{{ formatMoney(average.profit) }}</span>
                    </p>
                    <div class="mt-2 flex gap-2">
                        <input
                            data-testid="average-qty"
                            class="w-28 rounded-lg border border-gray-200 px-2 py-1.5 text-sm dark:border-gray-700 dark:bg-gray-950"
                            type="number"
                            min="0"
                            step="any"
                            :value="averageQty"
                            @input="emit('average-qty', Number(($event.target as HTMLInputElement).value))"
                        />
                        <button type="button" data-testid="average-sell" class="rounded-lg bg-error-500 px-3 py-1.5 text-sm font-semibold text-white disabled:opacity-50" :disabled="busy || average.sellQty <= 0" @click="emit('average')">
                            Sell at average
                        </button>
                    </div>
                </div>
            </template>

            <template v-else-if="tab === 'lots'">
                <div class="overflow-x-auto" data-testid="lots-view">
                    <table class="w-full text-left text-sm">
                        <thead class="text-xs text-gray-500">
                            <tr>
                                <th class="py-1 pe-2 font-medium">Opened</th>
                                <th class="py-1 pe-2 font-medium">Qty</th>
                                <th class="py-1 pe-2 font-medium">Buy</th>
                                <th class="py-1 pe-2 font-medium">Cost</th>
                                <th class="py-1 pe-2 font-medium">Value</th>
                                <th class="py-1 font-medium">P/L</th>
                            </tr>
                        </thead>
                        <tbody>
                            <tr v-for="lot in lots" :key="lot.tradeId" data-testid="open-lot">
                                <td class="py-1 pe-2">{{ formatTimeCt(lot.executedAt) }}</td>
                                <td class="py-1 pe-2">{{ formatQty(lot.qty) }}</td>
                                <td class="py-1 pe-2">{{ formatMenuPrice(lot.buyPrice) }}</td>
                                <td class="py-1 pe-2">{{ formatMoney(lot.cost, { signed: false }) }}</td>
                                <td class="py-1 pe-2">{{ price == null ? '' : formatMoney(lot.qty * price, { signed: false }) }}</td>
                                <td class="py-1 font-medium" :class="pnlClass(lot.unrealizedPnl)">
                                    {{ formatMoney(lot.unrealizedPnl) }}
                                    <span class="block text-xs">{{ formatPct(lot.unrealizedPnlPercent) }}</span>
                                </td>
                            </tr>
                            <tr v-if="lots.length === 0">
                                <td colspan="6" class="py-3 text-gray-500">No open lots for this product.</td>
                            </tr>
                        </tbody>
                        <tfoot data-testid="lot-totals">
                            <tr class="border-t border-gray-200 text-xs dark:border-gray-700">
                                <td class="py-2 pe-2 font-medium" colspan="2">{{ formatQty(totals.qty) }} held</td>
                                <td class="py-2 pe-2" colspan="2">Avg {{ formatMenuPrice(totals.averageCost) }}</td>
                                <td class="py-2 font-medium" colspan="2" :class="pnlClass(totals.total)">
                                    {{ formatMoney(totals.unrealized) }} open
                                    <span class="block">{{ formatMoney(totals.realized) }} realized</span>
                                    <span class="block">{{ formatMoney(totals.total) }} total</span>
                                </td>
                            </tr>
                        </tfoot>
                    </table>
                </div>
                <details data-testid="closed-lots" class="rounded-lg border border-gray-200 px-3 py-2 text-sm dark:border-gray-700">
                    <summary class="cursor-pointer font-medium">Closed lots ({{ closedLots.length }})</summary>
                    <ul class="mt-2 space-y-1">
                        <li v-for="lot in closedLots" :key="lot.tradeId" data-testid="closed-lot">
                            {{ formatQty(lot.qty) }} @ {{ formatMenuPrice(lot.buyPrice) }} sold {{ formatMenuPrice(lot.sellPrice) }}
                            <span class="font-medium" :class="pnlClass(lot.realizedPnl)">{{ formatMoney(lot.realizedPnl) }}</span>
                        </li>
                        <li v-if="closedLots.length === 0" class="text-gray-500">No closed lots.</li>
                    </ul>
                </details>
                <ul v-if="settlements.length" data-testid="settlement-lots" class="space-y-1 text-sm">
                    <li v-for="lot in settlements" :key="lot.tradeId">
                        Settlement {{ formatQty(lot.qty) }} @ {{ formatMenuPrice(lot.sellPrice) }}
                        <span :class="pnlClass(lot.realizedPnl)">{{ formatMoney(lot.realizedPnl) }}</span>
                    </li>
                </ul>
            </template>

            <template v-else>
                <div class="grid grid-cols-2 gap-2 text-sm">
                    <label class="text-xs font-medium text-gray-500">
                        Side
                        <select class="mt-1 w-full rounded-lg border border-gray-200 px-2 py-1.5 dark:border-gray-700 dark:bg-gray-950" :value="side" @change="onSide(($event.target as HTMLSelectElement).value as LiveSide)">
                            <option value="Buy">Buy</option>
                            <option value="Sell">Sell</option>
                        </select>
                    </label>
                    <label class="text-xs font-medium text-gray-500">
                        Type
                        <select class="mt-1 w-full rounded-lg border border-gray-200 px-2 py-1.5 dark:border-gray-700 dark:bg-gray-950" :value="orderType" @change="orderType = ($event.target as HTMLSelectElement).value as LiveOrderType">
                            <option v-for="item in types" :key="item" :value="item">{{ orderLabel(item) }}</option>
                        </select>
                    </label>
                    <label class="text-xs font-medium text-gray-500">
                        Qty
                        <input class="mt-1 w-full rounded-lg border border-gray-200 px-2 py-1.5 dark:border-gray-700 dark:bg-gray-950" type="number" min="0" step="any" :value="qty" @input="emit('qty', Number(($event.target as HTMLInputElement).value))" />
                    </label>
                    <label v-if="orderType !== 'Market'" class="text-xs font-medium text-gray-500">
                        Trigger
                        <input v-model="trigger" class="mt-1 w-full rounded-lg border border-gray-200 px-2 py-1.5 dark:border-gray-700 dark:bg-gray-950" type="number" min="0" step="any" />
                    </label>
                    <label v-if="side === 'Sell'" class="col-span-2 text-xs font-medium text-gray-500">
                        Match
                        <select v-model="costing" class="mt-1 w-full rounded-lg border border-gray-200 px-2 py-1.5 dark:border-gray-700 dark:bg-gray-950">
                            <option value="Fifo">Oldest first</option>
                            <option value="Average">Average</option>
                            <option value="Lot">One lot</option>
                        </select>
                    </label>
                    <label v-if="side === 'Sell' && costing === 'Lot'" class="col-span-2 text-xs font-medium text-gray-500">
                        Lot
                        <select v-model="lotId" class="mt-1 w-full rounded-lg border border-gray-200 px-2 py-1.5 dark:border-gray-700 dark:bg-gray-950">
                            <option value="">Choose a lot</option>
                            <option v-for="lot in lots" :key="lot.tradeId" :value="String(lot.tradeId)">{{ formatQty(lot.qty) }} @ {{ formatMenuPrice(lot.buyPrice) }}</option>
                        </select>
                    </label>
                </div>
                <p class="text-sm" :class="pnlClass(preview)">{{ preview == null ? 'GTC until you cancel.' : `Profit before placing ${formatMoney(preview)}` }}</p>
                <button
                    type="button"
                    data-testid="place-order"
                    class="w-full rounded-lg px-3 py-2 text-sm font-semibold text-white disabled:opacity-50"
                    :class="side === 'Buy' ? 'bg-success-600' : 'bg-error-500'"
                    :disabled="busy || !walletKey"
                    @click="placeOrder"
                >
                    {{ side === 'Buy' ? 'Place buy' : 'Place sell' }}
                </button>
                <ul class="space-y-2">
                    <li v-for="order in orders" :key="order.id" data-testid="open-order" class="flex items-center justify-between gap-2 rounded-lg border border-gray-200 px-3 py-2 text-sm dark:border-gray-700">
                        <span :class="order.side === 'Buy' ? 'text-success-600' : 'text-error-500'">
                            {{ orderLabel(order.type) }} {{ order.side }} {{ formatQty(order.qty) }}
                            <span v-if="order.triggerPrice != null"> @ {{ formatMenuPrice(order.triggerPrice) }}</span>
                            <span class="ms-1 text-xs text-gray-500">GTC</span>
                            <span v-if="order.lastError" class="block text-xs text-error-500">{{ order.lastError }}</span>
                        </span>
                        <button type="button" class="rounded-md px-2 py-1 text-xs font-medium text-gray-600" :disabled="busy" @click="emit('cancel', order.id)">Cancel</button>
                    </li>
                    <li v-if="orders.length === 0" class="text-sm text-gray-500">No open orders.</li>
                </ul>
            </template>
        </div>
    </aside>
</template>
