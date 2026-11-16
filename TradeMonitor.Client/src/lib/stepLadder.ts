import { asRecord, pick, readMoney } from '@/api/parse'

export interface StepLadderInputs {
    stepIncrease: number
    dormantThreshold: number
    dormantPeriod: number
}

export interface StepLadderBar {
    time: number
    volume: number
    vol24: number | null
    step: boolean
    dormant: boolean
}

export interface StepLadderSnapshot {
    bars: StepLadderBar[]
}

export const stepLadderDefaults: StepLadderInputs = {
    stepIncrease: 2,
    dormantThreshold: 0.5,
    dormantPeriod: 5,
}

export function readStepLadderInputs(
    stepIncrease: unknown,
    dormantThreshold: unknown,
    dormantPeriod: unknown,
): { ok: true; value: StepLadderInputs } | { ok: false; error: string } {
    const step = number(stepIncrease)
    const threshold = number(dormantThreshold)
    const period = number(dormantPeriod)
    if (!(step > 0)) return { ok: false, error: 'Step increase must be greater than 0.' }
    if (!(threshold > 0)) return { ok: false, error: 'Dormant threshold must be greater than 0.' }
    if (!Number.isInteger(period) || period < 1) return { ok: false, error: 'Dormant period must be a whole number of at least 1.' }
    return { ok: true, value: { stepIncrease: step, dormantThreshold: threshold, dormantPeriod: period } }
}

export function keepStepIncrease(value: unknown): number {
    const parsed = readStepLadderInputs(value, stepLadderDefaults.dormantThreshold, stepLadderDefaults.dormantPeriod)
    return parsed.ok ? parsed.value.stepIncrease : stepLadderDefaults.stepIncrease
}

export function keepDormantThreshold(value: unknown): number {
    const parsed = readStepLadderInputs(stepLadderDefaults.stepIncrease, value, stepLadderDefaults.dormantPeriod)
    return parsed.ok ? parsed.value.dormantThreshold : stepLadderDefaults.dormantThreshold
}

export function keepDormantPeriod(value: unknown): number {
    const parsed = readStepLadderInputs(stepLadderDefaults.stepIncrease, stepLadderDefaults.dormantThreshold, value)
    return parsed.ok ? parsed.value.dormantPeriod : stepLadderDefaults.dormantPeriod
}

export function parseStepLadder(body: unknown): StepLadderSnapshot {
    const record = asRecord(body) ?? {}
    const rows = pick(record, 'bars')
    if (!Array.isArray(rows)) return { bars: [] }
    return {
        bars: rows.flatMap((item) => {
            const row = asRecord(item)
            if (!row) return []
            const time = readMoney(pick(row, 'time'))
            const volume = readMoney(pick(row, 'volume'))
            if (time == null || time <= 0 || volume == null) return []
            return [
                {
                    time,
                    volume,
                    vol24: readMoney(pick(row, 'vol24')),
                    step: pick(row, 'step') === true,
                    dormant: pick(row, 'dormant') === true,
                },
            ]
        }),
    }
}

function number(value: unknown): number {
    if (typeof value === 'number') return value
    if (typeof value === 'string' && value.trim()) return Number(value)
    return Number.NaN
}
