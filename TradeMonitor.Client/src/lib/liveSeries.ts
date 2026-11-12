import { asRecord, pick, readMoney, readTime } from '@/api/parse'
import type { LiveCandle, LiveTick } from '@/api/live'
import { parseInterval } from '@/lib/chartInterval'
import type { CandleBar } from '@/lib/chartSeries'

export interface SeriesPoint {
    time: number
    value: number
}

export function bucketSeconds(granularity: string): number {
    return parseInterval(granularity)?.seconds ?? 300
}

export function toCandleBars(bars: Pick<LiveCandle, 'start' | 'open' | 'high' | 'low' | 'close' | 'volume'>[]): CandleBar[] {
    return bars
        .filter((bar) => bar.start > 0 && bar.close > 0)
        .map((bar) => ({
            time: bar.start,
            open: bar.open > 0 ? bar.open : bar.close,
            high: bar.high > 0 ? bar.high : bar.close,
            low: bar.low > 0 ? bar.low : bar.close,
            close: bar.close,
            volume: bar.volume ?? 0,
        }))
}

export function applyCandleTick(bars: CandleBar[], tick: { price: number; at?: string | null }, bucket: number, nowMs = Date.now()): CandleBar[] {
    if (!(tick.price > 0)) return bars
    const parsed = tick.at ? Date.parse(tick.at) : Number.NaN
    const ms = Number.isNaN(parsed) ? nowMs : parsed
    const size = Math.max(60, bucket)
    const time = Math.floor(ms / 1000 / size) * size
    const fresh = { time, open: tick.price, high: tick.price, low: tick.price, close: tick.price, volume: 0 }
    if (bars.length === 0) return [fresh]
    const last = bars[bars.length - 1]!
    if (time === last.time) {
        return [...bars.slice(0, -1), { ...last, close: tick.price, high: Math.max(last.high, tick.price), low: Math.min(last.low, tick.price) }]
    }
    if (time > last.time) return [...bars, fresh]
    return bars
}

export function barsToPoints(bars: Pick<LiveCandle, 'start' | 'close'>[]): SeriesPoint[] {
    return bars.filter((bar) => bar.start > 0 && bar.close > 0).map((bar) => ({ time: bar.start, value: bar.close }))
}

// A tick inside the current candle replaces the close. A later candle is appended.
export function applyTick(points: SeriesPoint[], tick: { price: number; at?: string | null }, bucket: number, nowMs = Date.now()): SeriesPoint[] {
    if (!(tick.price > 0)) return points
    const parsed = tick.at ? Date.parse(tick.at) : Number.NaN
    const ms = Number.isNaN(parsed) ? nowMs : parsed
    const size = Math.max(60, bucket)
    const time = Math.floor(ms / 1000 / size) * size
    if (points.length === 0) return [{ time, value: tick.price }]
    const last = points[points.length - 1]!
    if (time === last.time) return [...points.slice(0, -1), { time, value: tick.price }]
    if (time > last.time) return [...points, { time, value: tick.price }]
    return points
}

export function parseLiveTick(body: unknown): LiveTick | null {
    let record = asRecord(body)
    if (!record) return null
    if (pick(record, 'price') == null) {
        const nested = pick(record, 'json')
        if (typeof nested === 'string') {
            try {
                record = asRecord(JSON.parse(nested))
            } catch {
                return null
            }
        } else {
            record = asRecord(nested)
        }
    }
    if (!record) return null
    const price = readMoney(pick(record, 'price'))
    if (price == null || price <= 0) return null
    const product = pick(record, 'productId')
    return {
        productId: typeof product === 'string' ? product : '',
        price,
        at: readTime(pick(record, 'at')),
    }
}
