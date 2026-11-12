import { describe, expect, it } from 'vite-plus/test'
import { applyCandleTick, applyTick, barsToPoints, bucketSeconds, parseLiveTick, toCandleBars } from './liveSeries'

describe('live series', () => {
    it('keeps the candle close and appends the next bucket', () => {
        expect(bucketSeconds('5m')).toBe(300)
        expect(bucketSeconds('1h')).toBe(3600)
        const start = 1_780_000_200
        const seeded = barsToPoints([{ start, close: 100 }])
        const same = applyTick(seeded, { price: 101, at: new Date(start * 1000 + 20_000).toISOString() }, 300)
        expect(same).toEqual([{ time: start, value: 101 }])
        const next = applyTick(same, { price: 102, at: new Date((start + 300) * 1000).toISOString() }, 300)
        expect(next).toEqual([
            { time: start, value: 101 },
            { time: start + 300, value: 102 },
        ])
        expect(applyTick(next, { price: 0, at: new Date((start + 600) * 1000).toISOString() }, 300)).toBe(next)
        expect(applyTick(next, { price: 9, at: new Date((start - 300) * 1000).toISOString() }, 300)).toBe(next)
        expect(bucketSeconds('7m')).toBe(420)
        expect(bucketSeconds('4h')).toBe(14400)
        expect(bucketSeconds('nope')).toBe(300)
    })

    it('updates the open bucket high and low for any interval', () => {
        const start = 1_680_000_000
        const seeded = toCandleBars([{ start, open: 100, high: 110, low: 90, close: 105, volume: 3 }])
        const same = applyCandleTick(seeded, { price: 120, at: new Date(start * 1000 + 30_000).toISOString() }, 420)
        expect(same).toEqual([{ time: start, open: 100, high: 120, low: 90, close: 120, volume: 3 }])
        const later = applyCandleTick(same, { price: 80, at: new Date((start + 420) * 1000).toISOString() }, 420)
        expect(later[1]).toEqual({ time: start + 420, open: 80, high: 80, low: 80, close: 80, volume: 0 })
    })

    it('reads a server event body', () => {
        expect(parseLiveTick({ productId: 'BTC-USD', price: '100.25', at: '2026-06-15T12:00:00Z' })).toEqual({
            productId: 'BTC-USD',
            price: 100.25,
            at: '2026-06-15T12:00:00.000Z',
        })
        expect(parseLiveTick({ json: '{"ProductId":"ETH-USD","Price":9,"At":"/Date(1781524800000)/"}' })?.productId).toBe('ETH-USD')
        expect(parseLiveTick({ price: 0 })).toBeNull()
    })
})
