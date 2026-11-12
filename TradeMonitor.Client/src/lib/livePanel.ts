export type PanelWidth = 'collapsed' | 'normal' | 'expanded'
export type PanelTab = 'quick' | 'orders'

export const PANEL_WIDTH_KEY = 'trademonitor.live.panel'
export const PANEL_TAB_KEY = 'trademonitor.live.tab'

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
    return storage.getItem(PANEL_TAB_KEY) === 'orders' ? 'orders' : 'quick'
}

export function writePanelWidth(storage: PanelStorage, width: PanelWidth) {
    storage.setItem(PANEL_WIDTH_KEY, width)
}

export function writePanelTab(storage: PanelStorage, tab: PanelTab) {
    storage.setItem(PANEL_TAB_KEY, tab)
}
