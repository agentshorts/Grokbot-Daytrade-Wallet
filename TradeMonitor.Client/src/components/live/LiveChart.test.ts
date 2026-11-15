import { describe, expect, it, vi } from 'vite-plus/test'
import { flushPromises, mount } from '@vue/test-utils'
import LiveChart from './LiveChart.vue'

const chart = vi.hoisted(() => {
    const setData = vi.fn()
    const update = vi.fn()
    const fitContent = vi.fn()
    return {
        setData,
        update,
        fitContent,
        api: {
            addSeries: () => ({
                setData,
                update,
                applyOptions: vi.fn(),
                createPriceLine: vi.fn(),
                removePriceLine: vi.fn(),
                coordinateToPrice: () => 10,
                priceToCoordinate: () => 40,
            }),
            applyOptions: vi.fn(),
            timeScale: () => ({
                fitContent,
                timeToCoordinate: () => 30,
                getVisibleLogicalRange: () => ({ from: 0, to: 10 }),
                setVisibleLogicalRange: vi.fn(),
            }),
            remove: vi.fn(),
            removeSeries: vi.fn(),
        },
    }
})

vi.mock('lightweight-charts', () => ({
    ColorType: { Solid: 'solid' },
    LineStyle: { Solid: 0, Dotted: 1, Dashed: 2 },
    LineType: { Simple: 0, WithSteps: 1 },
    LineSeries: {},
    AreaSeries: {},
    BaselineSeries: {},
    CandlestickSeries: {},
    BarSeries: {},
    HistogramSeries: {},
    createSeriesMarkers: vi.fn(),
    createChart: () => chart.api,
}))

const mark = {
    id: 1,
    time: 1_780_000_200,
    price: 10,
    side: 'buy' as const,
    simulated: false,
    text: 'Buy 2 @ $10, fee $0.25, fill',
}

describe('live chart', () => {
    it('updates the series for a new tick and lists trade markers', async () => {
        const wrapper = mount(LiveChart, {
            props: {
                points: [
                    { time: 1_780_000_200, value: 100 },
                    { time: 1_780_000_500, value: 101 },
                ],
                markers: [mark, { ...mark, id: 2, side: 'sell', simulated: true, text: 'Sell 1 @ $12, fee $0, fill, simulation' }],
                price: 101,
                lots: [],
                orders: [],
                fitKey: 'BTC-USD:5m',
            },
        })
        await flushPromises()
        expect(chart.setData).toHaveBeenCalled()
        expect(chart.fitContent).toHaveBeenCalled()
        const listed = wrapper.get('[data-testid=trade-markers]')
        expect(listed.text()).toContain('Buy 2 @ $10, fee $0.25, fill')
        expect(listed.text()).toContain('Sell 1 @ $12, fee $0, fill, simulation')
        expect(listed.find('[data-simulated=true]').exists()).toBe(true)

        await wrapper.setProps({
            points: [
                { time: 1_780_000_200, value: 100 },
                { time: 1_780_000_500, value: 101 },
                { time: 1_780_000_800, value: 102 },
            ],
        } as never)
        await flushPromises()
        expect(chart.update).toHaveBeenCalledWith({ time: 1_780_000_800, open: 102, high: 102, low: 102, close: 102 })
    })
})
