<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import type { LiveBarPage, LiveBook, LiveCandle, LiveCosting, LiveOrderType, LiveProduct, LiveSide, LiveTradeMark, PlaceOrderInput, TrackedSymbol } from '@/api/live'
import type { WalletInfo } from '@/api/types'
import { useRoute, useRouter } from 'vue-router'
import ChartMenu from '@/components/live/ChartMenu.vue'
import ChartToolbar from '@/components/live/ChartToolbar.vue'
import LiveChart from '@/components/live/LiveChart.vue'
import ProductSelect from '@/components/live/ProductSelect.vue'
import TradingPanel from '@/components/live/TradingPanel.vue'
import { explainLedgerError, useLedger } from '@/composables/useLedger'
import { useLive } from '@/composables/useLive'
import { useLiveStream } from '@/composables/useLiveStream'
import { useTheme } from '@/composables/useTheme'
import { formatMenuPrice } from '@/lib/chartMenu'
import type { ChartHit } from '@/lib/chartMenu'
import { readChartPrefs, rememberInterval, removeCustomInterval, writeChartPrefs, type ChartPrefs } from '@/lib/chartPrefs'
import { chartQuery, rangeBounds, readChartQuery, type ChartRange } from '@/lib/chartRange'
import { baselineValue, mergeCandles, type BaselineSource, type CandleBar, type ChartType, type DeltaMode } from '@/lib/chartSeries'
import { chartZone, unixFromWall, wallInput, zoneAbbrev, type ChartZoneMode } from '@/lib/chartTime'
import { applyCandleTick, bucketSeconds, toCandleBars } from '@/lib/liveSeries'
import { quoteLots } from '@/lib/lotQuote'
import { readPanelTab, readPanelWidth, readShowTrades, writePanelTab, writePanelWidth, writeShowTrades, type PanelTab, type PanelWidth } from '@/lib/livePanel'
import { liveQuery, readLiveSelection } from '@/lib/liveQuery'
import { toTradeMarks } from '@/lib/tradeMarkers'

const ledger = useLedger()
const live = useLive()
const stream = useLiveStream()
const route = useRoute()
const router = useRouter()
const { isDark } = useTheme()

const products = ref<LiveProduct[]>([])
const product = ref('BTC-USD')
const prefs = ref<ChartPrefs>(readChartPrefs(localStorage))
const fromSec = ref<number | null>(null)
const toSec = ref<number | null>(null)
const candles = ref<CandleBar[]>([])
const bucket = ref(bucketSeconds(prefs.value.interval))
const hint = ref('')
const price = ref<number | null>(null)
const wallets = ref<WalletInfo[]>([])
const walletKey = ref('')
const unknownWallet = ref('')
const walletNotice = ref('')
const book = ref<LiveBook | null>(null)
const marks = ref<LiveTradeMark[]>([])
const showTrades = ref(true)
const simulationId = ref<string | null>(null)
const width = ref<PanelWidth>('normal')
const tab = ref<PanelTab>('quick')
const qty = ref(1)
const averageQty = ref(0)
const averageEdited = ref(false)
const busy = ref(false)
const status = ref('')
const error = ref('')
const auth = ref(false)
const ready = ref(false)
const menu = ref<{ x: number; y: number; hit: ChartHit } | null>(null)
const pendingMoveId = ref<number | null>(null)
const chart = ref<{ zoom: (factor: number) => void; fit: () => void } | null>(null)
const historyBusy = ref(false)
const historyDone = ref(false)
let timer = 0
let stopStream: (() => void) | null = null
let sawLive = false
let booting = true

