import { describe, expect, it } from 'vite-plus/test'
import { tradeChartModel } from './tradeChart'

const buyAt = '2026-11-02T15:04:00.000Z'
const markAt = '2026-11-02T15:10:00.000Z'
const sellAt = '2026-11-02T16:00:00.000Z'

describe('trade chart markers', () => {
    it('lines each marker up with the recorded fill time and price', () => {
        const buyTime = Math.floor(new Date(buyAt).getTime() / 1000)
        const markTime = Math.floor(new Date(markAt).getTime() / 1000)
        const sellTime = Math.floor(new Date(sellAt).getTime() / 1000)
        const model = tradeChartModel({
            hasPriceMarks: true,
            priceMarks: [
                { id: 1, clientId: 'mark-0', symbol: 'BTC-USD', price: 63900, at: buyAt, source: 'agent', wallet: 'paper', recordedAt: buyAt },
                { id: 2, clientId: 'mark-1', symbol: 'BTC-USD', price: 64100, at: markAt, source: 'agent', wallet: 'paper', recordedAt: markAt },
            ],
            markers: [
                { tradeId: 3, side: 'buy', at: buyAt, price: 64000, qty: 0.01 },
                { tradeId: 4, side: 'sell', at: sellAt, price: 65000, qty: 0.004 },
            ],
        })

        expect(model.empty).toBe(false)
        expect(model.points).toEqual([
            { time: buyTime, value: 64000 },
            { time: markTime, value: 64100 },
            { time: sellTime, value: 65000 },
        ])
        expect(model.markers.map((marker) => marker.time)).toEqual([buyTime, sellTime])
        for (const marker of model.markers) {
            expect(model.points.find((point) => point.time === marker.time)).toBeTruthy()
        }
        expect(model.points.find((point) => point.time === buyTime)?.value).toBe(64000)
        expect(model.points.find((point) => point.time === sellTime)?.value).toBe(65000)
        expect(model.markers[0]?.text).toBe('Buy 0.01')
        expect(model.markers[1]?.text).toBe('Sell 0.004')
    })

    it('stays empty when the trade has no price marks', () => {
        const model = tradeChartModel({
            hasPriceMarks: false,
            priceMarks: [],
            markers: [{ tradeId: 5, side: 'buy', at: buyAt, price: 5, qty: 2 }],
        })
        expect(model).toEqual({ points: [], markers: [], empty: true })
    })
})
