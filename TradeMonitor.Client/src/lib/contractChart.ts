import type { ContractDetail, ContractMark, OscillationRow } from '@/api'
import { nearestTime } from '@/lib/chartHover'
import type { ChartMarker, MarkerSide, PricePoint } from '@/lib/priceSeries'

export interface SideQuote {
    cents: number | null
    bidCents: number | null
    askCents: number | null
    label: string
}

export function centsText(value: number): string {
    const rounded = Math.round(value * 10) / 10
    return Number.isInteger(rounded) ? String(rounded) : rounded.toFixed(1)
}

export function formatSideQuote(quote: SideQuote): string {
    const bid = finite(quote.bidCents)
    const ask = finite(quote.askCents)
    if (quote.label === 'bid/ask' && bid != null && ask != null) return `${centsText(bid)}/${centsText(ask)}c bid/ask`
    const cents = finite(quote.cents)
    if (cents == null) return 'n/a'
    return `${centsText(cents)}c${quote.label ? ` ${quote.label}` : ''}`
}

export function rowYes(row: OscillationRow): SideQuote {
    if (row.yesLabel || row.yesCents != null || row.yesBidCents != null || row.yesAskCents != null) {
        return { cents: row.yesCents, bidCents: row.yesBidCents, askCents: row.yesAskCents, label: row.yesLabel }
    }
    return { cents: row.priceCents, bidCents: null, askCents: null, label: row.priceCents == null ? '' : 'last' }
}

export function rowNo(row: OscillationRow): SideQuote {
    if (row.noLabel || row.noCents != null || row.noBidCents != null || row.noAskCents != null) {
        return { cents: row.noCents, bidCents: row.noBidCents, askCents: row.noAskCents, label: row.noLabel }
    }
    return {
        cents: row.priceCents == null ? null : 100 - row.priceCents,
        bidCents: null,
        askCents: null,
        label: row.priceCents == null ? '' : 'implied',
    }
}

export function formatStrikeLabel(strike: number | null, direction: string): string {
    if (strike == null || !Number.isFinite(strike)) return ''
    const digits = Number.isInteger(strike) ? 0 : 2
    const text = strike.toLocaleString('en-US', {
        style: 'currency',
        currency: 'USD',
        minimumFractionDigits: digits,
        maximumFractionDigits: digits,
    })
    if (direction === 'above') return `${text} or above`
    if (direction === 'below') return `${text} or below`
    return text
}

export function formatAvgCost(dollars: number): string {
    if (!Number.isFinite(dollars)) return 'n/a'
    return `${centsText(dollars * 100)}c`
}

export function noLineNote(detail: Pick<ContractDetail, 'noImplied' | 'noLineImplied'>): string {
    if (!detail.noLineImplied) return ''
    if (detail.noImplied) return 'The NO line is 1 minus YES. This source publishes the YES price.'
    return 'The NO line is 1 minus YES. The current NO quote is the live bid and ask.'
}

export function contractPath(ticker: string, lookbackHours?: number | null, thresholdCents?: number | null) {
    const query: Record<string, string> = {}
    if (lookbackHours != null) query.hours = String(lookbackHours)
    if (thresholdCents != null) query.cents = String(thresholdCents)
    return { path: `/predictions/contract/${encodeURIComponent(ticker)}`, query }
}

export function toContractSeries(points: { at: string; yesCents: number; noCents: number }[]): { yes: PricePoint[]; no: PricePoint[] } {
    const byTime = new Map<number, { yes: number; no: number }>()
    for (const point of points) {
        const time = Math.floor(new Date(point.at).getTime() / 1000)
        if (Number.isNaN(time)) continue
        byTime.set(time, { yes: point.yesCents, no: point.noCents })
    }
    const times = [...byTime.keys()].sort((a, b) => a - b)
    return {
        yes: times.map((time) => ({ time, value: byTime.get(time)!.yes })),
        no: times.map((time) => ({ time, value: byTime.get(time)!.no })),
    }
}

export function toContractMarkers(marks: ContractMark[], points: PricePoint[]): ChartMarker[] {
    return marks
        .map((mark) => {
            const raw = Math.floor(new Date(mark.at).getTime() / 1000)
            if (Number.isNaN(raw)) return null
            return { time: points.length === 0 ? raw : (nearestTime(raw, points) ?? raw), side: markerSide(mark.kind), text: mark.text }
        })
        .filter((mark): mark is ChartMarker => mark != null)
        .sort((a, b) => a.time - b.time || a.text.localeCompare(b.text))
}

function markerSide(kind: string): MarkerSide {
    if (kind === 'buy' || kind === 'sell' || kind === 'low') return kind
    return 'high'
}

function finite(value: number | null): number | null {
    return value != null && Number.isFinite(value) ? value : null
}
