import { describe, expect, it } from 'vite-plus/test'
import { UNKNOWN_AMOUNT } from '@/lib/format'
import { equityOption, pnlBarsOption } from './chartOptions'
import type { EquitySeries } from './book'
import mainTs from '../main.ts?raw'
import appVue from '../App.vue?raw'
import layoutVue from '../layouts/default.vue?raw'
import overviewVue from '../pages/index.vue?raw'
import tradesVue from '../pages/trades.vue?raw'
import walletsVue from '../pages/wallets.vue?raw'
import chartOptionsTs from './chartOptions.ts?raw'
import echartVue from '../components/EChart.vue?raw'
import priceChartVue from '../components/PriceChart.vue?raw'
import tradeDrawerVue from '../components/TradeDrawer.vue?raw'
import loadPriceChartTs from './loadPriceChart.ts?raw'

describe('chart options', () => {
    it('stacks one equity series per wallet and colors P&L bars by sign', () => {
        const series: EquitySeries[] = [
            { key: 'a', name: 'Coinbase', color: '#465fff', points: [{ at: 'a', label: 'Nov 1', value: 10 }] },
            { key: 'b', name: 'Paper', color: '#7a5af8', points: [{ at: 'a', label: 'Nov 1', value: 5 }] },
        ]
        const equity = equityOption(series, false)
        const drawn = equity.series as { stack: string; name: string }[]
        expect(drawn).toHaveLength(2)
        expect(drawn.every((item) => item.stack === 'equity')).toBe(true)

        const bars = pnlBarsOption(
            [
                { label: 'Nov 1', pnl: 12 },
                { label: 'Nov 2', pnl: -4 },
            ],
            true,
        )
        const barSeries = (bars.series as { data: (number | null)[]; itemStyle: { color: (params: { value?: unknown }) => string } }[])[0]
        expect(barSeries?.itemStyle.color({ value: 12 })).toBe('#12b76a')
        expect(barSeries?.itemStyle.color({ value: -4 })).toBe('#f04438')

        const blank = pnlBarsOption(
            [
                { label: 'Nov 1', pnl: null },
                { label: 'Nov 2', pnl: 0 },
            ],
            false,
        )
        const blankSeries = (blank.series as { data: (number | null)[] }[])[0]
        expect(blankSeries?.data).toEqual([null, 0])
        const tooltip = blank.tooltip as { valueFormatter: (value: unknown) => string }
        expect(tooltip.valueFormatter(null)).toBe(UNKNOWN_AMOUNT)
        expect(tooltip.valueFormatter(0)).toBe('$0.00')
    })
})

describe('lazy charts', () => {
    it('loads chart libraries only from the chart wrappers', () => {
        const forbidden = [/from\s+['"]echarts/, /from\s+['"]vue-echarts['"]/, /from\s+['"]lightweight-charts['"]/]
        const safe = [mainTs, appVue, layoutVue, overviewVue, tradesVue, walletsVue, chartOptionsTs]
        for (const text of safe) {
            for (const pattern of forbidden) expect(text).not.toMatch(pattern)
        }
        expect(echartVue).toMatch(/from 'vue-echarts'/)
        expect(echartVue).toMatch(/from 'echarts\/charts'/)
        expect(priceChartVue).toMatch(/from 'lightweight-charts'/)
        expect(overviewVue).toContain("import('@/components/EChart.vue')")
        expect(loadPriceChartTs).toContain("import('@/components/PriceChart.vue')")
        expect(tradeDrawerVue).toContain('loadPriceChart')
        expect(tradesVue).not.toContain('lightweight-charts')
        expect(overviewVue).not.toContain('lightweight-charts')
        expect(walletsVue).not.toContain('EChart')
        expect(walletsVue).not.toContain('PriceChart')
    })
})
