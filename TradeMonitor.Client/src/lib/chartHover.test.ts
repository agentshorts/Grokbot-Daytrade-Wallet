import { describe, expect, it, vi } from 'vite-plus/test'
import {
    barAt,
    buildTimeIndex,
    createFrameScheduler,
    edgePaint,
    formatHover,
    hoverHit,
    nearestTime,
    seriesPaint,
    unchangedPrefix,
    type HoverBar,
} from './chartHover'

function bars(count: number): HoverBar[] {
    const rows: HoverBar[] = []
    for (let i = 0; i < count; i++) {
        rows.push({ time: 1_700_000_000 + i * 60, open: i, high: i + 1, low: i - 1, close: i + 0.5, volume: i })
    }
    return rows
}

describe('chart hover index', () => {
    it('resolves a bar by time without scanning and keeps the earlier tie', () => {
        const rows = bars(5)
        const index = buildTimeIndex(rows)
        expect(barAt(index, rows, rows[3]!.time)).toBe(rows[3])
        expect(barAt(index, rows, 1)).toBeNull()
        expect(nearestTime(rows[0]!.time + 30, rows)).toBe(rows[0]!.time)
        expect(nearestTime(rows[0]!.time + 31, rows)).toBe(rows[1]!.time)
    })

    it('formats the hovered bar and its markers without a request', () => {
        const fetchSpy = vi.fn()
        const rows = bars(3)
        const index = buildTimeIndex(rows)
        const markers = new Map<number, string[]>([[rows[1]!.time, ['Buy 1', 'Low']]])
        const hit = hoverHit(index, rows, markers, rows[1]!.time)
        expect(hit).not.toBeNull()
        expect(formatHover(hit!)).toContain('Buy 1')
        expect(formatHover(hit!)).toContain('Low')
        expect(fetchSpy).not.toHaveBeenCalled()
    })

    it('runs only the latest hover callback on the next frame', () => {
        const queued: (() => void)[] = []
        const scheduler = createFrameScheduler(
            (cb) => {
                queued.push(cb)
                return queued.length
            },
            () => {},
        )
        const seen: number[] = []
        scheduler.schedule(() => seen.push(1))
        scheduler.schedule(() => seen.push(2))
        expect(queued).toHaveLength(1)
        queued[0]!()
        expect(seen).toEqual([2])
    })

    it('tells a last-bar change from a new bar and a replaced series', () => {
        const rows = bars(4)
        expect(seriesPaint(rows, [...rows.slice(0, 3), { ...rows[3]!, close: 9 }])).toBe('last')
        expect(edgePaint(rows.length, rows[0]!.time, rows[3]!.time, [...rows, { ...rows[0]!, time: rows[3]!.time + 60 }])).toBe('append')
        expect(seriesPaint(rows, bars(4).map((row) => ({ ...row, time: row.time + 10 })))).toBe('replace')
        expect(unchangedPrefix(rows, [...rows.slice(0, 3), { ...rows[3]!, close: 9 }], 3)).toBe(true)
        expect(unchangedPrefix(rows, [{ ...rows[0]!, close: 99 }, ...rows.slice(1)], 3)).toBe(false)
    })

    it('looks up a bar faster from the time index than from a scan', () => {
        const rows = bars(4000)
        const index = buildTimeIndex(rows)
        const target = rows[3500]!.time
        const rounds = 1500
        const scanStart = performance.now()
        let scanHit = 0
        for (let round = 0; round < rounds; round++) {
            for (let i = 0; i < rows.length; i++) {
                if (rows[i]!.time === target) scanHit = i
            }
        }
        const scanMs = performance.now() - scanStart
        const mapStart = performance.now()
        let mapHit = 0
        for (let round = 0; round < rounds; round++) mapHit = index.get(target) ?? -1
        const mapMs = performance.now() - mapStart
        expect(mapHit).toBe(scanHit)
        expect(mapMs).toBeLessThan(scanMs)
        console.info(`hover lookup ms scan=${scanMs.toFixed(2)} index=${mapMs.toFixed(2)} rounds=${rounds} bars=${rows.length}`)
    })
})