const points = computed(() => candles.value.map((bar) => ({ time: bar.time, value: bar.close })))
const fitKey = computed(() => `${product.value}:${prefs.value.interval}:${prefs.value.type}:${prefs.value.range}:${fromSec.value ?? ''}:${toSec.value ?? ''}`)
const zoneName = computed(() => chartZone(prefs.value.timeZone))
const zoneLabel = computed(() => zoneAbbrev(Math.floor(Date.now() / 1000), zoneName.value))
const averageCost = computed(() => (book.value && book.value.average.avgCost > 0 ? book.value.average.avgCost : null))
const baseline = computed(() => baselineValue(candles.value, prefs.value.baselineSource, averageCost.value, prefs.value.baselinePrice))
const showBaseline = computed(() => prefs.value.type === 'baseline' || prefs.value.type === 'vertex-baseline')
const fromInput = computed(() => (fromSec.value == null ? '' : wallInput(fromSec.value, zoneName.value)))
const toInput = computed(() => (toSec.value == null ? '' : wallInput(toSec.value, zoneName.value)))
const realWallet = computed(() => wallets.value.find((item) => item.key === walletKey.value)?.mode === 'real')
const shownLots = computed(() =>
    quoteLots(book.value?.lots ?? [], price.value).map((lot) => ({
        ...lot,
        symbol: book.value?.symbol ?? product.value,
        executedAt: lot.executedAt ?? null,
    })),
)
const shownBook = computed<LiveBook | null>(() => (book.value ? { ...book.value, lots: shownLots.value } : null))
const chartMarkers = computed(() => (showTrades.value ? toTradeMarks(marks.value) : []))
const linkLabel = computed(() => (stream.link.value === 'live' ? 'live' : stream.link.value === 'reconnecting' ? 'reconnecting' : 'connecting'))

function rememberWidth(value: PanelWidth) {
    width.value = value
    writePanelWidth(localStorage, value)
}

function rememberTab(value: PanelTab) {
    tab.value = value
    writePanelTab(localStorage, value)
}

function onShowTrades(value: boolean) {
    showTrades.value = value
    writeShowTrades(localStorage, value)
}

function fail(err: unknown) {
    const explained = explainLedgerError(err)
    error.value = explained.message
    auth.value = explained.code === 'auth'
}

function toProducts(rows: TrackedSymbol[]): LiveProduct[] {
    return rows
        .filter((row) => row.listed)
        .map((row) => ({
            productId: row.productId,
            base: row.productId.split('-')[0] || row.productId,
            quote: 'USD',
            name: row.name || row.productId,
            price: null,
            baseIncrement: row.baseIncrement ?? 0,
            quoteIncrement: row.quoteIncrement ?? 0,
            baseMinSize: row.baseMinSize ?? 0,
            minMarketFunds: row.minMarketFunds ?? 0,
        }))
}

async function loadSymbols() {
    const tracked = await live.tracked()
    products.value = toProducts(tracked.results)
    if (!products.value.some((item) => item.productId === product.value) && products.value[0]) product.value = products.value[0].productId
}

function bounds() {
    if (prefs.value.range === 'custom') return { from: fromSec.value, to: toSec.value }
    return rangeBounds(prefs.value.range, Math.floor(Date.now() / 1000), zoneName.value)
}

function syncQuery() {
    const chart = chartQuery({ range: prefs.value.range, from: fromSec.value, to: toSec.value, interval: prefs.value.interval })
    const selection = liveQuery(walletKey.value, product.value, unknownWallet.value || null)
    const next: Record<string, string> = {}
    for (const [key, value] of Object.entries(route.query)) {
        if (typeof value === 'string' && key !== 'from' && key !== 'to') next[key] = value
    }
    if (!selection.wallet) delete next.wallet
    if (!selection.symbol) delete next.symbol
    Object.assign(next, selection, chart)
    const current: Record<string, string> = {}
    for (const [key, value] of Object.entries(route.query)) {
        if (typeof value === 'string') current[key] = value
    }
    const keys = new Set([...Object.keys(next), ...Object.keys(current)])
    if ([...keys].every((key) => (next[key] ?? '') === (current[key] ?? ''))) return
    void router.replace({ query: next })
}

function savePrefs(next: ChartPrefs) {
    prefs.value = next
    writeChartPrefs(localStorage, next)
    syncQuery()
}

function readPayload(payload: LiveBarPage | LiveCandle[]) {
    if (Array.isArray(payload)) return { rows: payload, hint: '', seconds: bucketSeconds(prefs.value.interval) }
    return {
        rows: payload.results,
        hint: payload.hint,
        seconds: payload.seconds > 0 ? payload.seconds : bucketSeconds(payload.granularity || prefs.value.interval),
    }
}

async function loadBars(append = false) {
    const previous = candles.value
    const payload = (await live.bars(product.value, prefs.value.interval, bounds())) as LiveBarPage | LiveCandle[]
    const page = readPayload(payload)
    const next = toCandleBars(page.rows)
    candles.value = append ? mergeCandles(previous, next) : next
    if (append && candles.value.length === previous.length) historyDone.value = true
    hint.value = page.hint
    bucket.value = page.seconds
    const last = candles.value[candles.value.length - 1]
    if (price.value == null && last) price.value = last.close
}

