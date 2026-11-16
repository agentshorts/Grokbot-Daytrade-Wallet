import { describe, expect, it } from 'vite-plus/test'
import {
    UI_STATE_KEY,
    readNavCollapsed,
    readSectionHidden,
    readUiState,
    readViewFilter,
    writeNavCollapsed,
    writeSectionHidden,
    writeViewFilter,
    type UiStorage,
} from './uiState'

function memory(): UiStorage {
    const map = new Map<string, string>()
    return {
        getItem: (key) => map.get(key) ?? null,
        setItem: (key, value) => {
            map.set(key, value)
        },
    }
}

describe('ui state', () => {
    it('starts expanded with no filters', () => {
        const storage = memory()
        expect(readUiState(storage)).toEqual({ navCollapsed: false, hidden: [], filters: {} })
        expect(readNavCollapsed(storage)).toBe(false)
        expect(readViewFilter(storage, 'wallets')).toBe('')
        expect(readSectionHidden(storage, 'live-toolbar')).toBe(false)
    })

    it('stores the nav, a hidden section, and each view filter without clobbering the others', () => {
        const storage = memory()
        writeNavCollapsed(storage, true)
        writeSectionHidden(storage, 'live-toolbar', true)
        writeViewFilter(storage, 'wallets', 'btc')
        writeViewFilter(storage, 'trades', 'sell')
        expect(readNavCollapsed(storage)).toBe(true)
        expect(readSectionHidden(storage, 'live-toolbar')).toBe(true)
        expect(readViewFilter(storage, 'wallets')).toBe('btc')
        expect(readViewFilter(storage, 'trades')).toBe('sell')

        writeSectionHidden(storage, 'live-toolbar', false)
        writeViewFilter(storage, 'trades', '   ')
        expect(readSectionHidden(storage, 'live-toolbar')).toBe(false)
        expect(readViewFilter(storage, 'trades')).toBe('')
        expect(readNavCollapsed(storage)).toBe(true)
        expect(readViewFilter(storage, 'wallets')).toBe('btc')
    })

    it('ignores corrupt and unknown values', () => {
        const storage = memory()
        storage.setItem(UI_STATE_KEY, '{')
        expect(readNavCollapsed(storage)).toBe(false)

        storage.setItem(
            UI_STATE_KEY,
            JSON.stringify({
                navCollapsed: 'yes',
                hidden: [1, 'live-toolbar', 'live-toolbar', 'Bad'],
                filters: { wallets: '  btc  ', nope: 3, 'Has Space': 'x' },
            }),
        )
        expect(readUiState(storage)).toEqual({
            navCollapsed: false,
            hidden: ['live-toolbar'],
            filters: { wallets: '  btc  ' },
        })
        expect(readSectionHidden(storage, 'Not-An-Id')).toBe(false)
        writeViewFilter(storage, 'Bad', 'eth')
        expect(readViewFilter(storage, 'wallets')).toBe('  btc  ')
    })
})
