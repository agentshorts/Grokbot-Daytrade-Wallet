<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { chartMenu, type ChartHit, type MenuAction } from '@/lib/chartMenu'
import { formatMoney, formatQty } from '@/lib/format'
import { fromSpend, readSpendPercent, sellAmount, writeSpendPercent, type SpendQuote } from '@/lib/orderSize'

const props = withDefaults(
    defineProps<{
        hit: ChartHit
        x: number
        y: number
        pendingMoveId?: number
        extra?: MenuAction[]
        cash?: number
        heldQty?: number
        marketPrice?: number | null
        walletKey?: string
        baseIncrement?: number
        quoteIncrement?: number
        baseMinSize?: number
        minMarketFunds?: number
    }>(),
    {
        cash: 0,
        heldQty: 0,
        marketPrice: null,
        walletKey: '',
        baseIncrement: 0,
        quoteIncrement: 0,
        baseMinSize: 0,
        minMarketFunds: 0,
    },
)

const emit = defineEmits<{ action: [payload: { id: string; qty?: number }]; close: [] }>()

const spendBox = ref<HTMLInputElement | null>(null)
const percent = ref(0)
const spend = ref(0)
const spendText = ref('0.00')
const sellValue = ref(0)
const sellText = ref('0.00')
const sellMode = ref<'usd' | 'qty'>('usd')
const active = ref<'buy' | 'sell'>('buy')
const buyHint = ref('')
const sellHint = ref('')

const steps = computed(() => ({
    baseIncrement: props.baseIncrement,
    quoteIncrement: props.quoteIncrement,
    baseMinSize: props.baseMinSize,
    minMarketFunds: props.minMarketFunds,
}))

const clicked = computed(() => (props.hit.kind === 'time' ? 0 : props.hit.price))
const market = computed(() => (props.marketPrice && props.marketPrice > 0 ? props.marketPrice : clicked.value))
const priceHit = computed(() => props.hit.kind === 'price')

const buyMarket = computed(() => fromSpend(spend.value, market.value, props.cash, steps.value))
const buyLimit = computed(() => fromSpend(spend.value, clicked.value, props.cash, steps.value))
const sellQuote = computed(() => sellAmount(sellValue.value, sellMode.value, market.value, props.heldQty, steps.value))

const buyIds = new Set(['market-buy', 'limit-buy', 'stop-buy'])
const sellIds = new Set(['market-sell', 'limit-sell', 'stop', 'take'])

const grouped = computed(() => {
    const actions = [...(props.extra ?? []), ...chartMenu(props.hit, { pendingMoveId: props.pendingMoveId })]
    if (!priceHit.value) return { other: actions, buy: [] as MenuAction[], sell: [] as MenuAction[] }
    return {
        other: actions.filter((action) => !buyIds.has(action.id) && !sellIds.has(action.id)),
        buy: actions.filter((action) => buyIds.has(action.id)),
        sell: actions.filter((action) => sellIds.has(action.id)),
    }
})

const sellMax = computed(() => (sellMode.value === 'qty' ? props.heldQty : props.heldQty * Math.max(market.value, 0)))
const buyStep = computed(() => (props.quoteIncrement > 0 ? props.quoteIncrement : 0.01))
const sellStep = computed(() => (sellMode.value === 'qty' ? (props.baseIncrement > 0 ? props.baseIncrement : 0.00000001) : props.quoteIncrement > 0 ? props.quoteIncrement : 0.01))

function tone(value: 'buy' | 'sell' | 'neutral') {
    if (value === 'buy') return 'text-success-600'
    if (value === 'sell') return 'text-error-500'
    return 'text-gray-800 dark:text-gray-100'
}

function remember(next: number) {
    percent.value = Math.min(100, Math.max(0, next))
    writeSpendPercent(localStorage, props.walletKey, percent.value)
}

function showSpend(value: number) {
    spend.value = value
    spendText.value = value.toFixed(2)
}

function showSell(value: number) {
    sellValue.value = value
    sellText.value = sellMode.value === 'qty' ? formatQty(value) : value.toFixed(2)
}

function setPercent(next: number) {
    remember(next)
    const quote = fromSpend((props.cash * percent.value) / 100, market.value || 1, props.cash, steps.value)
    showSpend(quote.spend)
    buyHint.value = ''
    const cap = sellMax.value
    showSell(cap * (percent.value / 100))
    sellHint.value = ''
}

