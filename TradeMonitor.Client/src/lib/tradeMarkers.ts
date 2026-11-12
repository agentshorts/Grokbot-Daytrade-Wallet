import type { LiveTradeMark } from '@/api/live'
import { formatMenuPrice } from '@/lib/chartMenu'
import { formatQty } from '@/lib/format'

export interface ChartTradeMark {
    id: number
    time: number
    price: number
    side: 'buy' | 'sell'
    simulated: boolean
    text: string
}

export function tradeMarkerText(mark: { side: 'buy' | 'sell'; qty: number; price: number; fee: number; kind: string; simulated: boolean }): string {
    const verb = mark.side === 'buy' ? 'Buy' : 'Sell'
    const kind = mark.kind.trim().toLowerCase() || 'fill'
    const base = `${verb} ${formatQty(mark.qty)} @ ${formatMenuPrice(mark.price)}, fee ${formatMenuPrice(mark.fee)}, ${kind}`
    return mark.simulated ? `${base}, simulation` : base
}

export function toTradeMarks(rows: LiveTradeMark[]): ChartTradeMark[] {
    return rows.flatMap((row) => {
        if (!row.executedAt || row.price <= 0) return []
        const time = Math.floor(new Date(row.executedAt).getTime() / 1000)
        if (Number.isNaN(time)) return []
        const side = row.side === 'Sell' ? 'sell' : 'buy'
        return [
            {
                id: row.tradeId,
                time,
                price: row.price,
                side,
                simulated: row.simulated,
                text: tradeMarkerText({ side, qty: row.qty, price: row.price, fee: row.fee, kind: row.kind, simulated: row.simulated }),
            },
        ]
    })
}
