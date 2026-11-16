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
    type MouseEventParams,
    type Time,
    type UTCTimestamp,
} from 'lightweight-charts'
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import type { LiveOrder } from '@/api/live'
import { buildTimeIndex, createFrameScheduler, edgePaint, formatHover, hoverHit, indexMarkerText, type HoverBar } from '@/lib/chartHover'
import { hitTest, type ChartHit, type ChartLine } from '@/lib/chartMenu'
import { captureChartView, logicalRangeToApply, timeRangeToApply, viewToRestore, type ChartView } from '@/lib/chartView'
import { wantsOlderHistory } from '@/lib/chartRange'
import { deltaPoints, heikinAshi, heikinStep, lastDelta, priceBars, showsDeltaPane, usesOhlc, type CandleBar, type ChartType, type DeltaMode } from '@/lib/chartSeries'
import { chartZone, formatChartCrosshair, formatChartTick, type ChartZoneMode } from '@/lib/chartTime'
import type { SeriesPoint } from '@/lib/liveSeries'
import type { PriceZone } from '@/lib/atrBands'
import { forecastLabel, type ForecastStep } from '@/lib/forecast'
import { stackLabelYs, type ChartGuide } from '@/lib/swingLevels'
import type { ChartTradeMark } from '@/lib/tradeMarkers'
import type { StepLadderBar } from '@/lib/stepLadder'

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
        revision?: number
        guides?: ChartGuide[]
        zones?: PriceZone[]
        forecast?: ForecastStep[]
        showStudy?: boolean
        study?: StepLadderBar[]
    }>(),
    {
        chartType: 'candle',
        showVolume: false,
        baselinePrice: null,
        deltaMode: 'change',
        timeZone: 'local',
        zoneName: '',
        intervalSeconds: 300,
        revision: 0,
        guides: () => [],
        zones: () => [],
        forecast: () => [],
        showStudy: false,
        study: () => [],
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
let studyVolume: Plot | null = null
let studyAvg: Plot | null = null
let forecastLine: Plot | null = null
let plots: object[] = []
let lines: IPriceLine[] = []
let marks: { kind: 'lot' | 'order' | 'level'; id: number; price: number }[] = []
let stepMarkers: { setMarkers: (markers: object[]) => void } | null = null
let studyMarkers: { setMarkers: (markers: object[]) => void } | null = null
let observer: ResizeObserver | null = null
let fitted = ''
let built = ''
let drawnCount = 0
let drawnFirst = 0
let drawnLast = 0
let seenRevision = 0
let haPrev: { open: number; close: number } | null = null
let haTail: { open: number; close: number } | null = null
let lineKey = ''
let studyKey = ''
let barIndex = new Map<number, number>()
let indexedCount = -1
let indexedFirst = 0
let indexedLast = 0
let markerIndex = new Map<number, string[]>()
let markerSource: ChartTradeMark[] | null = null
let historyHooked = false
let crosshairHooked = false
let pendingHover: MouseEventParams<Time> | null = null
const hoverFrames = createFrameScheduler()
let lastView: ChartView | null = null
let restoring = false
let suspendView = false
let restoreFrame = 0
const timeAxisHeight = 28
const arrows = ref<{ id: number; x: number; y: number; side: 'buy' | 'sell'; simulated: boolean; text: string; color: string; clock: string }[]>([])
const guideLabels = ref<{ id: string; y: number; text: string; color: string; title: string }[]>([])
const zoneRects = ref<{ id: string; top: number; height: number; color: string }[]>([])
const forecastCones = ref({ outer: '', inner: '' })
const forecastLabels = ref<{ id: string; x: number; y: number; text: string; title: string }[]>([])
const hover = ref<{ x: number; y: number; text: string } | null>(null)
const studyMarks = computed(() => {
    if (!props.showStudy) return []
    return props.study.flatMap((bar) => {
        const rows: { time: number; kind: 'step' | 'dormant' }[] = []
        if (bar.step) rows.push({ time: bar.time, kind: 'step' })
        if (bar.dormant) rows.push({ time: bar.time, kind: 'dormant' })
        return rows
    })
})

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
        : props.points
              .filter((point) => point.time > 0 && point.value > 0)
              .map((point) => ({
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
    studyVolume = null
    studyAvg = null
    forecastLine = null
    stepMarkers = null
    studyMarkers = null
    lines = []
    marks = []
    drawnCount = 0
    drawnLast = 0
    haPrev = null
    haTail = null
    lineKey = ''
    studyKey = ''
    indexedCount = -1
}

function seriesShape(): string {
    return `${props.chartType}|${props.showVolume ? 1 : 0}|${props.deltaMode}|${props.showStudy ? 1 : 0}`
}

function studyPane(): number {
    let pane = 1
    if (showsDeltaPane(props.chartType)) pane += 1
    if (props.showVolume) pane += 1
    return pane
}

function studyMarkerRows() {
    const rows: object[] = []
    for (const bar of props.study) {
        if (bar.step) rows.push({ time: bar.time as UTCTimestamp, position: 'aboveBar', color: '#12b76a', shape: 'arrowUp', size: 2 })
        if (bar.dormant) rows.push({ time: bar.time as UTCTimestamp, position: 'belowBar', color: '#f04438', shape: 'arrowDown', size: 2 })
    }
    return rows
}

function paintStudy(force = false) {
    if (!studyVolume || !studyAvg) return
    const last = props.study[props.study.length - 1]
    const key = `${props.study.length}:${props.study[0]?.time ?? 0}:${last?.time ?? 0}:${last?.volume ?? ''}:${last?.vol24 ?? ''}:${last?.step ? 1 : 0}:${last?.dormant ? 1 : 0}`
    if (!force && key === studyKey) return
    studyKey = key
    studyVolume.setData(props.study.map((bar) => ({ time: bar.time as UTCTimestamp, value: bar.volume })))
    studyAvg.setData(props.study.flatMap((bar) => (bar.vol24 == null ? [] : [{ time: bar.time as UTCTimestamp, value: bar.vol24 }])))
    studyMarkers?.setMarkers(studyMarkerRows())
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
    if (props.showStudy && main) {
        const pane = studyPane()
        const line = { lineWidth: 2, priceLineVisible: false, lastValueVisible: true, priceFormat: { type: 'volume' } }
        studyVolume = add(LineSeries, { ...line, color: '#2e90fa' }, pane)
        studyAvg = add(LineSeries, { ...line, color: '#f79009' }, pane)
        studyMarkers = createSeriesMarkers(main as never, []) as { setMarkers: (markers: object[]) => void }
        paintStudy()
        const panes = (chart as { panes?: () => { setStretchFactor?: (value: number) => void }[] } | null)?.panes?.()
        panes?.[pane]?.setStretchFactor?.(0.3)
    }
    drawnCount = data.length
    drawnFirst = data[0]?.time ?? 0
    drawnLast = data[data.length - 1]?.time ?? 0
    rememberHeikin(price)
}

function rememberHeikin(price: CandleBar[]) {
    if (props.chartType !== 'heikin') {
        haPrev = null
        haTail = null
        return
    }
    const last = price[price.length - 1]
    const prev = price[price.length - 2]
    haTail = last ? { open: last.open, close: last.close } : null
    haPrev = prev ? { open: prev.open, close: prev.close } : null
}

function paintedBar(data: CandleBar[], kind: 'append' | 'last'): CandleBar | null {
    const source = data[data.length - 1]
    if (!source) return null
    if (props.chartType !== 'heikin') return source
    const prev = kind === 'append' ? haTail : haPrev
    return heikinStep(prev, source)
}

function paint(data: CandleBar[]) {
    const force = props.revision !== seenRevision
    const kind = force ? 'replace' : edgePaint(drawnCount, drawnFirst, drawnLast, data)
    const move = kind === 'append' || kind === 'last'
    if (move && main && props.chartType !== 'heikin') {
        const last = data[data.length - 1]
        if (last) {
            main.update(usesOhlc(props.chartType) ? ohlcPoint(last) : linePoint(last))
            if (delta) {
                const point = lastDelta(data, props.deltaMode)
                if (point) delta.update({ ...point, time: point.time as UTCTimestamp })
            }
            if (volume) volume.update(volumePoint(last))
        }
        paintStudy()
    } else if (move && main && props.chartType === 'heikin') {
        const last = paintedBar(data, kind)
        if (last) {
            if (kind === 'append') haPrev = haTail
            haTail = { open: last.open, close: last.close }
            main.update(ohlcPoint(last))
            if (delta) {
                const point = lastDelta(heikinAshi(data), props.deltaMode)
                if (point) delta.update({ ...point, time: point.time as UTCTimestamp })
            }
            if (volume) volume.update(volumePoint(data[data.length - 1]!))
        }
        paintStudy()
    } else {
        const price = priceBars(props.chartType, data)
        main?.setData(usesOhlc(props.chartType) ? price.map(ohlcPoint) : price.map(linePoint))
        delta?.setData(deltaPoints(data, props.deltaMode).map((point) => ({ ...point, time: point.time as UTCTimestamp })))
        volume?.setData(data.map(volumePoint))
        if (stepMarkers) {
            stepMarkers.setMarkers(price.map((bar) => ({ time: bar.time as UTCTimestamp, position: 'inBar', color: '#465fff', shape: 'circle' })))
        }
        rememberHeikin(price)
        paintStudy(true)
    }
    if (props.chartType === 'baseline' || props.chartType === 'vertex-baseline') main?.applyOptions(baselineOptions())
    drawnCount = data.length
    drawnFirst = data[0]?.time ?? 0
    drawnLast = data[data.length - 1]?.time ?? 0
}

function numericTime(range: { from: Time; to: Time } | null | undefined): { from: number; to: number } | null {
    if (!range || typeof range.from !== 'number' || typeof range.to !== 'number') return null
    return { from: range.from, to: range.to }
}

function readView(): ChartView | null {
    if (!chart) return null
    const timeScale = chart.timeScale()
    const logical = typeof timeScale.getVisibleLogicalRange === 'function' ? timeScale.getVisibleLogicalRange() : null
    const time = typeof timeScale.getVisibleRange === 'function' ? numericTime(timeScale.getVisibleRange()) : null
    return captureChartView(logical, time)
}

function rememberView() {
    if (!chart || restoring || suspendView) return
    const current = readView()
    if (current) lastView = current
}

function applyView(view: ChartView) {
    const paint = () => {
        if (!chart) return
        const timeScale = chart.timeScale()
        const logical = logicalRangeToApply(view)
        const time = timeRangeToApply(view)
        restoring = true
        if (logical && typeof timeScale.setVisibleLogicalRange === 'function') timeScale.setVisibleLogicalRange(logical)
        else if (time && typeof timeScale.setVisibleRange === 'function') {
            timeScale.setVisibleRange({ from: time.from as UTCTimestamp, to: time.to as UTCTimestamp })
        }
        restoring = false
    }
    paint()
    lastView = view
    if (typeof requestAnimationFrame !== 'function') return
    cancelAnimationFrame(restoreFrame)
    restoreFrame = requestAnimationFrame(() => {
        paint()
    })
}

function hookHistory() {
    const scale = chart?.timeScale() as { subscribeVisibleLogicalRangeChange?: (cb: (range: { from: number; to: number } | null) => void) => void } | undefined
    if (!scale || historyHooked || typeof scale.subscribeVisibleLogicalRangeChange !== 'function') return
    historyHooked = true
    scale.subscribeVisibleLogicalRangeChange((range) => {
        if (!restoring && !suspendView) {
            const captured = captureChartView(range, chart ? numericTime(chart.timeScale().getVisibleRange?.()) : null)
            if (captured) lastView = captured
        }
        if (!restoring && !suspendView && wantsOlderHistory(range)) emit('history')
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
    placeForecast(props.forecast)
}

function paintForecast() {
    if (!chart) return
    const rows = props.forecast
    if (rows.length === 0) {
        forecastLine?.setData([])
        placeForecast([])
        return
    }
    if (!forecastLine) {
        forecastLine = add(LineSeries, {
            color: '#7a5af8',
            lineWidth: 1,
            lineStyle: LineStyle.Dotted,
            priceLineVisible: false,
            lastValueVisible: false,
            crosshairMarkerVisible: false,
        })
    }
    forecastLine.setData(rows.map((step) => ({ time: step.time as UTCTimestamp, value: step.q50 })))
}

function cone(rows: ForecastStep[], high: 'q90' | 'q70', low: 'q10' | 'q30'): string {
    if (!chart || !main) return ''
    const forward: string[] = []
    const back: string[] = []
    for (const step of rows) {
        const x = chart.timeScale().timeToCoordinate(step.time as UTCTimestamp)
        const yHigh = main.priceToCoordinate(step[high])
        const yLow = main.priceToCoordinate(step[low])
        if (x == null || yHigh == null || yLow == null || Number.isNaN(x) || Number.isNaN(yHigh) || Number.isNaN(yLow)) continue
        forward.push(`${x},${yHigh}`)
        back.push(`${x},${yLow}`)
    }
    if (forward.length < 2) return ''
    return [...forward, ...back.reverse()].join(' ')
}

function placeForecast(rows: ForecastStep[]) {
    if (!chart || !main || rows.length === 0) {
        forecastCones.value = { outer: '', inner: '' }
        forecastLabels.value = []
        return
    }
    forecastCones.value = { outer: cone(rows, 'q90', 'q10'), inner: cone(rows, 'q70', 'q30') }
    const last = rows[rows.length - 1]
    if (!last) {
        forecastLabels.value = []
        return
    }
    const x = chart.timeScale().timeToCoordinate(last.time as UTCTimestamp)
    if (x == null || Number.isNaN(x)) {
        forecastLabels.value = []
        return
    }
    const anchor = props.price ?? last.mean
    const marks = [
        { id: 'p90', price: last.q90, text: forecastLabel('P90', last.q90, anchor) },
        { id: 'p50', price: last.q50, text: forecastLabel('Median', last.q50, anchor) },
        { id: 'p10', price: last.q10, text: forecastLabel('P10', last.q10, anchor) },
    ]
    const placed = marks.flatMap((mark) => {
        const y = main?.priceToCoordinate(mark.price)
        if (y == null || Number.isNaN(y)) return []
        return [{ id: mark.id, x, y, text: mark.text, title: 'model range, not advice' }]
    })
    const ys = stackLabelYs(placed)
    forecastLabels.value = placed.map((row, index) => ({ ...row, y: ys[index] ?? row.y }))
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

function linesSignature(): string {
    const lots = props.lots.map((lot) => `${lot.tradeId}@${lot.buyPrice}:${(lot.unrealizedPnl ?? 0) < 0 ? 1 : 0}`).join(';')
    const orders = props.orders.map((order) => `${order.id}@${order.triggerPrice ?? ''}:${order.type}:${order.side}`).join(';')
    const guides = props.guides.map((guide) => `${guide.id}@${guide.price}:${guide.width}:${guide.color}`).join(';')
    return `${lots}|${orders}|${guides}`
}

function drawLines() {
    if (!main) return
    const nextKey = linesSignature()
    if (nextKey === lineKey && lines.length > 0) return
    lineKey = nextKey
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
                title:
                    order.type === 'StopLoss' || order.type === 'StopBuy'
                        ? 'Stop'
                        : order.type === 'TakeProfit'
                          ? 'Target'
                          : order.side === 'Buy'
                            ? 'Limit buy'
                            : 'Limit sell',
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
    if (typeof cancelAnimationFrame === 'function') cancelAnimationFrame(restoreFrame)
    const refit = fitted !== props.fitKey
    const shape = seriesShape()
    const rebuilding = !main || built !== shape
    rememberView()
    const kept = viewToRestore(rebuilding ? lastView : null, refit)
    await nextTick()
    const data = sourceBars()
    if (!host.value || data.length === 0) return
    if (!chart) {
        chart = createChart(host.value, options(!!props.dark))
        hookHistory()
        hookCrosshair()
        if (typeof ResizeObserver !== 'undefined') {
            observer = new ResizeObserver(() => chart?.applyOptions({ autoSize: true }))
            observer.observe(host.value)
        }
    } else {
        chart.applyOptions(options(!!props.dark))
    }
    if (!main || built !== shape) {
        suspendView = true
        buildSeries(data)
        built = shape
        suspendView = false
    } else {
        paint(data)
    }
    drawLines()
    paintForecast()
    placeMarkers()
    placeBaselineHandle()
    if (fitted !== props.fitKey) {
        fitted = props.fitKey
        chart.timeScale().fitContent()
    } else if (kept) {
        applyView(kept)
    }
    seenRevision = props.revision
}

function barsForHover(): HoverBar[] {
    return sourceBars()
}

function timeIndex(data: HoverBar[]): Map<number, number> {
    const last = data[data.length - 1]?.time ?? 0
    const first = data[0]?.time ?? 0
    if (data.length === indexedCount && first === indexedFirst && last === indexedLast) return barIndex
    barIndex = buildTimeIndex(data)
    indexedCount = data.length
    indexedFirst = first
    indexedLast = last
    return barIndex
}

function markerTexts(): Map<number, string[]> {
    if (markerSource === props.markers) return markerIndex
    markerSource = props.markers
    markerIndex = indexMarkerText(props.markers)
    return markerIndex
}

function onCrosshair(param: MouseEventParams<Time>) {
    pendingHover = param
    hoverFrames.schedule(paintHover)
}

function paintHover() {
    const param = pendingHover
    const point = param?.point
    const time = param && typeof param.time === 'number' ? param.time : null
    if (!point || time == null) {
        hover.value = null
        return
    }
    const data = barsForHover()
    const hit = hoverHit(timeIndex(data), data, markerTexts(), time)
    if (!hit) {
        hover.value = null
        return
    }
    hover.value = { x: point.x, y: point.y, text: formatHover(hit) }
}

function hookCrosshair() {
    if (!chart || crosshairHooked) return
    crosshairHooked = true
    chart.subscribeCrosshairMove(onCrosshair)
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

let rendering = false
let renderAgain = false

function requestRender() {
    if (rendering) {
        renderAgain = true
        return
    }
    rendering = true
    void (async () => {
        do {
            renderAgain = false
            await render()
        } while (renderAgain)
        rendering = false
    })()
}

onMounted(() => {
    requestRender()
})

watch(
    () => [
        props.points,
        props.candles,
        props.price,
        props.lots,
        props.orders,
        props.markers,
        props.guides,
        props.zones,
        props.forecast,
        props.study,
        props.showStudy,
        props.dark,
        props.fitKey,
        props.chartType,
        props.showVolume,
        props.baselinePrice,
        props.deltaMode,
        props.timeZone,
        props.zoneName,
        props.intervalSeconds,
        props.revision,
    ],
    () => {
        requestRender()
    },
)

onUnmounted(() => {
    if (typeof cancelAnimationFrame === 'function') cancelAnimationFrame(restoreFrame)
    hoverFrames.cancel()
    if (chart && crosshairHooked) chart.unsubscribeCrosshairMove(onCrosshair)
    observer?.disconnect()
    chart?.remove()
    chart = null
    main = null
})
</script>

<template>
    <div class="relative h-full w-full" :data-chart-type="chartType">
        <div ref="host" data-testid="live-chart" class="h-full w-full" @contextmenu="onMenu" @wheel="nudgeOverlay" @pointerup="nudgeOverlay" />
        <p
            v-if="hover"
            data-testid="chart-hover"
            class="pointer-events-none absolute z-20 max-w-64 whitespace-pre rounded bg-white/95 px-2 py-1 text-[11px] font-medium text-gray-800 shadow dark:bg-gray-950/95 dark:text-gray-100"
            :style="{ left: `${hover.x + 12}px`, top: `${hover.y + 12}px` }"
        >
            {{ hover.text }}
        </p>
        <div class="pointer-events-none absolute inset-0 overflow-hidden">
            <div
                v-for="zone in zoneRects"
                :key="zone.id"
                data-testid="atr-zone"
                class="absolute inset-x-0"
                :style="{ top: `${zone.top}px`, height: `${zone.height}px`, background: zone.color }"
            />
        </div>
        <svg class="pointer-events-none absolute inset-0 h-full w-full overflow-hidden">
            <polygon v-if="forecastCones.outer" data-testid="forecast-outer" :points="forecastCones.outer" fill="rgba(122,90,248,0.12)" />
            <polygon v-if="forecastCones.inner" data-testid="forecast-inner" :points="forecastCones.inner" fill="rgba(122,90,248,0.24)" />
        </svg>
        <div class="pointer-events-none absolute inset-0 overflow-hidden">
            <p
                v-for="label in forecastLabels"
                :key="label.id"
                data-testid="forecast-label"
                class="absolute rounded bg-white/80 px-1 text-[11px] font-medium text-violet-700 dark:bg-gray-950/80 dark:text-violet-300"
                :style="{ left: `${label.x + 8}px`, top: `${label.y}px` }"
                :title="label.title"
            >
                {{ label.text }}
            </p>
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
        <ul class="sr-only" data-testid="step-ladder-markers">
            <li v-for="mark in studyMarks" :key="`${mark.kind}-${mark.time}`" :data-kind="mark.kind" :data-time="mark.time">{{ mark.kind }} {{ mark.time }}</li>
        </ul>
        <ul class="sr-only" data-testid="trade-markers">
            <li v-for="mark in markers" :key="mark.id" :data-side="mark.side" :data-simulated="String(mark.simulated)">{{ mark.text }}</li>
        </ul>
    </div>
</template>