function onSpendSlider(raw: string) {
    active.value = 'buy'
    const value = Number(raw)
    if (!Number.isFinite(value)) return
    showSpend(Math.min(Math.max(value, 0), props.cash))
    buyHint.value = ''
    remember(props.cash > 0 ? (spend.value / props.cash) * 100 : 0)
}

function onSpendText(raw: string) {
    active.value = 'buy'
    spendText.value = raw
    const parsed = Number(raw)
    if (!Number.isFinite(parsed)) return
    if (parsed > props.cash) {
        showSpend(props.cash)
        buyHint.value = 'Capped at available cash.'
    } else {
        spend.value = parsed < 0 ? 0 : parsed
        buyHint.value = ''
    }
    remember(props.cash > 0 ? (spend.value / props.cash) * 100 : 0)
}

function onSellSlider(raw: string) {
    active.value = 'sell'
    const value = Number(raw)
    if (!Number.isFinite(value)) return
    showSell(Math.min(Math.max(value, 0), sellMax.value))
    sellHint.value = ''
    remember(sellMax.value > 0 ? (sellValue.value / sellMax.value) * 100 : 0)
}

function onSellText(raw: string) {
    active.value = 'sell'
    sellText.value = raw
    const parsed = Number(raw)
    if (!Number.isFinite(parsed)) return
    if (parsed > sellMax.value) {
        showSell(sellMax.value)
        sellHint.value = sellMode.value === 'qty' ? 'Capped at the quantity held.' : 'Capped at the position value.'
    } else {
        sellValue.value = parsed < 0 ? 0 : parsed
        sellHint.value = ''
    }
    remember(sellMax.value > 0 ? (sellValue.value / sellMax.value) * 100 : 0)
}

function setSellMode(mode: 'usd' | 'qty') {
    if (mode === sellMode.value) return
    const qty = sellQuote.value.qty
    sellMode.value = mode
    showSell(mode === 'qty' ? qty : qty * Math.max(market.value, 0))
}

function quoteFor(id: string): SpendQuote {
    if (id === 'market-buy') return buyMarket.value
    if (id === 'limit-buy' || id === 'stop-buy') return buyLimit.value
    return sellQuote.value
}

function choose(id: string) {
    if (!priceHit.value || (!buyIds.has(id) && !sellIds.has(id))) {
        emit('action', { id })
        return
    }
    const quote = quoteFor(id)
    if (quote.error || quote.qty <= 0) return
    emit('action', { id, qty: quote.qty })
}

function nudge(delta: number) {
    setPercent(percent.value + delta)
}

function onKey(event: KeyboardEvent) {
    if (event.key === 'Escape') {
        event.preventDefault()
        emit('close')
        return
    }
    if (event.key === 'ArrowUp' || event.key === 'ArrowRight') {
        event.preventDefault()
        nudge(1)
        return
    }
    if (event.key === 'ArrowDown' || event.key === 'ArrowLeft') {
        event.preventDefault()
        nudge(-1)
        return
    }
    if (event.key === 'Enter') {
        event.preventDefault()
        choose(active.value === 'sell' ? 'market-sell' : 'market-buy')
    }
}

function preview(quote: SpendQuote, resulting: number) {
    if (quote.qty <= 0 && quote.spend <= 0) return `Qty 0, fee ${formatMoney(0, { signed: false })}, position ${formatQty(resulting)}`
    if (quote.error) return quote.error
    return `Qty ${formatQty(quote.qty)}, fee ${formatMoney(0, { signed: false })}, position ${formatQty(resulting)}`
}

onMounted(() => {
    const saved = readSpendPercent(localStorage, props.walletKey)
    if (saved != null) setPercent(saved)
    spendBox.value?.focus()
})
</script>

