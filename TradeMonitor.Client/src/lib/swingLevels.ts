import { asRecord, pick, readMoney } from '@/api/parse'
import { formatMenuPrice } from '@/lib/chartMenu'

export interface ChartGuide {
    id: string
    price: number
    color: string
    title: string
    label: string
    width: number
    hit?: boolean
}

export interface SwingLevel {
    price: number
    side: 'support' | 'resistance'
    strength: number
    firstAt: number
    lastAt: number
}

export interface SwingSnapshot {
    notice: string
    levels: SwingLevel[]
    above: SwingLevel[]
    below: SwingLevel[]
}

export function parseSwing(body: unknown): SwingSnapshot {
    const record = asRecord(body) ?? {}
    const nearest = asRecord(pick(record, 'nearest')) ?? {}
    return {
        notice: typeof pick(record, 'notice') === 'string' ? String(pick(record, 'notice')) : '',
        levels: rows(pick(record, 'levels')),
        above: rows(pick(nearest, 'above')),
        below: rows(pick(nearest, 'below')),
    }
}

export function nearestSwing(levels: SwingLevel[], price: number, take = 2): { above: SwingLevel[]; below: SwingLevel[] } {
    return {
        above: levels.filter((level) => level.price > price).sort((a, b) => a.price - b.price).slice(0, take),
        below: levels.filter((level) => level.price < price).sort((a, b) => b.price - a.price).slice(0, take),
    }
}

export function swingLabel(level: SwingLevel, price: number, rank: number): string {
    const name = level.side === 'resistance' ? `R${rank}` : `S${rank}`
    const delta = level.price - price
    const pct = price === 0 ? 0 : (delta / price) * 100
    const sign = delta > 0 ? '+' : delta < 0 ? '-' : ''
    const money = formatMenuPrice(Math.abs(delta))
    const percent = Math.abs(pct).toLocaleString('en-US', { maximumFractionDigits: 2 })
    return `${name} ${formatMenuPrice(level.price)} ${sign}${money} ${sign}${percent}% (${level.strength}x)`
}

export function stackLabelYs(rows: { y: number }[], gap = 18): number[] {
    const order = rows.map((row, index) => ({ index, y: row.y })).sort((a, b) => a.y - b.y)
    const placed = new Array<number>(rows.length)
    let last = Number.NEGATIVE_INFINITY
    for (const row of order) {
        const y = Math.max(row.y, last + gap)
        placed[row.index] = y
        last = y
    }
    return placed
}

function rows(value: unknown): SwingLevel[] {
    if (!Array.isArray(value)) return []
    return value.flatMap((item) => {
        const record = asRecord(item)
        if (!record) return []
        const price = readMoney(pick(record, 'price'))
        if (price == null || price <= 0) return []
        const side = String(pick(record, 'side') ?? '').toLowerCase() === 'support' ? 'support' : 'resistance'
        return [
            {
                price,
                side,
                strength: Math.max(1, Math.round(readMoney(pick(record, 'strength')) ?? 1)),
                firstAt: readMoney(pick(record, 'firstAt')) ?? 0,
                lastAt: readMoney(pick(record, 'lastAt')) ?? 0,
            },
        ]
    })
}
