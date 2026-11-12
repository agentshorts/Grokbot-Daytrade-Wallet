import { zoneOffsetMs } from '@/lib/format'
import { parseInterval } from '@/lib/chartInterval'

export const quickRanges = ['1H', '4H', '1D', '1W', '1M', '3M', 'YTD', 'All'] as const

export type QuickRange = (typeof quickRanges)[number]
export type ChartRange = QuickRange | 'custom'

export interface RangeBounds {
    from: number | null
    to: number | null
}

export function rangeBounds(range: ChartRange, nowSec: number, timeZone: string): RangeBounds {
    const to = nowSec + 60
    if (range === '1H') return { from: nowSec - 3600, to }
    if (range === '4H') return { from: nowSec - 4 * 3600, to }
    if (range === '1D') return { from: nowSec - 86400, to }
    if (range === '1W') return { from: nowSec - 7 * 86400, to }
    if (range === '1M') return { from: nowSec - 30 * 86400, to }
    if (range === '3M') return { from: nowSec - 90 * 86400, to }
    if (range === 'YTD') return { from: startOfYear(nowSec, timeZone), to }
    return { from: null, to: null }
}

export function startOfYear(nowSec: number, timeZone: string): number {
    const list = new Intl.DateTimeFormat('en-US-u-hc-h23', { timeZone, year: 'numeric' }).formatToParts(new Date(nowSec * 1000))
    const year = Number(list.find((item) => item.type === 'year')?.value)
    const wall = Date.UTC(year, 0, 1)
    let utc = wall - zoneOffsetMs(new Date(wall), timeZone)
    utc = wall - zoneOffsetMs(new Date(utc), timeZone)
    return Math.floor(utc / 1000)
}

export function readChartQuery(query: Record<string, unknown>): { range: ChartRange | null; from: number | null; to: number | null; interval: string | null } {
    const rangeText = one(query.range)
    const range = quickRanges.includes(rangeText as QuickRange) || rangeText === 'custom' ? (rangeText as ChartRange) : null
    const from = numberOrNull(one(query.from))
    const to = numberOrNull(one(query.to))
    const intervalText = one(query.interval)
    const interval = parseInterval(intervalText)?.key ?? null
    return { range, from, to, interval }
}

export function chartQuery(input: { range: ChartRange; from: number | null; to: number | null; interval: string }): Record<string, string> {
    const query: Record<string, string> = { range: input.range, interval: input.interval }
    if (input.range === 'custom' && input.from != null) query.from = String(input.from)
    if (input.range === 'custom' && input.to != null) query.to = String(input.to)
    return query
}

export function wantsOlderHistory(range: { from: number; to: number } | null): boolean {
    return range != null && range.from < -2
}

function one(value: unknown): string {
    if (Array.isArray(value)) return typeof value[0] === 'string' ? value[0] : ''
    return typeof value === 'string' ? value : ''
}

function numberOrNull(value: string): number | null {
    if (!value) return null
    const parsed = Number(value)
    return Number.isFinite(parsed) ? parsed : null
}
