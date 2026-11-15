export type PanelWidth = 'collapsed' | 'normal' | 'expanded'
export type PanelTab = 'quick' | 'orders' | 'lots'

export const PANEL_WIDTH_KEY = 'trademonitor.live.panel'
export const PANEL_TAB_KEY = 'trademonitor.live.tab'
export const SHOW_TRADES_KEY = 'trademonitor.live.showTrades'

export interface PanelStorage {
    getItem(key: string): string | null
    setItem(key: string, value: string): void
}

export function readPanelWidth(storage: PanelStorage): PanelWidth {
    const value = storage.getItem(PANEL_WIDTH_KEY)
    if (value === 'collapsed' || value === 'normal' || value === 'expanded') return value
    return 'normal'
}

export function readPanelTab(storage: PanelStorage): PanelTab {
    const value = storage.getItem(PANEL_TAB_KEY)
    if (value === 'orders' || value === 'lots') return value
    return 'quick'
}

export function readShowTrades(storage: PanelStorage): boolean {
    return storage.getItem(SHOW_TRADES_KEY) !== '0'
}

export function writeShowTrades(storage: PanelStorage, on: boolean) {
    storage.setItem(SHOW_TRADES_KEY, on ? '1' : '0')
}

export function writePanelWidth(storage: PanelStorage, width: PanelWidth) {
    storage.setItem(PANEL_WIDTH_KEY, width)
}

export function writePanelTab(storage: PanelStorage, tab: PanelTab) {
    storage.setItem(PANEL_TAB_KEY, tab)
}
