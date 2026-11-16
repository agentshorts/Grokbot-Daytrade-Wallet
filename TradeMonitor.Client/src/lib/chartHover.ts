export interface Timed {
    time: number
}

export interface HoverBar extends Timed {
    open: number
    high: number
    low: number
    close: number
    volume: number
}

export interface HoverHit {
    time: number
    open: number
    high: number
    low: number
    close: number
    volume: number
    markers: string[]
}

export type SeriesPaint = 'append' | 'last' | 'replace'

/** Bar time to row index. Hover lookup stays O(1) instead of scanning the series. */
export function buildTimeIndex(bars: readonly Timed[]): Map<number, number> {
    const index = new Map<number, number>()
    for (let i = 0; i < bars.length; i++) index.set(bars[i]!.time, i)
    return index
}

export function barAt<T>(index: Map<number, number>, bars: readonly T[], time: number): T | null {
    const found = index.get(time)
    if (found == null) return null
    return bars[found] ?? null
}

/** Closest time on a sorted series. Ties keep the earlier bar, matching a forward scan. */
export function nearestTime(time: number, points: readonly Timed[]): number | null {
    const row = nearestBar(points, time)
    return row ? row.time : null
}

export function nearestBar<T extends Timed>(bars: readonly T[], time: number): T | null {
    if (bars.length === 0) return null
    let lo = 0
    let hi = bars.length - 1
    while (lo < hi) {
        const mid = (lo + hi) >> 1
        if (bars[mid]!.time < time) lo = mid + 1
        else hi = mid
    }
    const at = bars[lo]!
    const prev = lo > 0 ? bars[lo - 1]! : null
    if (prev && Math.abs(prev.time - time) <= Math.abs(at.time - time)) return prev
    return at
}

export function indexMarkerText(marks: readonly { time: number; text: string }[]): Map<number, string[]> {
    const map = new Map<number, string[]>()
    for (const mark of marks) {
        const list = map.get(mark.time)
        if (list) list.push(mark.text)
        else map.set(mark.time, [mark.text])
    }
    return map
}

export function hoverHit(index: Map<number, number>, bars: readonly HoverBar[], markers: Map<number, string[]>, time: number): HoverHit | null {
    const bar = barAt(index, bars, time) ?? nearestBar(bars, time)
    if (!bar) return null
    return {
        time: bar.time,
        open: bar.open,
        high: bar.high,
        low: bar.low,
        close: bar.close,
        volume: bar.volume,
        markers: markers.get(bar.time) ?? [],
    }
}

export function formatHover(hit: HoverHit): string {
    const line = `O ${trim(hit.open)}  H ${trim(hit.high)}  L ${trim(hit.low)}  C ${trim(hit.close)}`
    if (hit.markers.length === 0) return line
    return `${line}\n${hit.markers.join('\n')}`
}

export function formatLineHover(value: number, markers: readonly string[]): string {
    const line = trim(value)
    if (markers.length === 0) return line
    return `${line}\n${markers.join('\n')}`
}

/** Decide whether the chart can move one bar or must replace the series. */
export function edgePaint(drawnCount: number, drawnFirst: number, drawnLast: number, next: readonly Timed[]): SeriesPaint {
    if (drawnCount <= 0 || next.length === 0) return 'replace'
    if (next[0]!.time !== drawnFirst) return 'replace'
    if (next.length === drawnCount + 1 && next[next.length - 2]?.time === drawnLast) return 'append'
    if (next.length === drawnCount && next[next.length - 1]!.time === drawnLast) return 'last'
    return 'replace'
}

export function unchangedPrefix<T extends Timed & { value?: number; close?: number }>(previous: readonly T[], next: readonly T[], count: number): boolean {
    for (let i = 0; i < count; i++) {
        const left = previous[i]
        const right = next[i]
        if (!left || !right || left.time !== right.time) return false
        if (left.value !== undefined && left.value !== right.value) return false
        if (left.close !== undefined && left.close !== right.close) return false
    }
    return true
}

export function seriesPaint(previous: readonly Timed[], next: readonly Timed[]): SeriesPaint {
    if (previous.length === 0) return 'replace'
    return edgePaint(previous.length, previous[0]!.time, previous[previous.length - 1]!.time, next)
}

export interface FrameScheduler {
    schedule(work: () => void): void
    cancel(): void
}

/** Keep the newest hover callback and run it on the next frame. Never starts a request. */
export function createFrameScheduler(frame: (cb: () => void) => number = requestAnimationFrame, cancelFrame: (id: number) => void = cancelAnimationFrame): FrameScheduler {
    let id = 0
    let queued: (() => void) | null = null
    return {
        schedule(work: () => void) {
            queued = work
            if (id) return
            id = frame(() => {
                id = 0
                const run = queued
                queued = null
                run?.()
            })
        },
        cancel() {
            if (id) cancelFrame(id)
            id = 0
            queued = null
        },
    }
}

function trim(value: number): string {
    if (!Number.isFinite(value)) return ''
    const digits = value >= 1000 ? 2 : value >= 1 ? 4 : 6
    return value.toFixed(digits).replace(/\.?0+$/, '')
}