<template>
    <ul
        role="menu"
        tabindex="-1"
        data-testid="chart-menu"
        class="fixed z-50 min-w-72 rounded-xl border border-gray-200 bg-white py-1 shadow-lg dark:border-gray-700 dark:bg-gray-900"
        :style="{ left: `${x}px`, top: `${y}px` }"
        @keydown="onKey"
    >
        <li v-for="action in grouped.other" :key="action.id">
            <button
                type="button"
                role="menuitem"
                class="block w-full px-3 py-2 text-start text-sm font-medium hover:bg-gray-50 dark:hover:bg-white/5"
                :class="tone(action.tone)"
                :data-testid="`menu-${action.id}`"
                @click="choose(action.id)"
            >
                {{ action.label }}
            </button>
        </li>
        <li v-if="priceHit" class="space-y-2 px-3 py-2 text-xs text-gray-600 dark:text-gray-300">
            <div class="font-semibold text-gray-800 dark:text-gray-100">Spend</div>
            <input data-testid="spend-slider" type="range" min="0" :max="cash" :step="buyStep" :value="spend" class="w-full" @input="onSpendSlider(($event.target as HTMLInputElement).value)" />
            <input
                ref="spendBox"
                data-testid="spend-input"
                inputmode="decimal"
                class="w-full rounded-md border border-gray-200 px-2 py-1 text-sm dark:border-gray-700 dark:bg-gray-950"
                :value="spendText"
                @input="onSpendText(($event.target as HTMLInputElement).value)"
                @focus="active = 'buy'"
            />
            <div class="flex gap-1">
                <button v-for="chip in [10, 25, 50, 75]" :key="chip" type="button" class="rounded-md border border-gray-200 px-1.5 py-0.5 dark:border-gray-700" :data-testid="`chip-${chip}`" @click="setPercent(chip)">
                    {{ chip }}%
                </button>
                <button type="button" data-testid="chip-max" class="rounded-md border border-gray-200 px-1.5 py-0.5 dark:border-gray-700" @click="setPercent(100)">Max</button>
            </div>
            <p v-if="buyHint" data-testid="spend-hint">{{ buyHint }}</p>
            <p data-testid="buy-preview">{{ preview(buyMarket, heldQty + buyMarket.qty) }}</p>
        </li>
        <li v-for="action in grouped.buy" :key="action.id">
            <button
                type="button"
                role="menuitem"
                class="block w-full px-3 py-2 text-start text-sm font-medium hover:bg-gray-50 dark:hover:bg-white/5"
                :class="tone(action.tone)"
                :data-testid="`menu-${action.id}`"
                @click="choose(action.id)"
            >
                {{ action.label }}
            </button>
        </li>
        <li v-if="priceHit" class="space-y-2 border-t border-gray-100 px-3 py-2 text-xs text-gray-600 dark:border-gray-800 dark:text-gray-300">
            <div class="flex items-center justify-between">
                <span class="font-semibold text-gray-800 dark:text-gray-100">Sell</span>
                <span class="flex gap-1">
                    <button type="button" data-testid="sell-usd" class="rounded-md px-1.5 py-0.5" :class="sellMode === 'usd' ? 'bg-gray-900 text-white dark:bg-white dark:text-gray-900' : ''" @click="setSellMode('usd')">USD</button>
                    <button type="button" data-testid="sell-qty" class="rounded-md px-1.5 py-0.5" :class="sellMode === 'qty' ? 'bg-gray-900 text-white dark:bg-white dark:text-gray-900' : ''" @click="setSellMode('qty')">Qty</button>
                </span>
            </div>
            <input data-testid="sell-slider" type="range" min="0" :max="sellMax" :step="sellStep" :value="sellValue" class="w-full" @input="onSellSlider(($event.target as HTMLInputElement).value)" />
            <input
                data-testid="sell-input"
                inputmode="decimal"
                class="w-full rounded-md border border-gray-200 px-2 py-1 text-sm dark:border-gray-700 dark:bg-gray-950"
                :value="sellText"
                @input="onSellText(($event.target as HTMLInputElement).value)"
                @focus="active = 'sell'"
            />
            <p v-if="sellHint" data-testid="sell-hint">{{ sellHint }}</p>
            <p data-testid="sell-preview">{{ preview(sellQuote, Math.max(0, heldQty - sellQuote.qty)) }}</p>
        </li>
        <li v-for="action in grouped.sell" :key="action.id">
            <button
                type="button"
                role="menuitem"
                class="block w-full px-3 py-2 text-start text-sm font-medium hover:bg-gray-50 dark:hover:bg-white/5"
                :class="tone(action.tone)"
                :data-testid="`menu-${action.id}`"
                @click="choose(action.id)"
            >
                {{ action.label }}
            </button>
        </li>
    </ul>
</template>
