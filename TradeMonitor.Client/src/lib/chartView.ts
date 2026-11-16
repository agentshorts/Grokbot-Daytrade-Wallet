export interface ChartSpan {
    from: number
    to: number
}

/** Visible logical indexes and the UTC time window they cover. */
export interface ChartView {
    logical: ChartSpan | null
    time: ChartSpan | null
}

export function chartViewKey(input: { symbol: string; interval: string; range: string; from: number | null; to: number | null }): string {
    return `${input.symbol}:${input.interval}:${input.range}:${input.from ?? ''}:${input.to ?? ''}`
}

function span(value: { from: number; to: number } | null | undefined): ChartSpan | null {
    if (!value) return null
    if (!Number.isFinite(value.from) || !Number.isFinite(value.to) || value.to <= value.from) return null
    return { from: value.from, to: value.to }
}

export function captureChartView(
    logical: { from: number; to: number } | null | undefined,
    time: { from: number; to: number } | null | undefined,
): ChartView | null {
    const view = { logical: span(logical), time: span(time) }
    if (!view.logical && !view.time) return null
    return view
}

/** A graph-type switch passes refit false so the same window is put back. A new symbol or timeframe passes true. */
export function viewToRestore(view: ChartView | null, refit: boolean): ChartView | null {
    if (refit || !view) return null
    return view
}

export function logicalRangeToApply(view: ChartView | null): ChartSpan | null {
    return view?.logical ?? null
}

export function timeRangeToApply(view: ChartView | null): ChartSpan | null {
    if (view?.logical) return null
    return view?.time ?? null
}