function follow(next: string) {
    stopStream?.()
    stopStream = stream.subscribe(next, (tick) => {
        if (tick.productId && tick.productId.toUpperCase() !== product.value.toUpperCase()) return
        price.value = tick.price
        candles.value = applyCandleTick(candles.value, tick, bucket.value)
    })
}

async function loadWallets() {
    const listed = await ledger.wallets()
    wallets.value = listed.results.filter((item) => !item.isArchived)
}

async function refreshBook() {
    if (!walletKey.value) return
    const sellQty = averageEdited.value ? averageQty.value : undefined
    const next = realWallet.value
        ? simulationId.value
            ? await live.simulation(simulationId.value, product.value, sellQty)
            : await live.openSimulation(walletKey.value, product.value)
        : await live.book(walletKey.value, product.value, sellQty)
    book.value = next
    if (next.simulationId) simulationId.value = next.simulationId
    if (price.value == null && next.price != null) price.value = next.price
    if (!averageEdited.value) averageQty.value = next.average.openQty
}

async function refreshTrades() {
    if (!walletKey.value) {
        marks.value = []
        return
    }
    marks.value = await live.trades(walletKey.value, product.value, realWallet.value ? simulationId.value : null)
}

async function refreshSide() {
    try {
        if (walletKey.value) {
            await refreshBook()
            await refreshTrades()
        } else {
            marks.value = []
        }
        error.value = ''
        auth.value = false
    } catch (err) {
        fail(err)
    }
}

async function run(action: () => Promise<void>) {
    if (busy.value) return
    busy.value = true
    error.value = ''
    try {
        await action()
    } catch (err) {
        fail(err)
    } finally {
        busy.value = false
    }
}

function order(input: PlaceOrderInput) {
    return run(async () => {
        if (!walletKey.value) {
            status.value = 'Choose a wallet first.'
            return
        }
        const next = realWallet.value && simulationId.value ? await live.placeSimulation(simulationId.value, input) : await live.placePaper(walletKey.value, input)
        book.value = next
        await refreshTrades()
        const kind = input.type === 'Market' ? 'Market' : input.type === 'StopBuy' ? 'Stop buy' : input.type === 'StopLoss' ? 'Stop-loss' : input.type === 'TakeProfit' ? 'Take-profit' : 'Limit'
        status.value = `${kind} ${input.side.toLowerCase()} placed`
        menu.value = null
    })
}

const selected = computed(() => products.value.find((item) => item.productId === product.value) ?? null)

function onMenu(action: { id: string; qty?: number }) {
    const id = action.id
    const hit = menu.value?.hit
    if (!hit) return
    if (hit.kind === 'time') {
        if (id === 'zoom-in') chart.value?.zoom(0.7)
        else if (id === 'zoom-out') chart.value?.zoom(1.4)
        else if (id === 'fit') chart.value?.fit()
        else savePrefs({ ...prefs.value, interval: id })
        menu.value = null
        return
    }
    if (id === 'move-here' && hit.kind === 'order') {
        pendingMoveId.value = hit.orderId
        status.value = 'Right-click a price to move the order.'
        menu.value = null
        return
    }
    if (id === 'move' && hit.kind === 'price' && pendingMoveId.value != null) {
        const idToMove = pendingMoveId.value
        pendingMoveId.value = null
        void run(async () => {
            book.value = realWallet.value && simulationId.value ? await live.moveSimulation(simulationId.value, idToMove, hit.price) : await live.movePaper(idToMove, hit.price)
            await refreshTrades()
            status.value = `Order moved to ${formatMenuPrice(hit.price)}`
        })
        menu.value = null
        return
    }
    if (id === 'cancel' && hit.kind === 'order') {
        void onCancel(hit.orderId)
        menu.value = null
        return
    }
    if (id === 'quick-sell' && hit.kind === 'lot') {
        const lot = shownLots.value.find((item) => item.tradeId === hit.lotId)
        if (lot) void order({ symbol: product.value, side: 'Sell', type: 'Market', qty: lot.qty, costing: 'Lot', lotId: lot.tradeId })
        return
    }
    if (hit.kind !== 'price') return
    const amount = action.qty != null && action.qty > 0 ? action.qty : qty.value
    const shared = { symbol: product.value, qty: amount, triggerPrice: hit.price }
    if (id === 'market-buy') void order({ symbol: product.value, side: 'Buy', type: 'Market', qty: amount, costing: 'Fifo' })
    if (id === 'limit-buy') void order({ ...shared, side: 'Buy', type: 'Limit', costing: 'Fifo' })
    if (id === 'stop-buy') void order({ ...shared, side: 'Buy', type: 'StopBuy', costing: 'Fifo' })
    if (id === 'market-sell') void order({ symbol: product.value, side: 'Sell', type: 'Market', qty: amount, costing: 'Fifo' })
    if (id === 'limit-sell') void order({ ...shared, side: 'Sell', type: 'Limit', costing: 'Fifo' })
    if (id === 'stop') void order({ ...shared, side: 'Sell', type: 'StopLoss', costing: 'Fifo' })
    if (id === 'take') void order({ ...shared, side: 'Sell', type: 'TakeProfit', costing: 'Fifo' })
}

