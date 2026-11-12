import { afterEach, describe, expect, it } from 'vite-plus/test'
import { nextTick } from 'vue'
import { mount } from '@vue/test-utils'
import ChartMenu from './ChartMenu.vue'
import { chartMenu, hitTest } from '@/lib/chartMenu'

const spendKey = 'trademonitor.live.spendPercent.paper'

afterEach(() => localStorage.removeItem(spendKey))

function labels(hit: Parameters<typeof chartMenu>[0], pendingMoveId?: number) {
    const wrapper = mount(ChartMenu, { props: { hit, x: 12, y: 24, pendingMoveId } })
    return wrapper.findAll('[role="menuitem"]').map((item) => item.text())
}

describe('chart context menu', () => {
    it('maps the price under the cursor to limit, stop, and take-profit actions', () => {
        expect(labels({ kind: 'price', price: 15.25 })).toEqual([
            'Market buy',
            'Limit buy at $15.25',
            'Stop buy at $15.25',
            'Market sell',
            'Limit sell at $15.25',
            'Stop-loss at $15.25',
            'Take-profit at $15.25',
        ])
        expect(chartMenu({ kind: 'price', price: 4.375 }).map((action) => action.label)).toEqual([
            'Market buy',
            'Limit buy at $4.375',
            'Stop buy at $4.375',
            'Market sell',
            'Limit sell at $4.375',
            'Stop-loss at $4.375',
            'Take-profit at $4.375',
        ])
        expect(chartMenu({ kind: 'price', price: 15 })[1]?.label).toBe('Limit buy at $15')
    })

    it('offers a move when an order is armed, and lot or order actions on those lines', () => {
        expect(labels({ kind: 'price', price: 20 }, 7)[0]).toBe('Move order to $20')
        expect(labels({ kind: 'lot', lotId: 3, price: 15 })).toEqual(['Quick sell this lot'])
        expect(labels({ kind: 'order', orderId: 9, price: 20 })).toEqual(['Cancel order', 'Move order here'])
        expect(labels({ kind: 'time' })).toEqual(['1 minute', '5 minutes', '15 minutes', '1 hour', '1 day', 'Zoom in', 'Zoom out', 'Fit chart'])
        expect(labels({ kind: 'level', price: 72410 })).toEqual(['Limit sell here at $72,410', 'Take-profit here at $72,410', 'Stop here at $72,410'])
        const extra = mount(ChartMenu, {
            props: { hit: { kind: 'price', price: 100 }, x: 0, y: 0, extra: [{ id: 'atr-take', label: 'Take-profit at +1 ATR', tone: 'buy' }] },
        })
        expect(extra.get('[data-testid=menu-atr-take]').text()).toBe('Take-profit at +1 ATR')
    })

    it('keeps the spend slider and the amount box on the same value', async () => {
        localStorage.removeItem(spendKey)
        const props = {
            hit: { kind: 'price' as const, price: 50000 },
            x: 0,
            y: 0,
            cash: 1000,
            heldQty: 0.005,
            marketPrice: 50000,
            walletKey: 'paper',
            baseIncrement: 0.00000001,
            quoteIncrement: 0.01,
            baseMinSize: 0.00000001,
            minMarketFunds: 1,
        }
        const wrapper = mount(ChartMenu, { props })
        await wrapper.get('[data-testid=spend-slider]').setValue('250')
        expect((wrapper.get('[data-testid=spend-input]').element as HTMLInputElement).value).toBe('250.00')
        expect(wrapper.get('[data-testid=buy-preview]').text()).toContain('0.005')
        await wrapper.get('[data-testid=spend-input]').setValue('100')
        expect(Number((wrapper.get('[data-testid=spend-slider]').element as HTMLInputElement).value)).toBe(100)
        await wrapper.get('[data-testid=spend-input]').setValue('5000')
        expect(wrapper.get('[data-testid=spend-hint]').text()).toContain('Capped')
        expect((wrapper.get('[data-testid=spend-input]').element as HTMLInputElement).value).toBe('1000.00')
        await wrapper.get('[data-testid=chip-25]').trigger('click')
        expect((wrapper.get('[data-testid=spend-input]').element as HTMLInputElement).value).toBe('250.00')
        await wrapper.get('[data-testid=chart-menu]').trigger('keydown', { key: 'ArrowUp' })
        expect((wrapper.get('[data-testid=spend-input]').element as HTMLInputElement).value).toBe('260.00')
        await wrapper.get('[data-testid=sell-qty]').trigger('click')
        await wrapper.get('[data-testid=chip-50]').trigger('click')
        expect(wrapper.get('[data-testid=sell-preview]').text()).toContain('0.0025')
        await wrapper.get('[data-testid=sell-input]').trigger('focus')
        await wrapper.get('[data-testid=chart-menu]').trigger('keydown', { key: 'Enter' })
        const sent = wrapper.emitted('action')
        expect(sent?.[sent.length - 1]?.[0]).toEqual({ id: 'market-sell', qty: 0.0025 })
        expect(localStorage.getItem(spendKey)).toBe('50')
        wrapper.unmount()
        const again = mount(ChartMenu, { props })
        await nextTick()
        expect((again.get('[data-testid=spend-input]').element as HTMLInputElement).value).toBe('500.00')
        await again.get('[data-testid=chart-menu]').trigger('keydown', { key: 'Escape' })
        expect(again.emitted('close')).toHaveLength(1)
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
        expect(hitTest({ y: 20, height: 400, timeAxisHeight: 28, price: 18, lines: [{ kind: 'level', id: 0, y: 22, price: 110 }] })).toEqual({ kind: 'level', price: 110 })
    })
})
