export interface ParsedInterval {
    seconds: number
    key: string
}

export const intervalPresets = ['1m', '2m', '5m', '15m', '30m', '1h', '2h', '3h', '5h', '1d', '1w'] as const

const maxSeconds = 28 * 24 * 3600

export function formatInterval(seconds: number): string {
    if (seconds % 604800 === 0) return `${seconds / 604800}w`
    if (seconds % 86400 === 0) return `${seconds / 86400}d`
    if (seconds % 3600 === 0) return `${seconds / 3600}h`
    return `${seconds / 60}m`
}

export function parseInterval(text: string): ParsedInterval | null {
    const key = text.trim().toLowerCase()
    if (!key) return null
    const native: Record<string, number> = { '60': 60, '300': 300, '900': 900, '3600': 3600, '21600': 21600, '86400': 86400 }
    if (native[key]) return { seconds: native[key], key: formatInterval(native[key]) }
    const match = /^(\d+)([mhdw])$/.exec(key)
    if (!match) return null
    const count = Number(match[1])
    if (!Number.isInteger(count) || count <= 0) return null
    const unit = match[2] === 'm' ? 60 : match[2] === 'h' ? 3600 : match[2] === 'd' ? 86400 : 604800
    const seconds = count * unit
    if (seconds < 60 || seconds > maxSeconds) return null
    return { seconds, key: formatInterval(seconds) }
}

export function intervalLabel(key: string): string {
    const parsed = parseInterval(key)
    if (!parsed) return key
    const unit = parsed.key.slice(-1)
    const count = parsed.key.slice(0, -1)
    const name = unit === 'm' ? 'minute' : unit === 'h' ? 'hour' : unit === 'd' ? 'day' : 'week'
    return count === '1' ? `1 ${name}` : `${count} ${name}s`
}
