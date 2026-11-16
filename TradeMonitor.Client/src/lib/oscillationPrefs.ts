export const OSCILLATION_PREFS_KEY = 'trademonitor.oscillation'

export const lookbackChoices = [
    { hours: 1, label: '1h' },
    { hours: 6, label: '6h' },
    { hours: 12, label: '12h' },
    { hours: 24, label: '24h' },
    { hours: 72, label: '3d' },
] as const

export interface OscillationPrefs {
    lookbackHours: number
    thresholdCents: number
}

export interface PrefsStorage {
    getItem(key: string): string | null
    setItem(key: string, value: string): void
}

export function defaultOscillationPrefs(): OscillationPrefs {
    return { lookbackHours: 6, thresholdCents: 5 }
}

export function readOscillationPrefs(storage: Pick<PrefsStorage, 'getItem'>): OscillationPrefs {
    const fallback = defaultOscillationPrefs()
    let raw: string | null
    try {
        raw = storage.getItem(OSCILLATION_PREFS_KEY)
    } catch {
        return fallback
    }
    if (!raw) return fallback
    try {
        return sanitize(JSON.parse(raw) as Partial<OscillationPrefs>, fallback)
    } catch {
        return fallback
    }
}

export function writeOscillationPrefs(storage: Pick<PrefsStorage, 'setItem'>, prefs: OscillationPrefs) {
    storage.setItem(OSCILLATION_PREFS_KEY, JSON.stringify(sanitize(prefs, defaultOscillationPrefs())))
}

function sanitize(value: Partial<OscillationPrefs>, fallback: OscillationPrefs): OscillationPrefs {
    const hours =
        typeof value.lookbackHours === 'number' && lookbackChoices.some((item) => item.hours === value.lookbackHours)
            ? value.lookbackHours
            : fallback.lookbackHours
    return {
        lookbackHours: hours,
        thresholdCents: keepCents(value.thresholdCents, fallback.thresholdCents),
    }
}

function keepCents(value: unknown, fallback: number): number {
    if (typeof value !== 'number' || !Number.isFinite(value) || value <= 0 || value > 50) return fallback
    return Math.round(value * 10) / 10
}
