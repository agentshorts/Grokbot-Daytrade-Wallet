import { describe, expect, it } from 'vite-plus/test'
import { markerStyle, toChartPoints, toTradeMarkers } from './priceSeries'

describe('price series markers', () => {
    it('places buy and sell markers on the fill time', () => {
        const at = '2026-07-15T17:00:00.000Z'
        const time = Math.floor(new Date(at).getTime() / 1000)
        const points = toChartPoints([{ at, price: 101 }])
        const markers = toTradeMarkers([
            { executedAt: at, side: 'buy', qty: 0.15 },
            { executedAt: '2026-07-16T17:00:00.000Z', side: 'sell', qty: 0.08 },
        ])
        expect(points[0]).toEqual({ time, value: 101 })
        expect(markers[0]?.time).toBe(points[0]?.time)
        expect(markerStyle(markers[0]!.side)).toEqual({ position: 'belowBar', shape: 'arrowUp', color: '#12b76a' })
        expect(markerStyle(markers[1]!.side)).toEqual({ position: 'aboveBar', shape: 'arrowDown', color: '#f04438' })
        expect(markers[0]?.text).toBe('Buy 0.15')
        expect(markers[1]?.text).toBe('Sell 0.08')
    })
})
