const CHICAGO = 'America/Chicago'

export function roundTo(value: number, digits = 2): number {
    const factor = 10 ** digits
    return Math.round((value + Number.EPSILON) * factor) / factor
}

function zonedParts(date: Date, timeZone: string): Record<string, string> {
    const fmt = new Intl.DateTimeFormat('en-US-u-hc-h23', {
        timeZone,
        year: 'numeric',
        month: '2-digit',
        day: '2-digit',
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit',
    })
    const map: Record<string, string> = {}
    for (const part of fmt.formatToParts(date)) {
        if (part.type !== 'literal') map[part.type] = part.value
    }
    if (map.hour === '24') map.hour = '00'
    return map
}

/** Offset of `timeZone` at `date`, in milliseconds (local clock minus UTC). */
export function zoneOffsetMs(date: Date, timeZone = CHICAGO): number {
    const parts = zonedParts(date, timeZone)
    const asUtc = Date.UTC(Number(parts.year), Number(parts.month) - 1, Number(parts.day), Number(parts.hour), Number(parts.minute), Number(parts.second))
    return asUtc - date.getTime()
}

/** Midnight at the start of the Chicago calendar day that contains `now`. */
export function startOfChicagoDay(now: Date): Date {
    const parts = zonedParts(now, CHICAGO)
    const utcMidnight = Date.UTC(Number(parts.year), Number(parts.month) - 1, Number(parts.day))
    let offset = zoneOffsetMs(new Date(utcMidnight))
    let start = utcMidnight - offset
    offset = zoneOffsetMs(new Date(start))
    start = utcMidnight - offset
    return new Date(start)
}

/** A clock time on a Chicago day relative to the Chicago day of `now`. */
export function chicagoDateTime(now: Date, dayOffset: number, hour: number, minute: number): Date {
    const start = startOfChicagoDay(now)
    return new Date(start.getTime() + dayOffset * 86_400_000 + hour * 3_600_000 + minute * 60_000)
}

/** Shown when an amount is unknown. A missing price mark is not zero. */
export const UNKNOWN_AMOUNT = '—'

export function formatMoney(value: number | null | undefined, options?: { signed?: boolean; currency?: string }): string {
    if (value == null || Number.isNaN(value)) return ''
    const currency = options?.currency ?? 'USD'
    const signed = options?.signed ?? true
    const abs = new Intl.NumberFormat('en-US', { style: 'currency', currency }).format(Math.abs(value))
    if (value < 0) return `-${abs}`
    if (signed && value > 0) return `+${abs}`
    return abs
}

export function formatPct(value: number | null | undefined, options?: { signed?: boolean; digits?: number }): string {
    if (value == null || Number.isNaN(value)) return ''
    const digits = options?.digits ?? 2
    const signed = options?.signed ?? true
    const abs = Math.abs(value).toLocaleString('en-US', {
        minimumFractionDigits: digits,
        maximumFractionDigits: digits,
    })
    if (value < 0) return `-${abs}%`
    if (signed && value > 0) return `+${abs}%`
    return `${abs}%`
}

export function formatQty(value: number | null | undefined): string {
    if (value == null || Number.isNaN(value)) return ''
    return new Intl.NumberFormat('en-US', { maximumFractionDigits: 8 }).format(value)
}

export function formatPrice(value: number | null | undefined, currency = 'USD'): string {
    return formatMoney(value, { signed: false, currency })
}

/**
 * America/Chicago wall time with a CT label.
 * CST and CDT both render as CT, and the hour still follows the daylight-saving offset.
 */
export function formatTimeCt(value: string | number | Date | null | undefined): string {
    if (value == null || value === '') return ''
    const date = value instanceof Date ? value : new Date(value)
    if (Number.isNaN(date.getTime())) return ''
    const parts = new Intl.DateTimeFormat('en-US', {
        timeZone: CHICAGO,
        month: 'short',
        day: 'numeric',
        year: 'numeric',
        hour: 'numeric',
        minute: '2-digit',
        hour12: true,
    }).formatToParts(date)
    const get = (type: Intl.DateTimeFormatPartTypes) => parts.find((part) => part.type === type)?.value ?? ''
    return `${get('month')} ${get('day')}, ${get('year')}, ${get('hour')}:${get('minute')} ${get('dayPeriod')} CT`
}

export function formatChartLabel(value: string, grain: 'day' | 'hour'): string {
    const date = new Date(value)
    if (Number.isNaN(date.getTime())) return ''
    if (grain === 'hour') {
        const parts = new Intl.DateTimeFormat('en-US', {
            timeZone: CHICAGO,
            hour: 'numeric',
            minute: '2-digit',
            hour12: true,
        }).formatToParts(date)
        const get = (type: Intl.DateTimeFormatPartTypes) => parts.find((part) => part.type === type)?.value ?? ''
        return `${get('hour')}:${get('minute')} ${get('dayPeriod')}`
    }
    const parts = new Intl.DateTimeFormat('en-US', {
        timeZone: CHICAGO,
        month: 'short',
        day: 'numeric',
    }).formatToParts(date)
    const get = (type: Intl.DateTimeFormatPartTypes) => parts.find((part) => part.type === type)?.value ?? ''
    return `${get('month')} ${get('day')}`
}

export function isoDateTime(value: string | number | Date | null | undefined): string {
    if (value == null || value === '') return ''
    const date = value instanceof Date ? value : new Date(value)
    if (Number.isNaN(date.getTime())) return ''
    return date.toISOString()
}
