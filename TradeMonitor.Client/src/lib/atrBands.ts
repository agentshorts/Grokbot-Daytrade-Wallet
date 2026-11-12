import { asRecord, pick, readMoney } from '@/api/parse'
import { formatMenuPrice, type MenuAction } from '@/lib/chartMenu'

export const atrHorizons = [
    { id: '1bar', label: '1 bar' },
    { id: '1h', label: '1 hour' },
    { id: '4h', label: '4 hours' },
    { id: '1d', label: '1 day' },
] as const

export type AtrHorizon = (typeof atrHorizons)[number]['id']

export interface AtrBand {
    mult: number
    upper: number
    lower: number
}

export interface AtrSnapshot {
    atr: number | null
    price: number
    period: number
    notice: string
}

export interface PriceZone {
    id: string
    top: number
    bottom: number
    color: string
}

export function isAtrHorizon(value: string): value is AtrHorizon {
    return atrHorizons.some((item) => item.id === value)
}

export function horizonBars(horizon: AtrHorizon, intervalSeconds: number): number {
    if (horizon === '1bar') return 1
    const seconds = horizon === '1h' ? 3600 : horizon === '4h' ? 14400 : 86400
    const step = intervalSeconds > 0 ? intervalSeconds : 300
    return Math.max(1, Math.round(seconds / step))
}

export function atrScale(bars: number): number {
    return Math.sqrt(Math.max(1, bars))
}

export function scaleBands(price: number, atr: number, bars: number, multipliers = [1, 2]): AtrBand[] {
    const move = atr * atrScale(bars)
    return multipliers.map((mult) => ({
        mult,
        upper: price + mult * move,
        lower: price - mult * move,
    }))
}

export function atrLabel(mult: number, level: number, price: number, direction: 'up' | 'down'): string {
    const name = `${direction === 'up' ? '+' : '-'}${mult} ATR`
    const delta = level - price
    const pct = price === 0 ? 0 : (delta / price) * 100
    const sign = delta > 0 ? '+' : delta < 0 ? '-' : ''
    const percent = Math.abs(pct).toLocaleString('en-US', { maximumFractionDigits: 2 })
    return `${name} ${formatMenuPrice(level)} ${sign}${formatMenuPrice(Math.abs(delta))} ${sign}${percent}%`
}

export function atrReadout(period: number, interval: string, horizon: AtrHorizon, atr: number, bars: number): string {
    const label = atrHorizons.find((item) => item.id === horizon)?.label ?? horizon
    return `ATR ${period} (${interval}, ${label}): ${formatMenuPrice(atr * atrScale(bars))}`
}

export function costAtrText(cost: number, price: number, atr: number): string | null {
    if (!(atr > 0) || !(price > 0)) return null
    const dist = (cost - price) / atr
    const rounded = Math.round(dist * 10) / 10
    const text = rounded.toLocaleString('en-US', { maximumFractionDigits: 1, minimumFractionDigits: 1 })
    return `cost is ${text} ATR from price`
}

export function bandTarget(cursor: number, anchor: number, bands: AtrBand[]): { id: 'atr-take' | 'atr-stop'; price: number; mult: number } | null {
    if (!(cursor > 0) || !(anchor > 0) || cursor === anchor) return null
    if (cursor > anchor) {
        const hit = bands.filter((band) => cursor <= band.upper).sort((a, b) => a.mult - b.mult)[0]
        return hit ? { id: 'atr-take', price: hit.upper, mult: hit.mult } : null
    }
    const hit = bands.filter((band) => cursor >= band.lower).sort((a, b) => a.mult - b.mult)[0]
    return hit ? { id: 'atr-stop', price: hit.lower, mult: hit.mult } : null
}

export function bandMenu(cursor: number, anchor: number, bands: AtrBand[]): MenuAction[] {
    const target = bandTarget(cursor, anchor, bands)
    if (!target) return []
    if (target.id === 'atr-take') return [{ id: target.id, label: `Take-profit at +${target.mult} ATR`, tone: 'buy' }]
    return [{ id: target.id, label: `Stop at -${target.mult} ATR`, tone: 'sell' }]
}

export function atrZones(price: number, bands: AtrBand[]): PriceZone[] {
    const inner = bands[0]
    const outer = bands[bands.length - 1]
    if (!inner || !outer) return []
    return [
        { id: 'atr-up-outer', top: outer.upper, bottom: price, color: 'rgba(18,183,106,0.08)' },
        { id: 'atr-up-inner', top: inner.upper, bottom: price, color: 'rgba(18,183,106,0.16)' },
        { id: 'atr-down-outer', top: price, bottom: outer.lower, color: 'rgba(240,68,56,0.08)' },
        { id: 'atr-down-inner', top: price, bottom: inner.lower, color: 'rgba(240,68,56,0.16)' },
    ]
}

export function parseAtr(body: unknown): AtrSnapshot {
    const record = asRecord(body) ?? {}
    const atr = readMoney(pick(record, 'atr'))
    const period = readMoney(pick(record, 'period'))
    return {
        atr: atr != null && atr > 0 ? atr : null,
        price: readMoney(pick(record, 'price')) ?? 0,
        period: period != null && period > 0 ? period : 14,
        notice: typeof pick(record, 'notice') === 'string' ? String(pick(record, 'notice')) : '',
    }
}
