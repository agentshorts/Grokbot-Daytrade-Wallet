<script setup lang="ts">
import {
    AreaSeries,
    BarSeries,
    BaselineSeries,
    CandlestickSeries,
    ColorType,
    HistogramSeries,
    LineSeries,
    LineStyle,
    LineType,
    createChart,
    createSeriesMarkers,
    type IChartApi,
    type IPriceLine,
    type Time,
    type UTCTimestamp,
} from 'lightweight-charts'
import { nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import type { LiveOrder } from '@/api/live'
import { hitTest, type ChartHit, type ChartLine } from '@/lib/chartMenu'
import { wantsOlderHistory } from '@/lib/chartRange'
import { deltaPoints, priceBars, showsDeltaPane, usesOhlc, type CandleBar, type ChartType, type DeltaMode } from '@/lib/chartSeries'
import { chartZone, formatChartCrosshair, formatChartTick, type ChartZoneMode } from '@/lib/chartTime'
import type { SeriesPoint } from '@/lib/liveSeries'
import type { PriceZone } from '@/lib/atrBands'
import { stackLabelYs, type ChartGuide } from '@/lib/swingLevels'
import type { ChartTradeMark } from '@/lib/tradeMarkers'

interface Plot {
    setData: (data: readonly object[]) => void
    update: (bar: object) => void
    applyOptions: (options: object) => void
    createPriceLine: (options: object) => IPriceLine
    removePriceLine: (line: IPriceLine) => void
    coordinateToPrice: (y: number) => number | null
    priceToCoordinate: (price: number) => number | null
}

const props = withDefaults(
    defineProps<{
        points: SeriesPoint[]
        candles?: CandleBar[]
        markers: ChartTradeMark[]
        price: number | null
        lots: { tradeId: number; qty: number; buyPrice: number; unrealizedPnl: number | null }[]
        orders: LiveOrder[]
        dark?: boolean
        fitKey: string
        chartType?: ChartType
        showVolume?: boolean
        baselinePrice?: number | null
        deltaMode?: DeltaMode
        timeZone?: ChartZoneMode
        zoneName?: string
        intervalSeconds?: number
        guides?: ChartGuide[]
        zones?: PriceZone[]
    }>(),
    {
        chartType: 'candle',
        showVolume: false,
        baselinePrice: null,
        deltaMode: 'change',
        timeZone: 'local',
        zoneName: '',
        intervalSeconds: 300,
        guides: () => [],
        zones: () => [],
    },
)

const emit = defineEmits<{
    menu: [payload: { x: number; y: number; hit: ChartHit }]
    history: []
    baseline: [price: number]
}>()

const host = ref<HTMLElement | null>(null)
const baselineY = ref<number | null>(null)
let chart: IChartApi | null = null
let main: Plot | null = null
let delta: Plot | null = null
let volume: Plot | null = null
let plots: object[] = []
let lines: IPriceLine[] = []
let marks: { kind: 'lot' | 'order' | 'level'; id: number; price: number }[] = []
let stepMarkers: { setMarkers: (markers: object[]) => void } | null = null
let observer: ResizeObserver | null = null
let fitted = ''
let built = ''
let drawnCount = 0
let drawnFirst = 0
let historyHooked = false
const timeAxisHeight = 28
const arrows = ref<{ id: number; x: number; y: number; side: 'buy' | 'sell'; simulated: boolean; text: string; color: string; clock: string }[]>([])
const guideLabels = ref<{ id: string; y: number; text: string; color: string; title: string }[]>([])
const zoneRects = ref<{ id: string; top: number; height: number; color: string }[]>([])

function zone(): string {
    return chartZone(props.timeZone, props.zoneName || undefined)
}

function unixOf(time: Time): number {
    return typeof time === 'number' ? time : 0
}

function options(dark: boolean) {
    const text = dark ? '#98a2b3' : '#667085'
    const grid = dark ? '#1d2939' : '#f2f4f7'
    const timeZone = zone()
    return {
        autoSize: true,
        layout: {
            background: { type: ColorType.Solid, color: 'transparent' },
            textColor: text,
            fontFamily: 'Outfit, ui-sans-serif, system-ui, sans-serif',
        },
        grid: { vertLines: { color: grid }, horzLines: { color: grid } },
        rightPriceScale: { borderVisible: false },
        timeScale: {
            borderVisible: false,
            secondsVisible: props.intervalSeconds < 3600,
            tickMarkFormatter: (time: Time) => formatChartTick(unixOf(time), timeZone, props.intervalSeconds),
        },
        localization: {
            timeFormatter: (time: Time) => formatChartCrosshair(unixOf(time), timeZone),
        },
        handleScroll: true,
        handleScale: true,
    }
}

function sourceBars(): CandleBar[] {
    const rows = props.candles?.length
        ? props.candles.filter((bar) => bar.time > 0 && bar.close > 0)
        : props.points.filter((point) => point.time > 0 && point.value > 0).map((point) => ({
              time: point.time,
              open: point.value,
              high: point.value,
              low: point.value,
              close: point.value,
              volume: 0,
          }))
    if (rows.length > 0) return rows
    if (props.price == null || props.price <= 0) return []
    const bucket = Math.floor(Date.now() / 1000 / 60) * 60
    return [synthetic(bucket - 60), synthetic(bucket)]
}

function synthetic(time: number): CandleBar {
    const price = props.price ?? 0
    return { time, open: price, high: price, low: price, close: price, volume: 0 }
}

function linePoint(bar: CandleBar) {
    return { time: bar.time as UTCTimestamp, value: bar.close }
}

function ohlcPoint(bar: CandleBar) {
    return { time: bar.time as UTCTimestamp, open: bar.open, high: bar.high, low: bar.low, close: bar.close }
}

function volumePoint(bar: CandleBar) {
    return { time: bar.time as UTCTimestamp, value: bar.volume, color: bar.close >= bar.open ? '#12b76a' : '#f04438' }
}

function baselineOptions() {
    const price = props.baselinePrice && props.baselinePrice > 0 ? props.baselinePrice : sourceBars()[0]?.close || 0
    return {
        baseValue: { type: 'price', price },
        topLineColor: '#12b76a',
        topFillColor1: 'rgba(18,183,106,0.28)',
        topFillColor2: 'rgba(18,183,106,0.04)',
        bottomLineColor: '#f04438',
        bottomFillColor1: 'rgba(240,68,56,0.04)',
        bottomFillColor2: 'rgba(240,68,56,0.28)',
        lineWidth: 2,
        priceLineVisible: false,
    }
}

function add(definition: unknown, seriesOptions: object, pane = 0): Plot {
    const series = chart!.addSeries(definition as never, seriesOptions as never, pane) as unknown as Plot
    plots.push(series)
    return series
}

function clearSeries() {
    if (chart) {
        for (const series of plots) chart.removeSeries(series as never)
    }
    plots = []
    main = null
    delta = null
    volume = null
    stepMarkers = null
    lines = []
    marks = []
    drawnCount = 0
}

function seriesShape(): string {
    return `${props.chartType}|${props.showVolume ? 1 : 0}|${props.deltaMode}`
}

function buildSeries(data: CandleBar[]) {
    clearSeries()
    const price = priceBars(props.chartType, data)
    if (props.chartType === 'bars' || props.chartType === 'candle' || props.chartType === 'heikin') {
        const definition = props.chartType === 'bars' ? BarSeries : CandlestickSeries
        main = add(definition, {
            upColor: '#12b76a',
            downColor: '#f04438',
            borderVisible: false,
            wickUpColor: '#12b76a',
            wickDownColor: '#f04438',
            priceLineVisible: false,
        })
        main.setData(price.map(ohlcPoint))
    } else if (props.chartType === 'area') {
        main = add(AreaSeries, {
            lineColor: '#465fff',
            topColor: 'rgba(70,95,255,0.35)',
            bottomColor: 'rgba(70,95,255,0.02)',
            lineWidth: 2,
            priceLineVisible: false,
        })
        main.setData(price.map(linePoint))
    } else if (props.chartType === 'baseline' || props.chartType === 'vertex-baseline') {
        main = add(BaselineSeries, baselineOptions())
        main.setData(price.map(linePoint))
    } else {
        main = add(LineSeries, {
            color: '#465fff',
            lineWidth: 2,
            priceLineVisible: false,
            lineType: props.chartType === 'vertex-step' ? LineType.WithSteps : LineType.Simple,
        })
        main.setData(price.map(linePoint))
        if (props.chartType === 'vertex-step') {
            stepMarkers = createSeriesMarkers(
                main as never,
                price.map((bar) => ({ time: bar.time as UTCTimestamp, position: 'inBar', color: '#465fff', shape: 'circle' })),
            ) as unknown as { setMarkers: (markers: object[]) => void }
        }
    }
    if (showsDeltaPane(props.chartType)) {
        delta = add(HistogramSeries, { priceFormat: { type: 'price', precision: 2, minMove: 0.01 } }, 1)
        delta.setData(deltaPoints(data, props.deltaMode).map((point) => ({ ...point, time: point.time as UTCTimestamp })))
    }
    if (props.showVolume) {
        volume = add(HistogramSeries, { priceFormat: { type: 'volume' } }, showsDeltaPane(props.chartType) ? 2 : 1)
        volume.setData(data.map(volumePoint))
    }
    drawnCount = data.length
    drawnFirst = data[0]?.time ?? 0
}

function paint(data: CandleBar[]) {
    const price = priceBars(props.chartType, data)
    const append = drawnCount > 0 && data[0]?.time === drawnFirst && data.length === drawnCount + 1 && props.chartType !== 'heikin'
    const last = price[price.length - 1]
    if (append && last && main) {
        main.update(usesOhlc(props.chartType) ? ohlcPoint(last) : linePoint(last))
        if (delta) {
            const point = deltaPoints(data, props.deltaMode).at(-1)
            if (point) delta.update({ ...point, time: point.time as UTCTimestamp })
        }
        if (volume) volume.update(volumePoint(data[data.length - 1]!))
    } else {
        main?.setData(usesOhlc(props.chartType) ? price.map(ohlcPoint) : price.map(linePoint))
        delta?.setData(deltaPoints(data, props.deltaMode).map((point) => ({ ...point, time: point.time as UTCTimestamp })))
        volume?.setData(data.map(volumePoint))
        if (stepMarkers) {
            stepMarkers.setMarkers(price.map((bar) => ({ time: bar.time as UTCTimestamp, position: 'inBar', color: '#465fff', shape: 'circle' })))
        }
    }
    if (props.chartType === 'baseline' || props.chartType === 'vertex-baseline') main?.applyOptions(baselineOptions())
    drawnCount = data.length
    drawnFirst = data[0]?.time ?? 0
}

function hookHistory() {
    const scale = chart?.timeScale() as { subscribeVisibleLogicalRangeChange?: (cb: (range: { from: number; to: number } | null) => void) => void } | undefined
    if (!scale || historyHooked || typeof scale.subscribeVisibleLogicalRangeChange !== 'function') return
    historyHooked = true
    scale.subscribeVisibleLogicalRangeChange((range) => {
        if (wantsOlderHistory(range)) emit('history')
    })
}

function placeMarkers() {
    if (!main || !chart) {
        arrows.value = []
        placeGuideLabels()
        placeZones()
        return
    }
    const timeZone = zone()
    const placed = []
    for (const mark of props.markers) {
        const x = chart.timeScale().timeToCoordinate(mark.time as UTCTimestamp)
        const y = main.priceToCoordinate(mark.price)
        if (x == null || y == null || Number.isNaN(x) || Number.isNaN(y)) continue
        placed.push({
            id: mark.id,
            x,
            y: mark.side === 'buy' ? y + 6 : y - 16,
            side: mark.side,
            simulated: mark.simulated,
            text: mark.text,
            color: mark.side === 'buy' ? '#039855' : '#d92d20',
            clock: formatChartCrosshair(mark.time, timeZone),
        })
    }
    arrows.value = placed
    placeGuideLabels()
    placeZones()
}

function placeZones() {
    if (!main) {
        zoneRects.value = []
        return
    }
    const placed = []
    for (const zone of props.zones) {
        const y1 = main.priceToCoordinate(zone.top)
        const y2 = main.priceToCoordinate(zone.bottom)
        if (y1 == null || y2 == null || Number.isNaN(y1) || Number.isNaN(y2)) continue
        const top = Math.min(y1, y2)
        const height = Math.abs(y2 - y1)
        if (height < 1) continue
        placed.push({ id: zone.id, top, height, color: zone.color })
    }
    zoneRects.value = placed
}

function nudgeOverlay() {
    requestAnimationFrame(() => placeMarkers())
}

function placeGuideLabels() {
    if (!main) {
        guideLabels.value = []
        return
    }
    const rows = []
    for (const guide of props.guides) {
        if (!guide.label) continue
        const y = main.priceToCoordinate(guide.price)
        if (y == null || Number.isNaN(y)) continue
        rows.push({ id: guide.id, y, text: guide.label, color: guide.color, title: guide.title })
    }
    const ys = stackLabelYs(rows)
    guideLabels.value = rows.map((row, index) => ({ ...row, y: ys[index] ?? row.y }))
}

function clearLines() {
    if (!main) return
    for (const line of lines) main.removePriceLine(line)
    lines = []
    marks = []
}

function drawLines() {
    if (!main) return
    clearLines()
    for (const lot of props.lots) {
        if (lot.buyPrice <= 0) continue
        const losing = lot.unrealizedPnl != null && lot.unrealizedPnl < 0
        lines.push(
            main.createPriceLine({
                price: lot.buyPrice,
                color: losing ? '#f04438' : '#039855',
                lineWidth: 1,
                lineStyle: LineStyle.Solid,
                axisLabelVisible: true,
                title: `${lot.qty} @ ${lot.buyPrice}`,
            }),
        )
        marks.push({ kind: 'lot', id: lot.tradeId, price: lot.buyPrice })
    }
    for (const order of props.orders) {
        if (order.triggerPrice == null || order.triggerPrice <= 0) continue
        const stop = order.type === 'StopLoss' || order.type === 'StopBuy'
        const buy = order.side === 'Buy'
        lines.push(
            main.createPriceLine({
                price: order.triggerPrice,
                color: stop ? '#f79009' : buy ? '#12b76a' : '#f04438',
                lineWidth: 1,
                lineStyle: order.type === 'Limit' || stop ? LineStyle.Dashed : LineStyle.Solid,
                axisLabelVisible: true,
                title: order.type === 'StopLoss' || order.type === 'StopBuy' ? 'Stop' : order.type === 'TakeProfit' ? 'Target' : order.side === 'Buy' ? 'Limit buy' : 'Limit sell',
            }),
        )
        marks.push({ kind: 'order', id: order.id, price: order.triggerPrice })
    }
    for (const guide of props.guides) {
        if (guide.price <= 0) continue
        const width = Math.min(4, Math.max(1, guide.width)) as 1 | 2 | 3 | 4
        lines.push(
            main.createPriceLine({
                price: guide.price,
                color: guide.color,
                lineWidth: width,
                lineStyle: LineStyle.Dashed,
                axisLabelVisible: false,
                title: '',
            }),
        )
        if (guide.hit !== false) marks.push({ kind: 'level', id: 0, price: guide.price })
    }
}

function placeBaselineHandle() {
    if (!main || (props.chartType !== 'baseline' && props.chartType !== 'vertex-baseline')) {
        baselineY.value = null
        return
    }
    const price = props.baselinePrice && props.baselinePrice > 0 ? props.baselinePrice : sourceBars()[0]?.close
    const y = price == null ? null : main.priceToCoordinate(price)
    baselineY.value = y == null || Number.isNaN(y) ? null : y
}

async function render() {
    await nextTick()
    const data = sourceBars()
    if (!host.value || data.length === 0) return
    if (!chart) {
        chart = createChart(host.value, options(!!props.dark))
        hookHistory()
        if (typeof ResizeObserver !== 'undefined') {
            observer = new ResizeObserver(() => chart?.applyOptions({ autoSize: true }))
            observer.observe(host.value)
        }
    } else {
        chart.applyOptions(options(!!props.dark))
    }
    const shape = seriesShape()
    if (!main || built !== shape) {
        buildSeries(data)
        built = shape
    } else {
        paint(data)
    }
    drawLines()
    placeMarkers()
    placeBaselineHandle()
    if (fitted !== props.fitKey) {
        fitted = props.fitKey
        chart.timeScale().fitContent()
    }
}

function onMenu(event: MouseEvent) {
    if (!main || !host.value) return
    event.preventDefault()
    const rect = host.value.getBoundingClientRect()
    const y = event.clientY - rect.top
    const raw = main.coordinateToPrice(y)
    const price = raw == null || Number.isNaN(Number(raw)) ? null : Number(raw)
    const hits: ChartLine[] = []
    for (const mark of marks) {
        const lineY = main.priceToCoordinate(mark.price)
        if (lineY == null) continue
        hits.push({ kind: mark.kind, id: mark.id, y: lineY, price: mark.price })
    }
    const hit = hitTest({ y, height: rect.height, timeAxisHeight, price, lines: hits })
    if (!hit) return
    emit('menu', { x: event.clientX, y: event.clientY, hit })
}

function dragBaseline(event: PointerEvent) {
    const move = (next: PointerEvent) => {
        if (!main || !host.value) return
        const rect = host.value.getBoundingClientRect()
        const raw = main.coordinateToPrice(next.clientY - rect.top)
        const price = raw == null ? null : Number(raw)
        if (price == null || !Number.isFinite(price) || price <= 0) return
        emit('baseline', price)
    }
    const stop = () => {
        window.removeEventListener('pointermove', move)
        window.removeEventListener('pointerup', stop)
    }
    window.addEventListener('pointermove', move)
    window.addEventListener('pointerup', stop)
    move(event)
}

function zoom(factor: number) {
    const range = chart?.timeScale().getVisibleLogicalRange()
    if (!chart || !range) return
    const mid = (range.from + range.to) / 2
    const half = ((range.to - range.from) / 2) * factor
    chart.timeScale().setVisibleLogicalRange({ from: mid - half, to: mid + half })
}

function fit() {
    chart?.timeScale().fitContent()
}

function priceToY(price: number): number | null {
    const y = main?.priceToCoordinate(price)
    return y == null || Number.isNaN(y) ? null : y
}

function timeToX(time: number): number | null {
    const x = chart?.timeScale().timeToCoordinate(time as UTCTimestamp)
    return x == null || Number.isNaN(x) ? null : x
}

defineExpose({ zoom, fit, priceToY, timeToX })

onMounted(() => {
    void render()
})

watch(
    () => [props.points, props.candles, props.price, props.lots, props.orders, props.markers, props.guides, props.zones, props.dark, props.fitKey, props.chartType, props.showVolume, props.baselinePrice, props.deltaMode, props.timeZone, props.zoneName, props.intervalSeconds],
    () => {
        void render()
    },
    { deep: true },
)

onUnmounted(() => {
    observer?.disconnect()
    chart?.remove()
    chart = null
    main = null
})
</script>

<template>
    <div class="relative h-full w-full" :data-chart-type="chartType">
        <div ref="host" data-testid="live-chart" class="h-full w-full" @contextmenu="onMenu" @wheel="nudgeOverlay" @pointerup="nudgeOverlay" />
        <div class="pointer-events-none absolute inset-0 overflow-hidden">
            <div v-for="zone in zoneRects" :key="zone.id" data-testid="atr-zone" class="absolute inset-x-0" :style="{ top: `${zone.top}px`, height: `${zone.height}px`, background: zone.color }" />
        </div>
        <button
            v-if="baselineY != null"
            type="button"
            data-testid="baseline-handle"
            class="absolute right-16 z-10 h-4 w-4 rounded-full border border-gray-400 bg-white dark:bg-gray-900"
            :style="{ top: `${baselineY - 8}px` }"
            title="Drag the baseline"
            @pointerdown.stop.prevent="dragBaseline"
        />
        <div class="pointer-events-none absolute inset-0 overflow-hidden">
            <svg
                v-for="arrow in arrows"
                :key="arrow.id"
                class="absolute"
                :style="{ left: `${arrow.x - 6}px`, top: `${arrow.y}px` }"
                width="12"
                height="10"
                viewBox="0 0 12 10"
            >
                <title>{{ arrow.text }} · {{ arrow.clock }}</title>
                <polygon
                    :points="arrow.side === 'buy' ? '0,10 6,0 12,10' : '0,0 6,10 12,0'"
                    :fill="arrow.simulated ? 'none' : arrow.color"
                    :stroke="arrow.color"
                    :stroke-dasharray="arrow.simulated ? '2 1' : undefined"
                    stroke-width="1.5"
                />
            </svg>
        </div>
        <div class="pointer-events-none absolute inset-0 overflow-hidden">
            <p
                v-for="label in guideLabels"
                :key="label.id"
                data-testid="level-label"
                class="absolute right-2 rounded bg-white/80 px-1 text-[11px] font-medium dark:bg-gray-950/80"
                :style="{ top: `${label.y}px`, color: label.color }"
                :title="label.title"
            >
                {{ label.text }}
            </p>
        </div>
        <ul class="sr-only" data-testid="trade-markers">
            <li v-for="mark in markers" :key="mark.id" :data-side="mark.side" :data-simulated="String(mark.simulated)">{{ mark.text }}</li>
        </ul>
    </div>
</template>
