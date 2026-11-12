import { isAtrHorizon, type AtrHorizon } from '@/lib/atrBands'
import { intervalPresets, parseInterval } from '@/lib/chartInterval'
import { quickRanges, type ChartRange } from '@/lib/chartRange'
import { chartTypes, isChartType, type BaselineSource, type ChartType, type DeltaMode } from '@/lib/chartSeries'
import type { ChartZoneMode } from '@/lib/chartTime'

export const CHART_PREFS_KEY = 'trademonitor.chart'

export interface ChartPrefs {
    type: ChartType
    interval: string
    range: ChartRange
    timeZone: ChartZoneMode
    favoriteTypes: ChartType[]
    favoriteIntervals: string[]
    customIntervals: string[]
    showVolume: boolean
    baselineSource: BaselineSource
    baselinePrice: number | null
    deltaMode: DeltaMode
    showSwing: boolean
    showAtr: boolean
    atrHorizon: AtrHorizon
}

export interface PrefsStorage {
    getItem(key: string): string | null
    setItem(key: string, value: string): void
}

export function defaultChartPrefs(): ChartPrefs {
    return {
        type: 'candle',
        interval: '5m',
        range: 'All',
        timeZone: 'local',
        favoriteTypes: ['candle', 'line', 'vertex-step', 'vertex-baseline', 'area'],
        favoriteIntervals: ['1m', '5m', '15m', '1h', '1d'],
        customIntervals: [],
        showVolume: false,
        baselineSource: 'first',
        baselinePrice: null,
        deltaMode: 'change',
        showSwing: false,
        showAtr: false,
        atrHorizon: '1bar',
    }
}

export function readChartPrefs(storage: PrefsStorage): ChartPrefs {
    const fallback = defaultChartPrefs()
    const raw = storage.getItem(CHART_PREFS_KEY)
    if (!raw) return fallback
    try {
        const parsed = JSON.parse(raw) as Partial<ChartPrefs>
        return sanitize(parsed, fallback)
    } catch {
        return fallback
    }
}

export function writeChartPrefs(storage: PrefsStorage, prefs: ChartPrefs) {
    storage.setItem(CHART_PREFS_KEY, JSON.stringify(sanitize(prefs, defaultChartPrefs())))
}

export function rememberInterval(prefs: ChartPrefs, text: string): ChartPrefs | null {
    const parsed = parseInterval(text)
    if (!parsed) return null
    const custom = intervalPresets.includes(parsed.key as (typeof intervalPresets)[number])
        ? prefs.customIntervals
        : [...new Set([...prefs.customIntervals, parsed.key])]
    const favorite = prefs.favoriteIntervals.includes(parsed.key) ? prefs.favoriteIntervals : [...prefs.favoriteIntervals, parsed.key]
    return { ...prefs, interval: parsed.key, customIntervals: custom, favoriteIntervals: favorite }
}

export function removeCustomInterval(prefs: ChartPrefs, key: string): ChartPrefs {
    return {
        ...prefs,
        customIntervals: prefs.customIntervals.filter((item) => item !== key),
        favoriteIntervals: prefs.favoriteIntervals.filter((item) => item !== key),
        interval: prefs.interval === key ? '5m' : prefs.interval,
    }
}

function sanitize(value: Partial<ChartPrefs>, fallback: ChartPrefs): ChartPrefs {
    const type = typeof value.type === 'string' && isChartType(value.type) ? value.type : fallback.type
    const interval = typeof value.interval === 'string' && parseInterval(value.interval) ? parseInterval(value.interval)!.key : fallback.interval
    const range = typeof value.range === 'string' && (quickRanges.includes(value.range as (typeof quickRanges)[number]) || value.range === 'custom') ? (value.range as ChartRange) : fallback.range
    const timeZone = value.timeZone === 'utc' ? 'utc' : 'local'
    const favoriteTypes = cleanTypes(value.favoriteTypes, fallback.favoriteTypes)
    const favoriteIntervals = cleanIntervals(value.favoriteIntervals, fallback.favoriteIntervals)
    const customIntervals = cleanIntervals(value.customIntervals, []).filter((item) => !intervalPresets.includes(item as (typeof intervalPresets)[number]))
    const baselineSource = value.baselineSource === 'average' || value.baselineSource === 'typed' ? value.baselineSource : 'first'
    const baselinePrice = typeof value.baselinePrice === 'number' && value.baselinePrice > 0 ? value.baselinePrice : null
    const deltaMode = value.deltaMode === 'percent' ? 'percent' : 'change'
    return {
        type,
        interval,
        range,
        timeZone,
        favoriteTypes,
        favoriteIntervals,
        customIntervals,
        showVolume: value.showVolume === true,
        baselineSource,
        baselinePrice,
        deltaMode,
        showSwing: value.showSwing === true,
        showAtr: value.showAtr === true,
        atrHorizon: typeof value.atrHorizon === 'string' && isAtrHorizon(value.atrHorizon) ? value.atrHorizon : '1bar',
    }
}

function cleanTypes(value: unknown, fallback: ChartType[]): ChartType[] {
    if (!Array.isArray(value)) return fallback
    const next = value.filter((item): item is ChartType => typeof item === 'string' && isChartType(item))
    return next.length ? [...new Set(next)] : fallback
}

function cleanIntervals(value: unknown, fallback: string[]): string[] {
    if (!Array.isArray(value)) return fallback
    const next = value.flatMap((item) => (typeof item === 'string' && parseInterval(item) ? [parseInterval(item)!.key] : []))
    return next.length || fallback.length === 0 ? [...new Set(next)] : fallback
}

export const allChartTypes = chartTypes
