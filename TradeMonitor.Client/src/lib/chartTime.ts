import { zoneOffsetMs } from '@/lib/format'

export type ChartZoneMode = 'local' | 'utc'

export function chartZone(mode: ChartZoneMode, override?: string): string {
    if (mode === 'utc') return 'UTC'
    if (override) return override
    return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC'
}

function parts(unixSeconds: number, timeZone: string, options: Intl.DateTimeFormatOptions): Intl.DateTimeFormatPart[] {
    return new Intl.DateTimeFormat('en-US', { timeZone, hour12: true, ...options }).formatToParts(new Date(unixSeconds * 1000))
}

function part(list: Intl.DateTimeFormatPart[], type: Intl.DateTimeFormatPartTypes): string {
    return list.find((item) => item.type === type)?.value ?? ''
}

export function formatChartTick(unixSeconds: number, timeZone: string, intervalSeconds = 300): string {
    if (!Number.isFinite(unixSeconds)) return ''
    if (intervalSeconds >= 86400) {
        const list = parts(unixSeconds, timeZone, { month: 'short', day: 'numeric' })
        return `${part(list, 'month')} ${part(list, 'day')}`
    }
    const list = parts(unixSeconds, timeZone, { hour: 'numeric', minute: '2-digit' })
    return `${part(list, 'hour')}:${part(list, 'minute')} ${part(list, 'dayPeriod')}`
}

export function formatChartCrosshair(unixSeconds: number, timeZone: string): string {
    if (!Number.isFinite(unixSeconds)) return ''
    const list = parts(unixSeconds, timeZone, { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' })
    return `${part(list, 'month')} ${part(list, 'day')}, ${part(list, 'hour')}:${part(list, 'minute')} ${part(list, 'dayPeriod')}`
}

export function zoneAbbrev(unixSeconds: number, timeZone: string): string {
    const list = new Intl.DateTimeFormat('en-US', { timeZone, timeZoneName: 'short', hour: 'numeric' }).formatToParts(new Date(unixSeconds * 1000))
    return part(list, 'timeZoneName') || timeZone
}

export function wallInput(unixSeconds: number, timeZone: string): string {
    const list = new Intl.DateTimeFormat('en-US-u-hc-h23', {
        timeZone,
        year: 'numeric',
        month: '2-digit',
        day: '2-digit',
        hour: '2-digit',
        minute: '2-digit',
    }).formatToParts(new Date(unixSeconds * 1000))
    const hour = part(list, 'hour') === '24' ? '00' : part(list, 'hour')
    return `${part(list, 'year')}-${part(list, 'month')}-${part(list, 'day')}T${hour}:${part(list, 'minute')}`
}

export function unixFromWall(value: string, timeZone: string): number | null {
    const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})$/.exec(value)
    if (!match) return null
    const wall = Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3]), Number(match[4]), Number(match[5]))
    let utc = wall - zoneOffsetMs(new Date(wall), timeZone)
    utc = wall - zoneOffsetMs(new Date(utc), timeZone)
    return Math.floor(utc / 1000)
}