function onBuy() {
    void order({ symbol: product.value, side: 'Buy', type: 'Market', qty: qty.value, costing: 'Fifo' })
}

function onQuickSell(tradeId: number) {
    const lot = shownLots.value.find((item) => item.tradeId === tradeId)
    if (!lot) return
    void order({ symbol: product.value, side: 'Sell', type: 'Market', qty: lot.qty, costing: 'Lot', lotId: lot.tradeId })
}

function onAverage() {
    void order({ symbol: product.value, side: 'Sell', type: 'Market', qty: averageQty.value, costing: 'Average' })
}

function onAverageQty(value: number) {
    averageEdited.value = true
    averageQty.value = value
}

function onPlace(payload: { side: LiveSide; type: LiveOrderType; trigger: number | null; costing: LiveCosting; lotId: number | null }) {
    void order({
        symbol: product.value,
        side: payload.side,
        type: payload.type,
        qty: qty.value,
        triggerPrice: payload.trigger,
        costing: payload.costing,
        lotId: payload.lotId,
    })
}

function onCancel(id: number) {
    void run(async () => {
        book.value = realWallet.value && simulationId.value ? await live.cancelSimulation(simulationId.value, id) : await live.cancelPaper(id)
        await refreshTrades()
        status.value = 'Order cancelled'
        menu.value = null
    })
}

function onCreate(payload: { name: string; cash: number }) {
    void run(async () => {
        const created = await live.createPaperWallet(payload.name, payload.cash)
        await loadWallets()
        walletKey.value = created.key
        simulationId.value = null
        await refreshSide()
        status.value = `Paper wallet ${created.name} opened`
    })
}

function onReset() {
    if (!simulationId.value) return
    void run(async () => {
        book.value = await live.resetSimulation(simulationId.value!, product.value)
        await refreshTrades()
        status.value = 'Simulation reset'
    })
}

function applyQuery() {
    const selection = readLiveSelection(route.query, wallets.value, products.value, product.value)
    product.value = selection.symbol
    walletKey.value = selection.walletKey
    unknownWallet.value = selection.unknownWallet ?? ''
    walletNotice.value = selection.unknownWallet ? 'That wallet is not available.' : ''
}

function onProduct(id: string) {
    if (id === product.value) return
    product.value = id
    if (!booting) syncQuery()
}

function onWallet(key: string) {
    walletKey.value = key
    unknownWallet.value = ''
    walletNotice.value = ''
    simulationId.value = null
    book.value = null
    averageEdited.value = false
    syncQuery()
    void refreshSide()
}

watch(
    product,
    () => {
        if (!ready.value || booting) return
        price.value = null
        void (async () => {
            try {
                await loadBars()
                follow(product.value)
                await refreshSide()
            } catch (err) {
                fail(err)
            }
        })()
    },
    { flush: 'sync' },
)

watch(
    () => prefs.value.interval,
    () => {
        if (!ready.value) return
        historyDone.value = false
        void loadBars().catch(fail)
    },
)

function onType(value: ChartType) {
    savePrefs({ ...prefs.value, type: value })
}

function onInterval(value: string) {
    savePrefs({ ...prefs.value, interval: value })
}

function onAddInterval(value: string) {
    const next = rememberInterval(prefs.value, value)
    if (next) savePrefs(next)
}

function onRemoveInterval(value: string) {
    savePrefs(removeCustomInterval(prefs.value, value))
}

