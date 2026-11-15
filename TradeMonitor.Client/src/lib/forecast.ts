import { asRecord, pick, readMoney } from '@/api/parse'
import { horizonBars, type AtrHorizon } from '@/lib/atrBands'
import { formatMenuPrice } from '@/lib/chartMenu'

export type ForecastHorizon = '1h' | '4h' | '1d'

export const forecastHorizons: { id: ForecastHorizon; label: string }[] = [
    { id: '1h', label: '1 hour' },
    { id: '4h', label: '4 hours' },
    { id: '1d', label: '1 day' },
]

export interface ForecastStep {
    time: number
    mean: number
    q10: number
    q30: number
    q50: number
    q70: number
    q90: number
}

export interface ForecastSnapshot {
    model: string
    notice: string
    coverage: string
    generatedAt: number
    steps: ForecastStep[]
}

export function isForecastHorizon(value: string): value is ForecastHorizon {
    return forecastHorizons.some((item) => item.id === value)
}

export function forecastHorizonBars(horizon: ForecastHorizon, intervalSeconds: number): number {
    return Math.min(128, Math.max(1, horizonBars(horizon as AtrHorizon, intervalSeconds)))
}

export function forecastLabel(name: string, level: number, price: number): string {
    const pct = price === 0 ? 0 : ((level - price) / price) * 100
    const sign = pct > 0 ? '+' : pct < 0 ? '-' : ''
    const percent = Math.abs(pct).toLocaleString('en-US', { maximumFractionDigits: 1 })
    return `${name} ${formatMenuPrice(level)} ${sign}${percent}%`
}

export function parseForecast(body: unknown): ForecastSnapshot {
    const record = asRecord(body) ?? {}
    const steps = Array.isArray(pick(record, 'steps')) ? (pick(record, 'steps') as unknown[]) : []
    return {
        model: typeof pick(record, 'model') === 'string' ? String(pick(record, 'model')) : 'TimesFM 2.5',
        notice: typeof pick(record, 'notice') === 'string' ? String(pick(record, 'notice')) : '',
        coverage: typeof pick(record, 'coverage') === 'string' ? String(pick(record, 'coverage')) : '',
        generatedAt: readMoney(pick(record, 'generatedAt')) ?? 0,
        steps: steps.flatMap((item) => {
            const row = asRecord(item)
            if (!row) return []
            const time = readMoney(pick(row, 'at'))
            const mean = readMoney(pick(row, 'mean'))
            const q10 = readMoney(pick(row, 'q10'))
            const q90 = readMoney(pick(row, 'q90'))
            if (time == null || mean == null || q10 == null || q90 == null) return []
            return [
                {
                    time,
                    mean,
                    q10,
                    q30: readMoney(pick(row, 'q30')) ?? mean,
                    q50: readMoney(pick(row, 'q50')) ?? mean,
                    q70: readMoney(pick(row, 'q70')) ?? mean,
                    q90,
                },
            ]
        }),
    }
}
