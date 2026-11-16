<script setup lang="ts">
// TradingView lightweight-charts v5. The chart instance stays in a plain variable,
// created when the series is shown and removed on unmount.
import {
    ColorType,
    LineSeries,
    createChart,
    createSeriesMarkers,
    type IChartApi,
    type ISeriesApi,
    type ISeriesMarkersPluginApi,
    type MouseEventParams,
    type Time,
    type UTCTimestamp,
} from 'lightweight-charts'
import { nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { buildTimeIndex, createFrameScheduler, edgePaint, formatLineHover, indexMarkerText, nearestBar, unchangedPrefix } from '@/lib/chartHover'
import { markerStyle, type ChartMarker, type PricePoint } from '@/lib/priceSeries'

const props = withDefaults(
    defineProps<{
        points: PricePoint[]
        compare?: PricePoint[]
        markers?: ChartMarker[]
        dark?: boolean
        loading?: boolean
        error?: string | null
        empty?: boolean
        emptyLabel?: string
    }>(),
    { compare: () => [], markers: () => [], emptyLabel: 'No price marks recorded' },
)

const host = ref<HTMLElement | null>(null)
const hover = ref<{ x: number; y: number; text: string } | null>(null)
let chart: IChartApi | null = null
let series: ISeriesApi<'Line'> | null = null
let compareSeries: ISeriesApi<'Line'> | null = null
let markerApi: ISeriesMarkersPluginApi<Time> | null = null
let observer: ResizeObserver | null = null
let painted: PricePoint[] = []
let paintedCompare: PricePoint[] = []
let markerKey = ''
let barIndex = new Map<number, number>()
let markerIndex = new Map<number, string[]>()
let indexedCount = -1
let crosshairHooked = false
let pendingHover: MouseEventParams<Time> | null = null
const hoverFrames = createFrameScheduler()

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
        grid: {
            vertLines: { color: grid },
            horzLines: { color: grid },
        },
        rightPriceScale: { borderVisible: false },
        timeScale: { borderVisible: false, secondsVisible: false },
    }
}

function destroy() {
    hoverFrames.cancel()
    if (chart && crosshairHooked) chart.unsubscribeCrosshairMove(onCrosshair)
    crosshairHooked = false
    observer?.disconnect()
    observer = null
    markerApi = null
    compareSeries = null
    series = null
    painted = []
    paintedCompare = []
    markerKey = ''
    indexedCount = -1
    hover.value = null
    if (chart) {
        chart.remove()
        chart = null
    }
}

function applyPoints(target: ISeriesApi<'Line'>, previous: PricePoint[], next: PricePoint[], fit: boolean) {
    const kind = edgePaint(previous.length, previous[0]?.time ?? 0, previous[previous.length - 1]?.time ?? 0, next)
    const last = next[next.length - 1]
    const prefix = kind === 'append' ? previous.length : kind === 'last' ? Math.max(0, previous.length - 1) : 0
    if ((kind === 'append' || kind === 'last') && last && !fit && unchangedPrefix(previous, next, prefix)) {
        target.update({ time: last.time as UTCTimestamp, value: last.value })
        return
    }
    target.setData(next.map((point) => ({ time: point.time as UTCTimestamp, value: point.value })))
}

function markerSignature(marks: ChartMarker[]): string {
    let key = String(marks.length)
    for (const mark of marks) key += `|${mark.time}:${mark.side}:${mark.text}`
    return key
}

function onCrosshair(param: MouseEventParams<Time>) {
    pendingHover = param
    hoverFrames.schedule(paintHover)
}

function paintHover() {
    const param = pendingHover
    const point = param?.point
    const time = param && typeof param.time === 'number' ? param.time : null
    if (!point || time == null || painted.length === 0) {
        hover.value = null
        return
    }
    if (painted.length !== indexedCount) {
        barIndex = buildTimeIndex(painted)
        indexedCount = painted.length
    }
    const row = barIndex.get(time) != null ? painted[barIndex.get(time)!] : nearestBar(painted, time)
    if (!row) {
        hover.value = null
        return
    }
    hover.value = { x: point.x, y: point.y, text: formatLineHover(row.value, markerIndex.get(row.time) ?? []) }
}

