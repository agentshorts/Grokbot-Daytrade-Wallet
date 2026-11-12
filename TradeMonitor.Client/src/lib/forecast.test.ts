import { describe, expect, it } from 'vite-plus/test'
import { forecastHorizonBars, forecastLabel, parseForecast } from './forecast'

describe('forecast range', () => {
    it('turns a horizon into bars and labels the cone', () => {
        expect(forecastHorizonBars('1h', 300)).toBe(12)
        expect(forecastHorizonBars('4h', 3600)).toBe(4)
        expect(forecastHorizonBars('1d', 300)).toBe(128)
        expect(forecastLabel('P90', 72900, 72000)).toBe('P90 $72,900 +1.3%')
        expect(forecastLabel('P10', 71400, 72000)).toBe('P10 $71,400 -0.8%')
        expect(forecastLabel('Median', 72150, 72000)).toBe('Median $72,150 +0.2%')
    })

    it('reads steps and a missing-model notice', () => {
        expect(parseForecast({ notice: 'forecast model not installed', model: 'TimesFM 2.5' })).toMatchObject({
            notice: 'forecast model not installed',
            steps: [],
        })
        const snapshot = parseForecast({
            model: 'TimesFM 2.5',
            coverage: '80% band hit 74% of the time, last 7 days',
            generatedAt: 100,
            steps: [{ at: 1300, mean: 55, q10: 50, q30: 52, q50: 55, q70: 58, q90: 60 }],
        })
        expect(snapshot.coverage).toContain('74%')
        expect(snapshot.steps[0]).toMatchObject({ time: 1300, mean: 55, q10: 50, q90: 60 })
    })
})