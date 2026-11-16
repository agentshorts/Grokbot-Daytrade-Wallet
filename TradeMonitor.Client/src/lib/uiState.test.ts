import { describe, expect, it } from 'vite-plus/test'
import {
    UI_STATE_KEY,
    readNavCollapsed,
    readSectionHidden,
    readShowArchived,
    readUiState,
    readViewFilter,
    writeNavCollapsed,
    writeSectionHidden,
    writeShowArchived,
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
        expect(readUiState(storage)).toEqual({ navCollapsed: false, hidden: [], filters: {}, showArchived: false })
        expect(readShowArchived(storage)).toBe(false)
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
            showArchived: false,
        })
        expect(readSectionHidden(storage, 'Not-An-Id')).toBe(false)
        writeViewFilter(storage, 'Bad', 'eth')
        expect(readViewFilter(storage, 'wallets')).toBe('  btc  ')
        expect(readShowArchived(storage)).toBe(false)
    })

    it('stores show archived with the other ui fields', () => {
        const storage = memory()
        writeNavCollapsed(storage, true)
        writeViewFilter(storage, 'wallets', 'btc')
        writeShowArchived(storage, true)
        expect(readShowArchived(storage)).toBe(true)
        expect(readNavCollapsed(storage)).toBe(true)
        expect(readViewFilter(storage, 'wallets')).toBe('btc')
        expect(JSON.parse(storage.getItem(UI_STATE_KEY) ?? '{}')).toMatchObject({ showArchived: true, navCollapsed: true })

        writeShowArchived(storage, false)
        expect(readShowArchived(storage)).toBe(false)
        expect(readNavCollapsed(storage)).toBe(true)
        expect(readViewFilter(storage, 'wallets')).toBe('btc')
    })
})
