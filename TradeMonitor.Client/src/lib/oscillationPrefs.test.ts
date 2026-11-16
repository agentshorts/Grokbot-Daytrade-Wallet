import { describe, expect, it } from 'vite-plus/test'
import { defaultOscillationPrefs, OSCILLATION_PREFS_KEY, readOscillationPrefs, writeOscillationPrefs, type PrefsStorage } from './oscillationPrefs'

function memory(): PrefsStorage {
    const items = new Map<string, string>()
    return {
        getItem: (key) => items.get(key) ?? null,
        setItem: (key, value) => {
            items.set(key, value)
        },
    }
}

describe('oscillation prefs', () => {
    it('defaults to a 6h lookback and a 5c reversal', () => {
        expect(defaultOscillationPrefs()).toEqual({ lookbackHours: 6, thresholdCents: 5 })
        expect(readOscillationPrefs({ getItem: () => null })).toEqual({
            lookbackHours: 6,
            thresholdCents: 5,
        })
        expect(readOscillationPrefs({ getItem: () => 'not-json' })).toEqual({
            lookbackHours: 6,
            thresholdCents: 5,
        })
    })

    it('keeps a saved window and drops values outside the choices', () => {
        const store = memory()
        writeOscillationPrefs(store, { lookbackHours: 24, thresholdCents: 8 })
        expect(readOscillationPrefs(store)).toEqual({ lookbackHours: 24, thresholdCents: 8 })
        expect(store.getItem(OSCILLATION_PREFS_KEY)).toContain('"lookbackHours":24')

        store.setItem(OSCILLATION_PREFS_KEY, '{"lookbackHours":3,"thresholdCents":500}')
        expect(readOscillationPrefs(store)).toEqual({ lookbackHours: 6, thresholdCents: 5 })
    })
})
