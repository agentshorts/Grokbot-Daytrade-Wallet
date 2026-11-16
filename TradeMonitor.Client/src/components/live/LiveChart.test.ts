import { describe, expect, it, vi } from 'vite-plus/test'
import { flushPromises, mount } from '@vue/test-utils'
import LiveChart from './LiveChart.vue'

const chart = vi.hoisted(() => {
    const setData = vi.fn()
    const update = vi.fn()
    const fitContent = vi.fn()
    const setMarkers = vi.fn()
    const setVisibleLogicalRange = vi.fn()
    const setVisibleRange = vi.fn()
    const addSeries = vi.fn((_definition: unknown, _options: unknown, _pane?: number) => ({
        setData,
        update,
        applyOptions: vi.fn(),
        createPriceLine: vi.fn(),
        removePriceLine: vi.fn(),
        coordinateToPrice: () => 10,
        priceToCoordinate: () => 40,
    }))
    const timeScale = {
        fitContent,
        timeToCoordinate: () => 30,
        getVisibleLogicalRange: () => ({ from: 3, to: 9 }),
        setVisibleLogicalRange,
        getVisibleRange: () => ({ from: 100, to: 400 }),
        setVisibleRange,
    }
    return {
        setData,
        update,
        fitContent,
        setMarkers,
        setVisibleLogicalRange,
        addSeries,
        api: {
            addSeries,
            applyOptions: vi.fn(),
            timeScale: () => timeScale,
            remove: vi.fn(),
            removeSeries: vi.fn(),
            panes: () => [{}, { setStretchFactor: vi.fn() }],
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
    createSeriesMarkers: () => ({ setMarkers: chart.setMarkers }),
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

    it('draws step ladder triangles and a blue volume line with an orange average', async () => {
        chart.addSeries.mockClear()
        chart.setMarkers.mockClear()
        const wrapper = mount(LiveChart, {
            props: {
                points: [
                    { time: 100, value: 10 },
                    { time: 160, value: 11 },
                ],
                markers: [],
                price: 11,
                lots: [],
                orders: [],
                fitKey: 'BTC-USD:step',
                showStudy: true,
                study: [
                    { time: 100, volume: 4, vol24: null, step: false, dormant: false },
                    { time: 160, volume: 30, vol24: 10, step: true, dormant: false },
                    { time: 220, volume: 1, vol24: 8, step: false, dormant: true },
                ],
            },
        })
        await flushPromises()
        const colors = chart.addSeries.mock.calls.map((call) => (call[1] as { color?: string } | undefined)?.color)
        expect(colors).toContain('#2e90fa')
        expect(colors).toContain('#f79009')
        expect(chart.addSeries.mock.calls.find((call) => (call[1] as { color?: string } | undefined)?.color === '#2e90fa')?.[2]).toBe(1)
        expect(chart.setData).toHaveBeenCalledWith([
            { time: 100, value: 4 },
            { time: 160, value: 30 },
            { time: 220, value: 1 },
        ])
        expect(chart.setData).toHaveBeenCalledWith([
            { time: 160, value: 10 },
            { time: 220, value: 8 },
        ])
        expect(chart.setMarkers).toHaveBeenCalledWith([
            { time: 160, position: 'aboveBar', color: '#12b76a', shape: 'arrowUp', size: 2 },
            { time: 220, position: 'belowBar', color: '#f04438', shape: 'arrowDown', size: 2 },
        ])
        const listed = wrapper.get('[data-testid=step-ladder-markers]')
        expect(listed.text()).toContain('step 160')
        expect(listed.text()).toContain('dormant 220')
        expect(listed.text()).not.toContain('step 100')
    })

    it('keeps the visible range when the graph type changes and refits for a new window', async () => {
        chart.fitContent.mockClear()
        chart.setVisibleLogicalRange.mockClear()
        const wrapper = mount(LiveChart, {
            props: {
                points: [
                    { time: 1_780_000_200, value: 100 },
                    { time: 1_780_000_500, value: 101 },
                ],
                markers: [],
                price: 101,
                lots: [],
                orders: [],
                fitKey: 'BTC-USD:5m:1D::',
                chartType: 'candle',
            },
        })
        await flushPromises()
        expect(chart.fitContent).toHaveBeenCalledTimes(1)
        expect(chart.setVisibleLogicalRange).not.toHaveBeenCalled()

        chart.fitContent.mockClear()
        await wrapper.setProps({ chartType: 'line' } as never)
        await flushPromises()
        expect(chart.fitContent).not.toHaveBeenCalled()
        expect(chart.setVisibleLogicalRange).toHaveBeenCalledWith({ from: 3, to: 9 })

        await wrapper.setProps({ chartType: 'candle' } as never)
        await flushPromises()
        expect(chart.fitContent).not.toHaveBeenCalled()
        expect(chart.setVisibleLogicalRange).toHaveBeenLastCalledWith({ from: 3, to: 9 })

        chart.setVisibleLogicalRange.mockClear()
        await wrapper.setProps({ fitKey: 'ETH-USD:5m:1D::' } as never)
        await flushPromises()
        expect(chart.fitContent).toHaveBeenCalledTimes(1)
        expect(chart.setVisibleLogicalRange).not.toHaveBeenCalled()
    })
})