async function render() {
    await nextTick()
    const show = !props.loading && !props.error && !props.empty && props.points.length > 0
    if (!show || !host.value) {
        destroy()
        return
    }
    if (!chart) {
        chart = createChart(host.value, options(!!props.dark))
        series = chart.addSeries(LineSeries, { color: '#465fff', lineWidth: 2, priceLineVisible: false })
        markerApi = createSeriesMarkers(series)
        chart.subscribeCrosshairMove(onCrosshair)
        crosshairHooked = true
        if (typeof ResizeObserver !== 'undefined') {
            observer = new ResizeObserver(() => chart?.timeScale().fitContent())
            observer.observe(host.value)
        }
    } else {
        chart.applyOptions(options(!!props.dark))
    }
    const points = props.points
    const refit = painted.length === 0 || points.length === 0 || points[0]?.time !== painted[0]?.time || points[points.length - 1]?.time !== painted[painted.length - 1]?.time
    if (series) applyPoints(series, painted, points, refit)
    painted = points
    indexedCount = -1
    const compare = props.compare ?? []
    if (compare.length === 0) {
        if (compareSeries && chart) {
            chart.removeSeries(compareSeries)
            compareSeries = null
            paintedCompare = []
        }
    } else if (chart) {
        if (!compareSeries) {
            compareSeries = chart.addSeries(LineSeries, { color: '#f79009', lineWidth: 2, priceLineVisible: false })
            paintedCompare = []
        }
        const compareFit = paintedCompare.length === 0 || compare[0]?.time !== paintedCompare[0]?.time
        applyPoints(compareSeries, paintedCompare, compare, compareFit)
        paintedCompare = compare
    }
    const marks = props.markers ?? []
    const nextMarkerKey = markerSignature(marks)
    if (nextMarkerKey !== markerKey) {
        markerKey = nextMarkerKey
        markerIndex = indexMarkerText(marks)
        markerApi?.setMarkers(
            marks.map((marker) => {
                const style = markerStyle(marker.side)
                return {
                    time: marker.time as UTCTimestamp,
                    position: style.position,
                    shape: style.shape,
                    color: style.color,
                    text: marker.text,
                }
            }),
        )
    }
    if (refit) {
        chart.timeScale().fitContent()
        const paintedDark = !!props.dark
        requestAnimationFrame(() =>
            requestAnimationFrame(() => {
                if (!chart || paintedDark !== !!props.dark) return
                chart.applyOptions(options(paintedDark))
                chart.timeScale().fitContent()
            }),
        )
    }
}

onMounted(() => {
    void render()
})

watch(
    () => [props.points, props.compare, props.markers, props.dark, props.empty, props.loading, props.error],
    () => {
        void render()
    },
)

onUnmounted(destroy)
</script>

<template>
    <div v-if="loading" class="h-[320px] animate-pulse rounded-xl bg-gray-100 dark:bg-gray-800" role="status">
        <span class="sr-only">Loading price chart</span>
    </div>
    <p v-else-if="error" class="flex h-[320px] items-center justify-center text-sm text-error-600 dark:text-error-400" role="alert">
        {{ error }}
    </p>
    <p v-else-if="empty || points.length === 0" class="flex h-[320px] items-center justify-center px-4 text-center text-sm text-gray-500 dark:text-gray-400">
        {{ emptyLabel }}
    </p>
    <div v-else class="relative h-[320px] w-full min-w-0">
        <div ref="host" class="h-full w-full" />
        <p
            v-if="hover"
            data-testid="price-hover"
            class="pointer-events-none absolute z-20 max-w-64 whitespace-pre rounded bg-white/95 px-2 py-1 text-[11px] font-medium text-gray-800 shadow dark:bg-gray-950/95 dark:text-gray-100"
            :style="{ left: `${hover.x + 12}px`, top: `${hover.y + 12}px` }"
        >
            {{ hover.text }}
        </p>
    </div>
</template>
