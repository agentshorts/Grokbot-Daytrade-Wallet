export type ChartHit =
    | { kind: 'price'; price: number }
    | { kind: 'lot'; lotId: number; price: number }
    | { kind: 'order'; orderId: number; price: number }
    | { kind: 'time' }

export interface MenuAction {
    id: string
    label: string
    tone: 'buy' | 'sell' | 'neutral'
}

export interface ChartLine {
    kind: 'lot' | 'order'
    id: number
    y: number
}

/** $15, $15.25, and $4.375. The quantity is not part of the label. */
export function formatMenuPrice(price: number): string {
    const text = price.toLocaleString('en-US', { maximumFractionDigits: 8 })
    return `$${text}`
}

export function chartMenu(hit: ChartHit, ctx?: { pendingMoveId?: number }): MenuAction[] {
    if (hit.kind === 'price') {
        const price = formatMenuPrice(hit.price)
        const actions: MenuAction[] = []
        if (ctx?.pendingMoveId != null) {
            actions.push({ id: 'move', label: `Move order to ${price}`, tone: 'neutral' })
        }
        actions.push(
            { id: 'market-buy', label: 'Market buy', tone: 'buy' },
            { id: 'limit-buy', label: `Limit buy at ${price}`, tone: 'buy' },
            { id: 'stop-buy', label: `Stop buy at ${price}`, tone: 'buy' },
            { id: 'market-sell', label: 'Market sell', tone: 'sell' },
            { id: 'limit-sell', label: `Limit sell at ${price}`, tone: 'sell' },
            { id: 'stop', label: `Stop-loss at ${price}`, tone: 'sell' },
            { id: 'take', label: `Take-profit at ${price}`, tone: 'buy' },
        )
        return actions
    }
    if (hit.kind === 'lot') {
        return [{ id: 'quick-sell', label: 'Quick sell this lot', tone: 'sell' }]
    }
    if (hit.kind === 'order') {
        return [
            { id: 'cancel', label: 'Cancel order', tone: 'sell' },
            { id: 'move-here', label: 'Move order here', tone: 'neutral' },
        ]
    }
    return [
        { id: '1m', label: '1 minute', tone: 'neutral' },
        { id: '5m', label: '5 minutes', tone: 'neutral' },
        { id: '15m', label: '15 minutes', tone: 'neutral' },
        { id: '1h', label: '1 hour', tone: 'neutral' },
        { id: '1d', label: '1 day', tone: 'neutral' },
        { id: 'zoom-in', label: 'Zoom in', tone: 'neutral' },
        { id: 'zoom-out', label: 'Zoom out', tone: 'neutral' },
        { id: 'fit', label: 'Fit chart', tone: 'neutral' },
    ]
}

/** A line within 8px wins. The time axis is the bottom band. Otherwise the cursor is a price. */
export function hitTest(input: {
    y: number
    height: number
    timeAxisHeight: number
    price: number | null
    lines: ChartLine[]
}): ChartHit | null {
    if (input.y >= input.height - input.timeAxisHeight) return { kind: 'time' }
    let best: ChartLine | null = null
    let bestDist = 9
    for (const line of input.lines) {
        const dist = Math.abs(input.y - line.y)
        if (dist < bestDist) {
            best = line
            bestDist = dist
        }
    }
    if (best) {
        const price = input.price ?? 0
        return best.kind === 'lot' ? { kind: 'lot', lotId: best.id, price } : { kind: 'order', orderId: best.id, price }
    }
    if (input.price == null) return null
    return { kind: 'price', price: input.price }
}