function onRange(value: ChartRange) {
    historyDone.value = false
    if (value !== 'custom') {
        fromSec.value = null
        toSec.value = null
    }
    savePrefs({ ...prefs.value, range: value })
    void loadBars().catch(fail)
}

function onFrom(value: string) {
    const unix = unixFromWall(value, zoneName.value)
    if (unix == null) return
    fromSec.value = unix
    if (toSec.value == null) toSec.value = Math.floor(Date.now() / 1000) + 60
    historyDone.value = false
    savePrefs({ ...prefs.value, range: 'custom' })
    void loadBars().catch(fail)
}

function onTo(value: string) {
    const unix = unixFromWall(value, zoneName.value)
    if (unix == null) return
    toSec.value = unix
    if (fromSec.value == null) fromSec.value = unix - 86400
    historyDone.value = false
    savePrefs({ ...prefs.value, range: 'custom' })
    void loadBars().catch(fail)
}

function onZone() {
    const timeZone: ChartZoneMode = prefs.value.timeZone === 'local' ? 'utc' : 'local'
    savePrefs({ ...prefs.value, timeZone })
}

function onBaselineSource(value: BaselineSource) {
    savePrefs({ ...prefs.value, baselineSource: value })
}

function onBaselinePrice(value: number) {
    if (!(value > 0)) return
    savePrefs({ ...prefs.value, baselineSource: 'typed', baselinePrice: value })
}

function onDeltaMode(value: DeltaMode) {
    savePrefs({ ...prefs.value, deltaMode: value })
}

function onHistory() {
    if (!ready.value || historyBusy.value || historyDone.value) return
    const first = candles.value[0]
    if (!first) return
    const current = bounds()
    const span = current.from != null && current.to != null ? Math.max(60, current.to - current.from) : 7 * 86400
    const nextFrom = first.time - span
    if (current.from != null && nextFrom >= current.from) {
        historyDone.value = true
        return
    }
    historyBusy.value = true
    fromSec.value = nextFrom
    toSec.value = current.to ?? Math.floor(Date.now() / 1000) + 60
    prefs.value = { ...prefs.value, range: 'custom' }
    writeChartPrefs(localStorage, prefs.value)
    syncQuery()
    void loadBars(true)
        .catch(fail)
        .finally(() => {
            historyBusy.value = false
        })
}

watch(stream.link, (value) => {
    if (value === 'live') {
        if (sawLive) void loadBars().catch(fail)
        sawLive = true
    }
})

onMounted(async () => {
    width.value = readPanelWidth(localStorage)
    tab.value = readPanelTab(localStorage)
    showTrades.value = readShowTrades(localStorage)
    const located = readChartQuery(route.query as Record<string, unknown>)
    if (located.interval || located.range) {
        prefs.value = {
            ...prefs.value,
            interval: located.interval ?? prefs.value.interval,
            range: located.range ?? prefs.value.range,
        }
    }
    if (located.from != null) fromSec.value = located.from
    if (located.to != null) toSec.value = located.to
    bucket.value = bucketSeconds(prefs.value.interval)
    try {
        await loadSymbols()
        await loadWallets()
        applyQuery()
        await loadBars()
        await refreshSide()
        follow(product.value)
        error.value = ''
        auth.value = false
    } catch (err) {
        fail(err)
    }
    booting = false
    ready.value = true
    timer = window.setInterval(() => {
        void refreshSide()
    }, 2000)
})

onUnmounted(() => {
    window.clearInterval(timer)
    stopStream?.()
})
</script>

