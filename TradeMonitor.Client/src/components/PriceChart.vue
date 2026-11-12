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
    type Time,
    type UTCTimestamp,
} from 'lightweight-charts'
import { nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { markerStyle, type ChartMarker, type PricePoint } from '@/lib/priceSeries'

const props = withDefaults(
    defineProps<{
        points: PricePoint[]
        markers?: ChartMarker[]
        dark?: boolean
        loading?: boolean
        error?: string | null
        empty?: boolean
        emptyLabel?: string
    }>(),
    { markers: () => [], emptyLabel: 'No price marks recorded' },
)

const host = ref<HTMLElement | null>(null)
let chart: IChartApi | null = null
let series: ISeriesApi<'Line'> | null = null
let markerApi: ISeriesMarkersPluginApi<Time> | null = null
let observer: ResizeObserver | null = null

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
    observer?.disconnect()
    observer = null
    markerApi = null
    series = null
    if (chart) {
        chart.remove()
        chart = null
    }
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
        if (typeof ResizeObserver !== 'undefined') {
            observer = new ResizeObserver(() => chart?.timeScale().fitContent())
            observer.observe(host.value)
        }
    } else {
        chart.applyOptions(options(!!props.dark))
    }
    series?.setData(props.points.map((point) => ({ time: point.time as UTCTimestamp, value: point.value })))
    markerApi?.setMarkers(
        (props.markers ?? []).map((marker) => {
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

onMounted(() => {
    void render()
})

watch(
    () => [props.points, props.markers, props.dark, props.empty, props.loading, props.error],
    () => {
        void render()
    },
    { deep: true },
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
    <div v-else ref="host" class="h-[320px] w-full min-w-0" />
</template>
