import { describe, expect, it } from 'vite-plus/test'
import { toTradeMarks, tradeMarkerText } from './tradeMarkers'

describe('trade markers', () => {
    it('describes qty, price, fee, and kind, and flags a simulation fill', () => {
        expect(tradeMarkerText({ side: 'buy', qty: 2, price: 10, fee: 0.25, kind: 'Fill', simulated: false })).toBe('Buy 2 @ $10, fee $0.25, fill')
        expect(tradeMarkerText({ side: 'sell', qty: 1, price: 12, fee: 0, kind: 'Fill', simulated: true })).toBe('Sell 1 @ $12, fee $0, fill, simulation')
        expect(
            tradeMarkerText({
                side: 'sell',
                qty: 10,
                price: 15,
                fee: 0,
                kind: 'Fill',
                simulated: false,
                realizedPnl: 120,
                at: '2026-06-01T15:04:00.000Z',
            }),
        ).toContain('Sell 10 @ $15, fee $0, fill, P/L +$120.00')
        expect(
            tradeMarkerText({
                side: 'sell',
                qty: 10,
                price: 15,
                fee: 0,
                kind: 'Fill',
                simulated: false,
                realizedPnl: 120,
                at: '2026-06-01T15:04:00.000Z',
            }),
        ).toContain('CT')
        const marks = toTradeMarks([
            {
                tradeId: 4,
                side: 'Buy',
                kind: 'Fill',
                qty: 1,
                price: 6,
                fee: 0,
                executedAt: '2026-06-15T12:01:00.000Z',
                simulated: true,
            },
        ])
        expect(marks[0]).toMatchObject({ id: 4, side: 'buy', simulated: true, price: 6 })
    })
})
