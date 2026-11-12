import { describe, expect, it } from 'vite-plus/test'
import { averageQuote, quoteLots } from '@/lib/lotQuote'

const lots = [
    { tradeId: 1, qty: 10, buyPrice: 3, cost: 30, executedAt: '2026-06-01T15:00:00Z' },
    { tradeId: 2, qty: 4, buyPrice: 4, cost: 16, executedAt: '2026-06-01T15:01:00Z' },
    { tradeId: 3, qty: 2, buyPrice: 12, cost: 24, executedAt: '2026-06-01T15:02:00Z' },
]

describe('lot quotes', () => {
    it('sorts the worked example by profit and quotes the average', () => {
        const quoted = quoteLots(lots, 15)
        expect(quoted.map((lot) => [lot.qty, lot.buyPrice, lot.unrealizedPnl, lot.unrealizedPnlPercent])).toEqual([
            [10, 3, 120, 400],
            [4, 4, 44, 275],
            [2, 12, 6, 25],
        ])
        const average = averageQuote(16, 70, 16, 15)
        expect(average.avgCost).toBe(4.375)
        expect(average.profit).toBe(170)
        const partial = averageQuote(6, 40, 3, 15)
        expect(partial.matchedCost).toBe(20)
        expect(partial.profit).toBe(25)
    })

    it('turns a profitable lot red when the price falls through the cost', () => {
        const quoted = quoteLots([{ tradeId: 1, qty: 2, buyPrice: 12, cost: 24 }], 9)
        expect(quoted[0]?.unrealizedPnl).toBe(-6)
    })
})
