import { describe, expect, it } from 'vite-plus/test'
import { captureChartView, chartViewKey, logicalRangeToApply, timeRangeToApply, viewToRestore } from './chartView'

describe('chartViewKey', () => {
    it('uses symbol, interval, and window so a chart type switch keeps the key', () => {
        const window = { symbol: 'BTC-USD', interval: '5m', range: '1D', from: null, to: null }
        expect(chartViewKey(window)).toBe('BTC-USD:5m:1D::')
        expect(chartViewKey({ ...window, symbol: 'ETH-USD' })).not.toBe(chartViewKey(window))
        expect(chartViewKey({ ...window, interval: '1h' })).not.toBe(chartViewKey(window))
        expect(chartViewKey({ ...window, range: 'custom', from: 10, to: 20 })).toBe('BTC-USD:5m:custom:10:20')
    })
})

describe('viewToRestore', () => {
    const zoomed = captureChartView({ from: 4, to: 12 }, { from: 1_700_000_000, to: 1_700_003_000 })

    it('puts the same logical range back when the graph type changes', () => {
        expect(viewToRestore(zoomed, false)).toEqual(zoomed)
        expect(logicalRangeToApply(zoomed)).toEqual({ from: 4, to: 12 })
        expect(timeRangeToApply(zoomed)).toBeNull()
    })

    it('drops the saved window when the symbol or timeframe changes', () => {
        expect(viewToRestore(zoomed, true)).toBeNull()
        expect(viewToRestore(null, false)).toBeNull()
    })

    it('keeps a scrolled logical range and falls back to time when indexes are missing', () => {
        expect(captureChartView({ from: -4, to: 10 }, null)?.logical).toEqual({ from: -4, to: 10 })
        expect(captureChartView({ from: 5, to: 5 }, null)).toBeNull()
        const timed = captureChartView({ from: 5, to: 5 }, { from: 10, to: 20 })
        expect(timed).toEqual({ logical: null, time: { from: 10, to: 20 } })
        expect(logicalRangeToApply(timed)).toBeNull()
        expect(timeRangeToApply(timed)).toEqual({ from: 10, to: 20 })
        expect(captureChartView(null, { from: Number.NaN, to: 2 })).toBeNull()
    })
})
