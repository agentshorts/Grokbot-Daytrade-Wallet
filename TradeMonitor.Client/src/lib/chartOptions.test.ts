import { describe, expect, it } from 'vite-plus/test'
import { formatInterval, parseInterval } from './chartInterval'
import { defaultChartPrefs, readChartPrefs, rememberInterval, removeCustomInterval, writeChartPrefs, type PrefsStorage } from './chartPrefs'
import { chartQuery, rangeBounds, readChartQuery, startOfYear, wantsOlderHistory } from './chartRange'
import { baselineValue, deltaPoints, heikinAshi, mergeCandles, type CandleBar } from './chartSeries'
import { formatChartCrosshair, formatChartTick, unixFromWall, wallInput, zoneAbbrev } from './chartTime'

const chicago = 'America/Chicago'

function bar(time: number, open: number, high: number, low: number, close: number, volume = 0): CandleBar {
    return { time, open, high, low, close, volume }
}

describe('chart intervals', () => {
    it('parses presets, custom values, and rejects bad input', () => {
        expect(parseInterval('7m')).toEqual({ seconds: 420, key: '7m' })
        expect(parseInterval('4h')).toEqual({ seconds: 14400, key: '4h' })
        expect(parseInterval('90m')).toEqual({ seconds: 5400, key: '90m' })
        expect(parseInterval('2m')?.seconds).toBe(120)
        expect(parseInterval('5h')?.seconds).toBe(18000)
        expect(parseInterval('1W')?.key).toBe('1w')
        expect(parseInterval('1d')?.seconds).toBe(86400)
        for (const key of ['1m', '2m', '5m', '15m', '30m', '1h', '2h', '3h', '5h', '1d', '1w']) {
            expect(parseInterval(key)?.key).toBe(key)
        }
        expect(parseInterval('foo')).toBeNull()
        expect(parseInterval('0m')).toBeNull()
        expect(parseInterval('1.5h')).toBeNull()
        expect(parseInterval('')).toBeNull()
        expect(formatInterval(7200)).toBe('2h')
    })
})

describe('chart time', () => {
    it('shows Chicago wall time and UTC, including both daylight-saving boundaries', () => {
        const afternoon = Date.UTC(2026, 10, 12, 16, 5) / 1000
        expect(formatChartTick(afternoon, chicago, 300)).toBe('10:05 AM')
        expect(formatChartTick(afternoon, 'UTC', 300)).toBe('4:05 PM')
        expect(formatChartCrosshair(afternoon, chicago)).toBe('Nov 12, 10:05 AM')
        expect(zoneAbbrev(afternoon, chicago)).toBe('CST')

        const springBefore = Date.UTC(2026, 2, 8, 7, 30) / 1000
        const springAfter = Date.UTC(2026, 2, 8, 8, 30) / 1000
        expect(formatChartTick(springBefore, chicago, 300)).toBe('1:30 AM')
        expect(formatChartTick(springAfter, chicago, 300)).toBe('3:30 AM')
        expect(zoneAbbrev(springBefore, chicago)).toBe('CST')
        expect(zoneAbbrev(springAfter, chicago)).toBe('CDT')

        const fallBefore = Date.UTC(2026, 10, 1, 6, 30) / 1000
        const fallAfter = Date.UTC(2026, 10, 1, 7, 30) / 1000
        expect(formatChartTick(fallBefore, chicago, 300)).toBe('1:30 AM')
        expect(formatChartTick(fallAfter, chicago, 300)).toBe('1:30 AM')
        expect(zoneAbbrev(fallBefore, chicago)).toBe('CDT')
        expect(zoneAbbrev(fallAfter, chicago)).toBe('CST')
    })

    it('round trips a wall-clock input in a named zone', () => {
        const unix = Date.UTC(2026, 10, 12, 16, 5) / 1000
        expect(wallInput(unix, chicago)).toBe('2026-11-12T10:05')
        expect(unixFromWall('2026-11-12T10:05', chicago)).toBe(unix)
        expect(unixFromWall('2026-03-08T01:30', chicago)).toBe(Date.UTC(2026, 2, 8, 7, 30) / 1000)
        expect(unixFromWall('2026-03-08T03:30', chicago)).toBe(Date.UTC(2026, 2, 8, 8, 30) / 1000)
    })
})

describe('chart range', () => {
    it('builds quick ranges, a year start, and the url query', () => {
        const now = Date.UTC(2026, 10, 12, 16, 5) / 1000
        expect(rangeBounds('1W', now, chicago).from).toBe(now - 7 * 86400)
        expect(rangeBounds('All', now, chicago)).toEqual({ from: null, to: null })
        expect(startOfYear(now, chicago)).toBe(Date.UTC(2026, 0, 1, 6, 0) / 1000)
        expect(chartQuery({ range: '1W', from: 1, to: 2, interval: '5m' })).toEqual({ range: '1W', interval: '5m' })
        expect(chartQuery({ range: 'custom', from: 10, to: 20, interval: '7m' })).toEqual({ range: 'custom', from: '10', to: '20', interval: '7m' })
        expect(readChartQuery({ range: '1W', interval: '4h', from: '5' })).toEqual({ range: '1W', from: 5, to: null, interval: '4h' })
        expect(readChartQuery({ range: 'nope', interval: 'abc' }).range).toBeNull()
        expect(wantsOlderHistory({ from: -3, to: 10 })).toBe(true)
        expect(wantsOlderHistory({ from: 0, to: 10 })).toBe(false)
    })
})

describe('chart series', () => {
    it('builds heikin ashi, delta bars, and a baseline', () => {
        const bars = [bar(0, 10, 12, 9, 11), bar(60, 11, 14, 8, 13)]
        expect(heikinAshi(bars)[0]).toMatchObject({ open: 10.5, close: 10.5, high: 12, low: 9 })
        expect(heikinAshi(bars)[1]).toMatchObject({ open: 10.5, close: 11.5, high: 14, low: 8 })
        expect(deltaPoints(bars, 'change').map((point) => point.value)).toEqual([1, 2])
        expect(deltaPoints(bars, 'percent')[1]?.value).toBeCloseTo((13 / 11 - 1) * 100)
        expect(baselineValue(bars, 'first', 50, 80)).toBe(11)
        expect(baselineValue(bars, 'average', 50, 80)).toBe(50)
        expect(baselineValue(bars, 'typed', 50, 80)).toBe(80)
        expect(mergeCandles([bar(60, 1, 1, 1, 1)], [bar(0, 2, 2, 2, 2), bar(60, 3, 3, 3, 3)]).map((item) => item.close)).toEqual([2, 3])
    })
})

describe('chart prefs', () => {
    it('remembers and removes a custom interval', () => {
        const memory = new Map<string, string>()
        const storage: PrefsStorage = {
            getItem: (key) => memory.get(key) ?? null,
            setItem: (key, value) => memory.set(key, value),
        }
        const saved = rememberInterval(defaultChartPrefs(), '7m')
        expect(saved?.interval).toBe('7m')
        expect(saved?.customIntervals).toEqual(['7m'])
        writeChartPrefs(storage, saved!)
        expect(readChartPrefs(storage).interval).toBe('7m')
        expect(removeCustomInterval(saved!, '7m').customIntervals).toEqual([])
        expect(rememberInterval(defaultChartPrefs(), 'nope')).toBeNull()
    })
})
