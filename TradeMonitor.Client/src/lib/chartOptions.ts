import type { EquitySeries, FeeBar, SymbolPnl } from '@/lib/book'
import { formatMoney, UNKNOWN_AMOUNT } from '@/lib/format'

const UP = '#12b76a'
const DOWN = '#f04438'

function palette(dark: boolean) {
    return {
        text: dark ? '#98a2b3' : '#667085',
        line: dark ? '#344054' : '#e4e7ec',
        split: dark ? '#1d2939' : '#f2f4f7',
        tooltipBg: dark ? '#1d2939' : '#ffffff',
        tooltipText: dark ? '#f2f4f7' : '#101828',
        border: dark ? '#344054' : '#e4e7ec',
    }
}

function compactMoney(value: number): string {
    const abs = Math.abs(value)
    const sign = value < 0 ? '-' : ''
    if (abs >= 1000) {
        const scaled = abs / 1000
        const text = scaled >= 10 ? scaled.toFixed(0) : scaled.toFixed(1)
        return `${sign}$${text}k`
    }
    return formatMoney(value, { signed: false })
}

function formatAxisMoney(value: unknown, signed: boolean): string {
    if (value == null || value === '') return UNKNOWN_AMOUNT
    const amount = typeof value === 'number' ? value : Number(value)
    if (!Number.isFinite(amount)) return UNKNOWN_AMOUNT
    return formatMoney(amount, { signed })
}

function tooltip(dark: boolean, signed: boolean) {
    const colors = palette(dark)
    return {
        trigger: 'axis' as const,
        confine: true,
        backgroundColor: colors.tooltipBg,
        borderColor: colors.border,
        textStyle: { color: colors.tooltipText },
        valueFormatter: (value: unknown) => formatAxisMoney(value, signed),
    }
}

function barColor(value: unknown): string {
    if (value == null || value === '') return 'transparent'
    const amount = typeof value === 'number' ? value : Number(value)
    if (!Number.isFinite(amount)) return 'transparent'
    return amount >= 0 ? UP : DOWN
}

export function equityOption(series: EquitySeries[], dark: boolean): Record<string, unknown> {
    const colors = palette(dark)
    const labels = series[0]?.points.map((point) => point.label) ?? []
    return {
        animationDuration: 500,
        backgroundColor: 'transparent',
        darkMode: dark,
        aria: { enabled: true },
        color: series.map((item) => item.color),
        tooltip: tooltip(dark, false),
        legend: { top: 0, type: 'scroll', textStyle: { color: colors.text } },
        grid: { left: 8, right: 12, top: 36, bottom: 8, containLabel: true },
        dataZoom: [{ type: 'inside', throttle: 50 }],
        xAxis: {
            type: 'category',
            data: labels,
            boundaryGap: false,
            axisLabel: { color: colors.text, fontSize: 11, hideOverlap: true },
            axisLine: { lineStyle: { color: colors.line } },
        },
        yAxis: {
            type: 'value',
            scale: true,
            axisLabel: { color: colors.text, fontSize: 11, formatter: (value: number) => compactMoney(value) },
            splitLine: { lineStyle: { color: colors.split } },
        },
        series: series.map((item) => ({
            name: item.name,
            type: 'line',
            stack: 'equity',
            smooth: true,
            showSymbol: false,
            data: item.points.map((point) => point.value),
            lineStyle: { width: 2 },
            areaStyle: { opacity: 0.28 },
        })),
    }
}

export function pnlBarsOption(bars: { label: string; pnl: number | null }[], dark: boolean): Record<string, unknown> {
    const colors = palette(dark)
    return {
        animationDuration: 500,
        backgroundColor: 'transparent',
        darkMode: dark,
        aria: { enabled: true },
        tooltip: tooltip(dark, true),
        grid: { left: 8, right: 12, top: 16, bottom: 8, containLabel: true },
        xAxis: {
            type: 'category',
            data: bars.map((bar) => bar.label),
            axisLabel: { color: colors.text, fontSize: 11, hideOverlap: true },
            axisLine: { lineStyle: { color: colors.line } },
        },
        yAxis: {
            type: 'value',
            axisLabel: { color: colors.text, fontSize: 11, formatter: (value: number) => compactMoney(value) },
            splitLine: { lineStyle: { color: colors.split } },
        },
        series: [
            {
                name: 'P&L',
                type: 'bar',
                data: bars.map((bar) => bar.pnl),
                barMaxWidth: 28,
                itemStyle: {
                    borderRadius: 3,
                    color: (params: { value?: unknown }) => barColor(params.value),
                },
            },
        ],
    }
}

export function symbolPnlOption(rows: SymbolPnl[], dark: boolean): Record<string, unknown> {
    const colors = palette(dark)
    const ordered = rows.slice().reverse()
    return {
        animationDuration: 500,
        backgroundColor: 'transparent',
        darkMode: dark,
        aria: { enabled: true },
        tooltip: tooltip(dark, true),
        grid: { left: 8, right: 16, top: 8, bottom: 8, containLabel: true },
        xAxis: {
            type: 'value',
            axisLabel: { color: colors.text, fontSize: 11, formatter: (value: number) => compactMoney(value) },
            splitLine: { lineStyle: { color: colors.split } },
        },
        yAxis: {
            type: 'category',
            data: ordered.map((row) => row.symbol),
            axisLabel: { color: colors.text, fontSize: 11 },
            axisLine: { lineStyle: { color: colors.line } },
        },
        series: [
            {
                name: 'P&L',
                type: 'bar',
                data: ordered.map((row) => row.pnl),
                barMaxWidth: 18,
                itemStyle: {
                    borderRadius: 3,
                    color: (params: { value?: unknown }) => barColor(params.value),
                },
            },
        ],
    }
}

export function feesOption(rows: FeeBar[], dark: boolean): Record<string, unknown> {
    const colors = palette(dark)
    return {
        animationDuration: 500,
        backgroundColor: 'transparent',
        darkMode: dark,
        aria: { enabled: true },
        tooltip: tooltip(dark, false),
        grid: { left: 8, right: 12, top: 16, bottom: 8, containLabel: true },
        xAxis: {
            type: 'category',
            data: rows.map((row) => row.label),
            axisLabel: { color: colors.text, fontSize: 11, hideOverlap: true },
            axisLine: { lineStyle: { color: colors.line } },
        },
        yAxis: {
            type: 'value',
            axisLabel: { color: colors.text, fontSize: 11, formatter: (value: number) => compactMoney(value) },
            splitLine: { lineStyle: { color: colors.split } },
        },
        series: [
            {
                name: 'Fees',
                type: 'bar',
                data: rows.map((row) => row.amount),
                barMaxWidth: 28,
                itemStyle: { borderRadius: 3, color: '#465fff' },
            },
        ],
    }
}
