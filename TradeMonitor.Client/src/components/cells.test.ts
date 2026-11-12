import { describe, expect, it } from 'vite-plus/test'
import { mount } from '@vue/test-utils'
import MoneyCell from './MoneyCell.vue'
import PctCell from './PctCell.vue'
import TimeCell from './TimeCell.vue'
import KpiCard from './KpiCard.vue'
import WalletCard from './WalletCard.vue'
import EChart from './EChart.vue'
import PriceChart from './PriceChart.vue'
import type { Kpi } from '@/lib/book'

describe('display cells', () => {
    it('colors signed money and percent values', () => {
        const loss = mount(MoneyCell, { props: { value: -12.5 } })
        expect(loss.text()).toBe('-$12.50')
        expect(loss.classes().join(' ')).toContain('text-error-600')

        const gain = mount(PctCell, { props: { value: 4.25 } })
        expect(gain.text()).toBe('+4.25%')
        expect(gain.classes().join(' ')).toContain('text-success-600')

        const when = mount(TimeCell, { props: { value: '2026-01-15T18:00:00.000Z' } })
        expect(when.text()).toContain('CT')
        expect(when.text()).toContain('12:00 PM')
    })

    it('shows empty, loading and error states', () => {
        const kpi: Kpi = { id: 'value', label: 'Total value', display: 'money', value: null, signed: false, emptyLabel: 'No price mark' }
        expect(mount(KpiCard, { props: { kpi } }).text()).toContain('No price mark')
        expect(mount(KpiCard, { props: { kpi, loading: true } }).text()).toContain('Loading Total value')
        expect(mount(KpiCard, { props: { kpi, error: 'Unavailable' } }).text()).toContain('Unavailable')
        expect(mount(WalletCard, { props: { empty: true } }).text()).toContain('No wallets yet')
        expect(mount(EChart, { props: { empty: true, emptyLabel: 'Nothing recorded in this range' } }).text()).toContain('Nothing recorded in this range')
        expect(mount(PriceChart, { props: { points: [], empty: true } }).text()).toContain('No price marks recorded')
        expect(mount(PriceChart, { props: { points: [], loading: true } }).text()).toContain('Loading price chart')
        expect(mount(EChart, { props: { error: 'Chart failed' } }).text()).toContain('Chart failed')
    })
})
