export interface CandleBar {
    time: number
    open: number
    high: number
    low: number
    close: number
    volume: number
}

export type ChartType = 'candle' | 'line' | 'area' | 'baseline' | 'delta' | 'bars' | 'heikin' | 'vertex-step' | 'vertex-baseline'
export type BaselineSource = 'first' | 'average' | 'typed'
export type DeltaMode = 'change' | 'percent'

export const chartTypes: { id: ChartType; label: string; short: string }[] = [
    { id: 'candle', label: 'Candlestick', short: 'Candle' },
    { id: 'line', label: 'Line', short: 'Line' },
    { id: 'area', label: 'Area', short: 'Area' },
    { id: 'baseline', label: 'Baseline', short: 'Base' },
    { id: 'delta', label: 'Delta', short: 'Delta' },
    { id: 'bars', label: 'OHLC bars', short: 'OHLC' },
    { id: 'heikin', label: 'Heikin Ashi', short: 'Heikin' },
    { id: 'vertex-step', label: 'Vertex step', short: 'Step' },
    { id: 'vertex-baseline', label: 'Vertex baseline delta', short: 'Vertex' },
]

const up = '#12b76a'
const down = '#f04438'

export function isChartType(value: string): value is ChartType {
    return chartTypes.some((item) => item.id === value)
}

export function heikinAshi(bars: CandleBar[]): CandleBar[] {
    const next: CandleBar[] = []
    for (const bar of bars) {
        const close = (bar.open + bar.high + bar.low + bar.close) / 4
        const prev = next[next.length - 1]
        const open = prev ? (prev.open + prev.close) / 2 : (bar.open + bar.close) / 2
        next.push({
            time: bar.time,
            open,
            close,
            high: Math.max(bar.high, open, close),
            low: Math.min(bar.low, open, close),
            volume: bar.volume,
        })
    }
    return next
}

export function deltaPoints(bars: CandleBar[], mode: DeltaMode): { time: number; value: number; color: string }[] {
    const first = bars[0]?.close ?? 0
    return bars.map((bar, index) => {
        const prev = index === 0 ? bar.open : bars[index - 1]!.close
        const value = mode === 'percent' ? (first === 0 ? 0 : (bar.close / first - 1) * 100) : bar.close - prev
        return { time: bar.time, value, color: value >= 0 ? up : down }
    })
}

export function baselineValue(bars: CandleBar[], source: BaselineSource, average: number | null, typed: number | null): number | null {
    if (source === 'average') return average != null && average > 0 ? average : bars[0]?.close || null
    if (source === 'typed') return typed != null && typed > 0 ? typed : bars[0]?.close || null
    return bars[0]?.close || null
}

export function mergeCandles(current: CandleBar[], incoming: CandleBar[]): CandleBar[] {
    const map = new Map<number, CandleBar>()
    for (const bar of current) map.set(bar.time, bar)
    for (const bar of incoming) map.set(bar.time, bar)
    return [...map.values()].sort((a, b) => a.time - b.time)
}

export function priceBars(type: ChartType, bars: CandleBar[]): CandleBar[] {
    return type === 'heikin' ? heikinAshi(bars) : bars
}

export function showsDeltaPane(type: ChartType): boolean {
    return type === 'delta' || type === 'vertex-baseline'
}

export function usesOhlc(type: ChartType): boolean {
    return type === 'candle' || type === 'bars' || type === 'heikin'
}