<template>
    <div data-testid="live-page" class="flex h-full min-h-0 flex-col lg:flex-row" @click="menu = null">
        <section class="flex h-[55dvh] min-h-0 min-w-0 flex-1 flex-col lg:h-full">
            <div class="flex flex-wrap items-center gap-3 border-b border-gray-200 px-3 py-2 dark:border-gray-800" @click.stop>
                <label class="flex items-center gap-2 text-xs font-medium text-gray-500">
                    Wallet
                    <select
                        data-testid="wallet-select"
                        class="max-w-56 rounded-lg border border-gray-200 bg-white px-2 py-1.5 text-sm text-gray-900 dark:border-gray-700 dark:bg-gray-950 dark:text-white"
                        :value="walletKey"
                        @change="onWallet(($event.target as HTMLSelectElement).value)"
                    >
                        <option value="">Choose a wallet</option>
                        <option v-for="item in wallets" :key="item.key" :value="item.key">{{ item.name }} · {{ item.mode }}</option>
                    </select>
                </label>
                <ProductSelect :products="products" :product="product" @select="onProduct" />
                <p class="text-lg font-semibold text-gray-900 dark:text-white" data-testid="live-price">
                    {{ price == null ? 'Waiting for a public price' : formatMenuPrice(price) }}
                </p>
                <p class="text-xs text-gray-500" data-testid="live-link">{{ linkLabel }}</p>
                <p class="text-xs text-gray-500">{{ prefs.interval }} · public Coinbase</p>
                <p v-if="walletNotice" data-testid="wallet-notice" class="text-xs font-medium text-warning-600">{{ walletNotice }}</p>
                <label class="ms-auto flex items-center gap-2 text-sm text-gray-700 dark:text-gray-200">
                    <input
                        data-testid="show-trades"
                        type="checkbox"
                        class="size-4 rounded border-gray-300"
                        :checked="showTrades"
                        @change="onShowTrades(($event.target as HTMLInputElement).checked)"
                    />
                    Show my trades
                </label>
            </div>
            <ChartToolbar
                :chart-type="prefs.type"
                :favorite-types="prefs.favoriteTypes"
                :interval="prefs.interval"
                :favorite-intervals="prefs.favoriteIntervals"
                :custom-intervals="prefs.customIntervals"
                :range="prefs.range"
                :from-input="fromInput"
                :to-input="toInput"
                :zone-label="zoneLabel"
                :show-volume="prefs.showVolume"
                :baseline-source="prefs.baselineSource"
                :baseline-price="prefs.baselinePrice"
                :delta-mode="prefs.deltaMode"
                :hint="hint"
                :show-baseline="showBaseline"
                @type="onType"
                @interval="onInterval"
                @add-interval="onAddInterval"
                @remove-interval="onRemoveInterval"
                @range="onRange"
                @from="onFrom"
                @to="onTo"
                @zone="onZone"
                @volume="savePrefs({ ...prefs, showVolume: !prefs.showVolume })"
                @baseline-source="onBaselineSource"
                @baseline-price="onBaselinePrice"
                @delta-mode="onDeltaMode"
            />
            <div class="relative min-h-0 flex-1" @click.stop>
                <LiveChart
                    ref="chart"
                    :points="points"
                    :candles="candles"
                    :markers="chartMarkers"
                    :price="price"
                    :lots="showTrades ? shownLots : []"
                    :orders="shownBook?.orders ?? []"
                    :dark="isDark"
                    :fit-key="fitKey"
                    :chart-type="prefs.type"
                    :show-volume="prefs.showVolume"
                    :baseline-price="baseline"
                    :delta-mode="prefs.deltaMode"
                    :time-zone="prefs.timeZone"
                    :interval-seconds="bucket"
                    @menu="menu = $event"
                    @history="onHistory"
                    @baseline="onBaselinePrice"
                />
                <ChartMenu
                    v-if="menu"
                    :hit="menu.hit"
                    :x="menu.x"
                    :y="menu.y"
                    :pending-move-id="pendingMoveId ?? undefined"
                    :cash="shownBook?.cash ?? 0"
                    :held-qty="shownBook?.average.openQty ?? 0"
                    :market-price="price"
                    :wallet-key="walletKey"
                    :base-increment="selected?.baseIncrement ?? 0"
                    :quote-increment="selected?.quoteIncrement ?? 0"
                    :base-min-size="selected?.baseMinSize ?? 0"
                    :min-market-funds="selected?.minMarketFunds ?? 0"
                    @action="onMenu"
                    @close="menu = null"
                    @click.stop
                />
            </div>
        </section>
        <TradingPanel
            :width="width"
            :tab="tab"
            :wallets="wallets"
            :wallet-key="walletKey"
            :book="shownBook"
            :price="price"
            :qty="qty"
            :average-qty="averageQty"
            :busy="busy"
            :status="status"
            :error="error"
            :auth="auth"
            :time-zone="prefs.timeZone"
            @width="rememberWidth"
            @tab="rememberTab"
            @wallet="onWallet"
            @qty="qty = $event"
            @average-qty="onAverageQty"
            @create="onCreate"
            @buy="onBuy"
            @quick-sell="onQuickSell"
            @average="onAverage"
            @place="onPlace"
            @cancel="onCancel"
            @reset="onReset"
        />
    </div>
</template>
