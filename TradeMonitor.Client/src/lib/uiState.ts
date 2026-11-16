export const UI_STATE_KEY = 'trademonitor.ui'

export interface UiStorage {
    getItem(key: string): string | null
    setItem(key: string, value: string): void
}

export interface UiState {
    navCollapsed: boolean
    hidden: string[]
    filters: Record<string, string>
    showArchived: boolean
}

const idPattern = /^[a-z0-9-]{1,64}$/

export function defaultUiState(): UiState {
    return { navCollapsed: false, hidden: [], filters: {}, showArchived: false }
}

export function readUiState(storage: UiStorage): UiState {
    const raw = storage.getItem(UI_STATE_KEY)
    if (!raw) return defaultUiState()
    try {
        return sanitize(JSON.parse(raw))
    } catch {
        return defaultUiState()
    }
}

export function writeUiState(storage: UiStorage, state: UiState) {
    storage.setItem(UI_STATE_KEY, JSON.stringify(sanitize(state)))
}

export function readNavCollapsed(storage: UiStorage): boolean {
    return readUiState(storage).navCollapsed
}

export function writeNavCollapsed(storage: UiStorage, collapsed: boolean) {
    const state = readUiState(storage)
    state.navCollapsed = collapsed
    writeUiState(storage, state)
}

export function readSectionHidden(storage: UiStorage, id: string): boolean {
    if (!isId(id)) return false
    return readUiState(storage).hidden.includes(id)
}

export function writeSectionHidden(storage: UiStorage, id: string, hidden: boolean) {
    if (!isId(id)) return
    const state = readUiState(storage)
    const next = hidden ? [...new Set([...state.hidden, id])] : state.hidden.filter((item) => item !== id)
    state.hidden = next
    writeUiState(storage, state)
}

export function readViewFilter(storage: UiStorage, view: string): string {
    if (!isId(view)) return ''
    return readUiState(storage).filters[view] ?? ''
}

export function writeViewFilter(storage: UiStorage, view: string, text: string) {
    if (!isId(view)) return
    const state = readUiState(storage)
    const clean = text.slice(0, 200)
    if (clean.trim() === '') delete state.filters[view]
    else state.filters[view] = clean
    writeUiState(storage, state)
}

export function readShowArchived(storage: UiStorage): boolean {
    return readUiState(storage).showArchived
}

export function writeShowArchived(storage: UiStorage, showArchived: boolean) {
    const state = readUiState(storage)
    state.showArchived = showArchived
    writeUiState(storage, state)
}

function isId(value: string): boolean {
    return idPattern.test(value)
}

function sanitize(value: unknown): UiState {
    const fallback = defaultUiState()
    if (!value || typeof value !== 'object') return fallback
    const record = value as Partial<UiState>
    const hidden = Array.isArray(record.hidden)
        ? [...new Set(record.hidden.filter((item): item is string => typeof item === 'string' && isId(item)))].slice(0, 100)
        : []
    const filters: Record<string, string> = {}
    if (record.filters && typeof record.filters === 'object') {
        for (const [key, text] of Object.entries(record.filters)) {
            if (!isId(key) || typeof text !== 'string') continue
            const clean = text.slice(0, 200)
            if (clean.trim() === '') continue
            filters[key] = clean
            if (Object.keys(filters).length >= 40) break
        }
    }
    return {
        navCollapsed: record.navCollapsed === true,
        hidden,
        filters,
        showArchived: record.showArchived === true,
    }
}
