export interface PricePoint {
    time: number
    value: number
}

export type MarkerSide = 'buy' | 'sell' | 'high' | 'low'

export interface ChartMarker {
    time: number
    side: MarkerSide
    text: string
}

export function markerStyle(side: MarkerSide): {
    position: 'belowBar' | 'aboveBar'
    shape: 'arrowUp' | 'arrowDown' | 'circle'
    color: string
} {
    if (side === 'buy') return { position: 'belowBar', shape: 'arrowUp', color: '#12b76a' }
    if (side === 'sell') return { position: 'aboveBar', shape: 'arrowDown', color: '#f04438' }
    if (side === 'low') return { position: 'belowBar', shape: 'circle', color: '#12b76a' }
    return { position: 'aboveBar', shape: 'circle', color: '#f79009' }
}

export function toChartPoints(rows: { at: string; price: number }[]): PricePoint[] {
    const byTime = new Map<number, number>()
    for (const row of rows) {
        const time = Math.floor(new Date(row.at).getTime() / 1000)
        if (Number.isNaN(time)) continue
        byTime.set(time, row.price)
    }
    return [...byTime.entries()].sort((a, b) => a[0] - b[0]).map(([time, value]) => ({ time, value }))
}

export function toTradeMarkers(trades: { executedAt: string; side: 'buy' | 'sell'; qty: number }[]): ChartMarker[] {
    return trades
        .map((trade) => {
            const time = Math.floor(new Date(trade.executedAt).getTime() / 1000)
            const verb = trade.side === 'buy' ? 'Buy' : 'Sell'
            return { time, side: trade.side, text: `${verb} ${trade.qty}` }
        })
        .filter((marker) => !Number.isNaN(marker.time))
        .sort((a, b) => a.time - b.time)
}
