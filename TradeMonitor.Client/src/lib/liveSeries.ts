import { asRecord, pick, readMoney, readTime } from '@/api/parse'
import type { LiveCandle, LiveTick } from '@/api/live'

export interface SeriesPoint {
    time: number
    value: number
}

export function bucketSeconds(granularity: string): number {
    const key = granularity.trim().toLowerCase()
    if (key === '1m') return 60
    if (key === '15m') return 900
    if (key === '1h') return 3600
    if (key === '1d') return 86400
    return 300
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
