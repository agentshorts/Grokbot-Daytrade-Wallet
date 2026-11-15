import { describe, expect, it } from 'vite-plus/test'
import { CHART_PREFS_KEY, defaultChartPrefs, readChartPrefs, writeChartPrefs, type PrefsStorage } from './chartPrefs'
import { keepDormantPeriod, keepStepIncrease, parseStepLadder, readStepLadderInputs, stepLadderDefaults } from './stepLadder'

function memory(): PrefsStorage & { raw: Map<string, string> } {
    const raw = new Map<string, string>()
    return {
        raw,
        getItem: (key) => raw.get(key) ?? null,
        setItem: (key, value) => raw.set(key, value),
    }
}

describe('step ladder inputs', () => {
    it('accepts the defaults and rejects multipliers that are not positive or a period below 1', () => {
        expect(readStepLadderInputs(2, 0.5, 5)).toEqual({ ok: true, value: stepLadderDefaults })
        expect(readStepLadderInputs('3.5', '0.25', '8')).toEqual({ ok: true, value: { stepIncrease: 3.5, dormantThreshold: 0.25, dormantPeriod: 8 } })
        expect(readStepLadderInputs(1, 1, 1).ok).toBe(true)
        expect(readStepLadderInputs(0, 0.5, 5)).toEqual({ ok: false, error: 'Step increase must be greater than 0.' })
        expect(readStepLadderInputs(-2, 0.5, 5).ok).toBe(false)
        expect(readStepLadderInputs(2, 0, 5)).toEqual({ ok: false, error: 'Dormant threshold must be greater than 0.' })
        expect(readStepLadderInputs(2, 0.5, 0)).toEqual({ ok: false, error: 'Dormant period must be a whole number of at least 1.' })
        expect(readStepLadderInputs(2, 0.5, 1.5).ok).toBe(false)
        expect(readStepLadderInputs('', 0.5, 5).ok).toBe(false)
    })

    it('remembers the toggle and inputs, and falls back when stored values are invalid', () => {
        const store = memory()
        expect(defaultChartPrefs().showStepLadder).toBe(false)
        expect(defaultChartPrefs().stepIncrease).toBe(2)
        expect(defaultChartPrefs().dormantThreshold).toBe(0.5)
        expect(defaultChartPrefs().dormantPeriod).toBe(5)

        writeChartPrefs(store, { ...defaultChartPrefs(), showStepLadder: true, stepIncrease: 3, dormantThreshold: 0.25, dormantPeriod: 8 })
        expect(readChartPrefs(store)).toMatchObject({ showStepLadder: true, stepIncrease: 3, dormantThreshold: 0.25, dormantPeriod: 8 })

        store.setItem(CHART_PREFS_KEY, JSON.stringify({ showStepLadder: true, stepIncrease: 0, dormantThreshold: -1, dormantPeriod: 1.2 }))
        expect(readChartPrefs(store)).toMatchObject({ showStepLadder: true, stepIncrease: 2, dormantThreshold: 0.5, dormantPeriod: 5 })
        expect(keepStepIncrease(4)).toBe(4)
        expect(keepDormantPeriod(0)).toBe(5)
    })

    it('reads per-bar volume, vol24, step, and dormant flags', () => {
        expect(
            parseStepLadder({
                bars: [
                    { time: 60, volume: 5, vol24: null, step: false, dormant: false },
                    { Time: 120, Volume: 30, Vol24: 10, Step: true, Dormant: false },
                    { time: 0, volume: 1, vol24: 1, step: true, dormant: true },
                ],
            }),
        ).toEqual({
            bars: [
                { time: 60, volume: 5, vol24: null, step: false, dormant: false },
                { time: 120, volume: 30, vol24: 10, step: true, dormant: false },
            ],
        })
        expect(parseStepLadder({})).toEqual({ bars: [] })
    })
})
