import { describe, expect, it } from 'vite-plus/test'
import { atrLabel, atrReadout, atrZones, bandMenu, bandTarget, costAtrText, horizonBars, parseAtr, scaleBands } from './atrBands'

const bands = scaleBands(100, 10, 4)

describe('atr bands', () => {
    it('turns a horizon into bars and scales the band by the square root', () => {
        expect(horizonBars('1bar', 300)).toBe(1)
        expect(horizonBars('1h', 300)).toBe(12)
        expect(horizonBars('4h', 3600)).toBe(4)
        expect(horizonBars('1d', 300)).toBe(288)
        expect(bands.map((band) => [band.mult, band.upper, band.lower])).toEqual([
            [1, 120, 80],
            [2, 140, 60],
        ])
        expect(atrReadout(14, '5m', '1h', 10, 4)).toBe('ATR 14 (5m, 1 hour): $20')
    })

    it('labels the edges and the cost distance', () => {
        expect(atrLabel(1, 110, 100, 'up')).toBe('+1 ATR $110 +$10 +10%')
        expect(atrLabel(2, 80, 100, 'down')).toBe('-2 ATR $80 -$20 -20%')
        expect(costAtrText(86, 100, 10)).toBe('cost is -1.4 ATR from price')
        expect(atrZones(100, bands).map((zone) => zone.id)).toEqual(['atr-up-outer', 'atr-up-inner', 'atr-down-outer', 'atr-down-inner'])
    })

    it('offers take-profit or a stop for the band under the cursor', () => {
        expect(bandMenu(105, 100, bands).map((action) => action.label)).toEqual(['Take-profit at +1 ATR'])
        expect(bandMenu(125, 100, bands).map((action) => action.label)).toEqual(['Take-profit at +2 ATR'])
        expect(bandMenu(95, 100, bands).map((action) => action.label)).toEqual(['Stop at -1 ATR'])
        expect(bandMenu(70, 100, bands).map((action) => action.label)).toEqual(['Stop at -2 ATR'])
        expect(bandMenu(150, 100, bands)).toEqual([])
        expect(bandTarget(105, 100, bands)?.price).toBe(120)
        expect(bandTarget(70, 100, bands)?.price).toBe(60)
    })

    it('reads a short-history notice and a numeric atr', () => {
        expect(parseAtr({ notice: 'Not enough bars for ATR.', atr: null }).atr).toBeNull()
        expect(parseAtr({ notice: 'Not enough bars for ATR.' }).notice).toContain('Not enough')
        expect(parseAtr({ atr: '12.5', period: 14, price: 100 })).toMatchObject({ atr: 12.5, period: 14, price: 100 })
    })
})
