import { describe, expect, it } from 'vite-plus/test'
import { readPanelTab, readPanelWidth, readShowTrades, writePanelTab, writePanelWidth, writeShowTrades, type PanelStorage } from '@/lib/livePanel'

function memory(): PanelStorage & { values: Map<string, string> } {
    const values = new Map<string, string>()
    return {
        values,
        getItem: (key) => values.get(key) ?? null,
        setItem: (key, value) => {
            values.set(key, value)
        },
    }
}

describe('live panel memory', () => {
    it('remembers width and tab', () => {
        const storage = memory()
        expect(readPanelWidth(storage)).toBe('normal')
        expect(readPanelTab(storage)).toBe('quick')
        writePanelWidth(storage, 'expanded')
        writePanelTab(storage, 'orders')
        expect(readPanelWidth(storage)).toBe('expanded')
        expect(readPanelTab(storage)).toBe('orders')
        storage.setItem('trademonitor.live.panel', 'collapsed')
        expect(readPanelWidth(storage)).toBe('collapsed')
        storage.setItem('trademonitor.live.panel', 'wide')
        expect(readPanelWidth(storage)).toBe('normal')
        writePanelTab(storage, 'lots')
        expect(readPanelTab(storage)).toBe('lots')
        expect(readShowTrades(storage)).toBe(true)
        writeShowTrades(storage, false)
        expect(readShowTrades(storage)).toBe(false)
        writeShowTrades(storage, true)
        expect(readShowTrades(storage)).toBe(true)
    })
})
