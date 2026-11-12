<script setup lang="ts">
import { ColorType, LineSeries, LineStyle, createChart, type IChartApi, type IPriceLine, type ISeriesApi, type UTCTimestamp } from 'lightweight-charts'
import { nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import type { LiveOrder } from '@/api/live'
import { hitTest, type ChartHit, type ChartLine } from '@/lib/chartMenu'
import type { ChartTradeMark } from '@/lib/tradeMarkers'
import type { SeriesPoint } from '@/lib/liveSeries'

const props = defineProps<{
    points: SeriesPoint[]
    markers: ChartTradeMark[]
    price: number | null
    lots: { tradeId: number; qty: number; buyPrice: number; unrealizedPnl: number | null }[]
    orders: LiveOrder[]
    dark?: boolean
    fitKey: string
}>()

const emit = defineEmits<{ menu: [payload: { x: number; y: number; hit: ChartHit }] }>()

const host = ref<HTMLElement | null>(null)
let chart: IChartApi | null = null
let series: ISeriesApi<'Line'> | null = null
let lines: IPriceLine[] = []
let marks: { kind: 'lot' | 'order'; id: number; price: number }[] = []
let observer: ResizeObserver | null = null
let fitted = ''
let drawnCount = 0
let drawnFirst = 0
const timeAxisHeight = 28
const arrows = ref<{ id: number; x: number; y: number; side: 'buy' | 'sell'; simulated: boolean; text: string; color: string }[]>([])

function options(dark: boolean) {
    const text = dark ? '#98a2b3' : '#667085'
    const grid = dark ? '#1d2939' : '#f2f4f7'
    return {
        autoSize: true,
        layout: {
            background: { type: ColorType.Solid, color: 'transparent' },
            textColor: text,
            fontFamily: 'Outfit, ui-sans-serif, system-ui, sans-serif',
        },
        grid: { vertLines: { color: grid }, horzLines: { color: grid } },
        rightPriceScale: { borderVisible: false },
        timeScale: { borderVisible: false, secondsVisible: false },
        handleScroll: true,
        handleScale: true,
    }
}

function seriesData() {
    const rows = props.points.filter((point) => point.time > 0 && point.value > 0).map((point) => ({ time: point.time as UTCTimestamp, value: point.value }))
    if (rows.length > 0) return rows
    if (props.price == null || props.price <= 0) return []
    const bucket = Math.floor(Date.now() / 1000 / 60) * 60
    return [
        { time: (bucket - 60) as UTCTimestamp, value: props.price },
        { time: bucket as UTCTimestamp, value: props.price },
    ]
}

function placeMarkers() {
    if (!series || !chart) {
        arrows.value = []
        return
    }
    const placed = []
    for (const mark of props.markers) {
        const x = chart.timeScale().timeToCoordinate(mark.time as UTCTimestamp)
        const y = series.priceToCoordinate(mark.price)
        if (x == null || y == null || Number.isNaN(x) || Number.isNaN(y)) continue
        placed.push({
            id: mark.id,
            x,
            y: mark.side === 'buy' ? y + 6 : y - 16,
            side: mark.side,
            simulated: mark.simulated,
            text: mark.text,
            color: mark.side === 'buy' ? '#039855' : '#d92d20',
        })
    }
    arrows.value = placed
}

function clearLines() {
    if (!series) return
    for (const line of lines) series.removePriceLine(line)
    lines = []
    marks = []
}

function drawLines() {
    if (!series) return
    clearLines()
    for (const lot of props.lots) {
        if (lot.buyPrice <= 0) continue
        const losing = lot.unrealizedPnl != null && lot.unrealizedPnl < 0
        lines.push(
            series.createPriceLine({
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
        const stop = order.type === 'StopLoss'
        const buy = order.side === 'Buy'
        lines.push(
            series.createPriceLine({
                price: order.triggerPrice,
                color: stop ? '#f79009' : buy ? '#12b76a' : '#f04438',
                lineWidth: 1,
                lineStyle: order.type === 'Limit' || stop ? LineStyle.Dashed : LineStyle.Solid,
                axisLabelVisible: true,
                title: order.type === 'StopLoss' ? 'Stop' : order.type === 'TakeProfit' ? 'Target' : order.side === 'Buy' ? 'Limit buy' : 'Limit sell',
            }),
        )
        marks.push({ kind: 'order', id: order.id, price: order.triggerPrice })
    }
}

async function render() {
    await nextTick()
    const data = seriesData()
    if (!host.value || data.length === 0) return
    if (!chart) {
        chart = createChart(host.value, options(!!props.dark))
        series = chart.addSeries(LineSeries, { color: '#465fff', lineWidth: 2, priceLineVisible: false })
        if (typeof ResizeObserver !== 'undefined') {
            observer = new ResizeObserver(() => chart?.applyOptions({ autoSize: true }))
            observer.observe(host.value)
        }
    } else {
        chart.applyOptions(options(!!props.dark))
    }
    const first = data[0]?.time ?? 0
    const append = drawnCount > 0 && data[0]?.time === drawnFirst && data.length >= drawnCount && data.length <= drawnCount + 1
    if (append) series?.update(data[data.length - 1]!)
    else series?.setData(data)
    drawnCount = data.length
    drawnFirst = first
    drawLines()
    placeMarkers()
    if (fitted !== props.fitKey) {
        fitted = props.fitKey
        chart.timeScale().fitContent()
    }
}

function onMenu(event: MouseEvent) {
    if (!series || !host.value) return
    event.preventDefault()
    const rect = host.value.getBoundingClientRect()
    const y = event.clientY - rect.top
    const raw = series.coordinateToPrice(y)
    const price = raw == null || Number.isNaN(Number(raw)) ? null : Number(raw)
    const hits: ChartLine[] = []
    for (const mark of marks) {
        const lineY = series.priceToCoordinate(mark.price)
        if (lineY == null) continue
        hits.push({ kind: mark.kind, id: mark.id, y: lineY })
    }
    const hit = hitTest({ y, height: rect.height, timeAxisHeight, price, lines: hits })
    if (!hit) return
    emit('menu', { x: event.clientX, y: event.clientY, hit })
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

defineExpose({ zoom, fit })

onMounted(() => {
    void render()
})

watch(
    () => [props.points, props.price, props.lots, props.orders, props.markers, props.dark, props.fitKey],
    () => {
        void render()
    },
    { deep: true },
)

onUnmounted(() => {
    observer?.disconnect()
    chart?.remove()
    chart = null
    series = null
})
</script>

<template>
    <div class="relative h-full w-full">
        <div ref="host" data-testid="live-chart" class="h-full w-full" @contextmenu="onMenu" />
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
                <title>{{ arrow.text }}</title>
                <polygon
                    :points="arrow.side === 'buy' ? '0,10 6,0 12,10' : '0,0 6,10 12,0'"
                    :fill="arrow.simulated ? 'none' : arrow.color"
                    :stroke="arrow.color"
                    :stroke-dasharray="arrow.simulated ? '2 1' : undefined"
                    stroke-width="1.5"
                />
            </svg>
        </div>
        <ul class="sr-only" data-testid="trade-markers">
            <li v-for="mark in markers" :key="mark.id" :data-side="mark.side" :data-simulated="String(mark.simulated)">{{ mark.text }}</li>
        </ul>
    </div>
</template>
