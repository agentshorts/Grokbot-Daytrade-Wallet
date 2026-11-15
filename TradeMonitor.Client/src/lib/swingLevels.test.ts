import { describe, expect, it } from 'vite-plus/test'
import { nearestSwing, parseSwing, stackLabelYs, swingLabel } from './swingLevels'

describe('swing labels', () => {
    it('parses levels and labels the nearest ones from the live price', () => {
        const snapshot = parseSwing({
            notice: '',
            levels: [
                { price: 90, side: 'support', strength: 2, firstAt: 1, lastAt: 2 },
                { price: 110, side: 'resistance', strength: 3, firstAt: 3, lastAt: 4 },
                { price: 130, side: 'resistance', strength: 1, firstAt: 5, lastAt: 5 },
            ],
            nearest: { above: [{ price: 110, side: 'resistance', strength: 3 }], below: [{ price: 90, side: 'support', strength: 2 }] },
        })
        expect(snapshot.levels).toHaveLength(3)
        const nearest = nearestSwing(snapshot.levels, 100)
        expect(nearest.above.map((level) => level.price)).toEqual([110, 130])
        expect(nearest.below.map((level) => level.price)).toEqual([90])
        expect(swingLabel(nearest.above[0]!, 100, 1)).toBe('R1 $110 +$10 +10% (3x)')
        expect(swingLabel(nearest.below[0]!, 100, 1)).toBe('S1 $90 -$10 -10% (2x)')
        expect(parseSwing({ levels: [] }).notice).toBe('')
        expect(parseSwing({ notice: 'Not enough bars for swing levels.' }).notice).toContain('Not enough')
    })

    it('stacks labels that would overlap', () => {
        expect(stackLabelYs([{ y: 10 }, { y: 12 }, { y: 40 }])).toEqual([10, 28, 46])
    })
})
