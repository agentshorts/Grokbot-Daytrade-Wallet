import { describe, expect, it } from 'vite-plus/test'
import { contractPath, formatAvgCost, formatSideQuote, formatStrikeLabel, noLineNote, rowNo, rowYes, toContractMarkers, toContractSeries } from './contractChart'
import type { OscillationRow } from '@/api'

const row = {
    ticker: 'KXBTCD-26NOV1617-T64999.99',
    productId: 'KXBTCD-26NOV1617-T64999.99-KALSHI',
    series: 'KXBTCD',
    wallet: null,
    name: 'BTC',
    qty: 0,
    priceCents: 56,
    yesCents: null,
    yesBidCents: null,
    yesAskCents: null,
    yesLabel: '',
    noCents: null,
    noBidCents: null,
    noAskCents: null,
    noLabel: '',
    noImplied: false,
    highCents: null,
    lowCents: null,
    rangeCents: 0,
    rangePercent: null,
    reversals: 0,
    oscillating: false,
    sparkline: [],
    closeAt: null,
    closeCentral: null,
    source: 'kalshi',
    error: null,
} satisfies OscillationRow

describe('contract path', () => {
    it('keeps a dotted ticker in one encoded segment', () => {
        expect(contractPath('KXBTCD-26NOV1617-T64999.99', 6, 5)).toEqual({
            path: '/predictions/contract/KXBTCD-26NOV1617-T64999.99',
            query: { hours: '6', cents: '5' },
        })
        expect(contractPath('A B/C?D#E%F').path).toBe('/predictions/contract/A%20B%2FC%3FD%23E%25F')
        expect(contractPath('KXBTCD-26NOV1617-T64999.99').query).toEqual({})
    })
})

describe('contract quotes', () => {
    it('labels bid and ask, and falls back to last and implied NO', () => {
        expect(formatSideQuote({ cents: 56, bidCents: 54, askCents: 55, label: 'bid/ask' })).toBe('54/55c bid/ask')
        expect(formatSideQuote({ cents: 56, bidCents: null, askCents: null, label: 'last' })).toBe('56c last')
        expect(formatSideQuote({ cents: null, bidCents: null, askCents: null, label: '' })).toBe('n/a')
        expect(formatSideQuote(rowYes(row))).toBe('56c last')
        expect(formatSideQuote(rowNo(row))).toBe('44c implied')
        expect(formatStrikeLabel(64999.99, 'above')).toBe('$64,999.99 or above')
        expect(formatStrikeLabel(2500, 'below')).toBe('$2,500 or below')
        expect(formatAvgCost(0.4)).toBe('40c')
        expect(noLineNote({ noLineImplied: true, noImplied: true })).toContain('1 minus YES')
        expect(noLineNote({ noLineImplied: true, noImplied: false })).toContain('live bid and ask')
    })

    it('snaps a fill onto the nearest YES point and keeps reversal marks', () => {
        const series = toContractSeries([
            { at: '2026-11-16T18:00:00.000Z', yesCents: 40, noCents: 60 },
            { at: '2026-11-16T18:05:00.000Z', yesCents: 45, noCents: 55 },
        ])
        expect(series.yes.map((point) => point.value)).toEqual([40, 45])
        expect(series.no.map((point) => point.value)).toEqual([60, 55])
        const markers = toContractMarkers(
            [
                { at: '2026-11-16T18:00:00.000Z', kind: 'low', text: 'Low' },
                { at: '2026-11-16T18:05:30.000Z', kind: 'high', text: 'High' },
                { at: '2026-11-16T18:04:00.000Z', kind: 'buy', text: 'Buy 4 @ 40c' },
            ],
            series.yes,
        )
        expect(markers.map((marker) => marker.text)).toEqual(['Low', 'Buy 4 @ 40c', 'High'])
        expect(markers[1]?.time).toBe(series.yes[1]?.time)
        expect(markers[1]?.side).toBe('buy')
        expect(markers[0]?.side).toBe('low')
        expect(markers[2]?.side).toBe('high')
    })
})
