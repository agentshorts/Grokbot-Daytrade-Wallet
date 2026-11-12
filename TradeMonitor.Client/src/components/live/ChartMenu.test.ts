import { describe, expect, it } from 'vite-plus/test'
import { mount } from '@vue/test-utils'
import ChartMenu from './ChartMenu.vue'
import { chartMenu, hitTest } from '@/lib/chartMenu'

function labels(hit: Parameters<typeof chartMenu>[0], pendingMoveId?: number) {
    const wrapper = mount(ChartMenu, { props: { hit, x: 12, y: 24, pendingMoveId } })
    return wrapper.findAll('[role="menuitem"]').map((item) => item.text())
}

describe('chart context menu', () => {
    it('maps the price under the cursor to limit, stop, and take-profit actions', () => {
        expect(labels({ kind: 'price', price: 15.25 })).toEqual([
            'Limit buy at $15.25',
            'Limit sell at $15.25',
            'Stop-loss at $15.25',
            'Take-profit at $15.25',
        ])
        expect(chartMenu({ kind: 'price', price: 4.375 }).map((action) => action.label)).toEqual([
            'Limit buy at $4.375',
            'Limit sell at $4.375',
            'Stop-loss at $4.375',
            'Take-profit at $4.375',
        ])
        expect(chartMenu({ kind: 'price', price: 15 })[0]?.label).toBe('Limit buy at $15')
    })

    it('offers a move when an order is armed, and lot or order actions on those lines', () => {
        expect(labels({ kind: 'price', price: 20 }, 7)[0]).toBe('Move order to $20')
        expect(labels({ kind: 'lot', lotId: 3, price: 15 })).toEqual(['Quick sell this lot'])
        expect(labels({ kind: 'order', orderId: 9, price: 20 })).toEqual(['Cancel order', 'Move order here'])
        expect(labels({ kind: 'time' })).toEqual(['1 minute', '5 minutes', '15 minutes', '1 hour', '1 day', 'Zoom in', 'Zoom out', 'Fit chart'])
    })

    it('picks a line within 8px, the time axis, or the cursor price', () => {
        const lines = [
            { kind: 'lot' as const, id: 3, y: 100 },
            { kind: 'order' as const, id: 9, y: 140 },
        ]
        expect(hitTest({ y: 106, height: 400, timeAxisHeight: 28, price: 15.25, lines })).toEqual({ kind: 'lot', lotId: 3, price: 15.25 })
        expect(hitTest({ y: 147, height: 400, timeAxisHeight: 28, price: 20, lines })).toEqual({ kind: 'order', orderId: 9, price: 20 })
        expect(hitTest({ y: 120, height: 400, timeAxisHeight: 28, price: 18, lines })).toEqual({ kind: 'price', price: 18 })
        expect(hitTest({ y: 380, height: 400, timeAxisHeight: 28, price: 18, lines })).toEqual({ kind: 'time' })
    })
})
