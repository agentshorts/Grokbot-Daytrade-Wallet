<script setup lang="ts">
import { ColorType, LineSeries, LineStyle, createChart, type IChartApi, type IPriceLine, type ISeriesApi, type UTCTimestamp } from 'lightweight-charts'
import { nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import type { LiveCandle, LiveOrder } from '@/api/live'
import { hitTest, type ChartHit, type ChartLine } from '@/lib/chartMenu'

const props = defineProps<{
    candles: LiveCandle[]
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
const timeAxisHeight = 28

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

function points() {
    const rows = props.candles
        .filter((candle) => candle.start > 0)
        .map((candle) => ({ time: candle.start as UTCTimestamp, value: candle.close }))
    if (rows.length > 0) return rows
    if (props.price == null || props.price <= 0) return []
    const now = Math.floor(Date.now() / 1000)
    return [
        { time: (now - 300) as UTCTimestamp, value: props.price },
        { time: now as UTCTimestamp, value: props.price },
    ]
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
    const data = points()
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
    series?.setData(data)
    drawLines()
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
    () => [props.candles, props.price, props.lots, props.orders, props.dark, props.fitKey],
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
    <div ref="host" data-testid="live-chart" class="h-full w-full" @contextmenu="onMenu" />
</template>
