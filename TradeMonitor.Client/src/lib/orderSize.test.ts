import { describe, expect, it } from 'vite-plus/test'
import { floorTo, fromSpend, sellAmount } from './orderSize'

const btc = { baseIncrement: 0.00000001, quoteIncrement: 0.01, baseMinSize: 0.00000001, minMarketFunds: 1 }

describe('order size', () => {
    it('turns a spend into a quantity on the product step and caps it at cash', () => {
        const quarter = fromSpend(250, 50000, 1000, btc)
        expect(quarter.error).toBeNull()
        expect(quarter.spend).toBe(250)
        expect(quarter.qty).toBe(0.005)

        const over = fromSpend(2500, 50000, 1000, btc)
        expect(over.spend).toBe(1000)
        expect(over.qty).toBe(0.02)

        expect(floorTo(0.005000009, 0.00000001)).toBe(0.005)
        expect(fromSpend(0.5, 50000, 1000, btc).error).toContain('minimum')
    })

    it('sells half of a fractional lot and leaves the rest', () => {
        const half = sellAmount(0.0025, 'qty', 50000, 0.005, btc)
        expect(half.qty).toBe(0.0025)
        expect(half.error).toBeNull()
        expect(0.005 - half.qty).toBe(0.0025)
    })
})