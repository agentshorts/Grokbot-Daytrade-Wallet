import { describe, expect, it } from 'vite-plus/test'
import type { DashboardView } from '@/lib/book'
import { createAbortGate, isAbortError } from '@/lib/abort'
import { dashboardChanged, sameRows } from '@/lib/pollGuard'

function view(): DashboardView {
    return {
        emptyBook: false,
        noMatches: false,
        walletNames: { paper: 'Paper' },
        wallets: [],
        kpis: [{ id: 'value', label: 'Value', display: 'money', value: 10, signed: false }],
        trades: [],
        positions: [],
        marks: [],
        equity: [{ key: 'paper', name: 'Paper', color: '#465fff', points: [{ at: '2026-11-01', label: 'Nov 1', value: 10 }] }],
        pnlBars: [],
        pnlBySymbol: [],
        fees: [],
        lastRecordedAt: '2026-11-01T00:00:00.000Z',
        grain: 'day',
    }
}

describe('poll guard', () => {
    it('keeps an identical dashboard poll from replacing the view', () => {
        const current = view()
        expect(dashboardChanged(current, view())).toBe(false)
        const next = view()
        next.kpis = [{ ...next.kpis[0]!, value: 11 }]
        expect(dashboardChanged(current, next)).toBe(true)
        expect(sameRows([{ id: 1 }], [{ id: 1 }], (row) => String(row.id))).toBe(true)
        expect(sameRows([{ id: 1 }], [{ id: 2 }], (row) => String(row.id))).toBe(false)
    })

    it('aborts the read that a newer one replaces', () => {
        const gate = createAbortGate()
        const first = gate.next()
        const second = gate.next()
        expect(first.aborted).toBe(true)
        expect(second.aborted).toBe(false)
        expect(isAbortError(new DOMException('The operation was aborted.', 'AbortError'))).toBe(true)
        gate.abort()
        expect(second.aborted).toBe(true)
    })
})
