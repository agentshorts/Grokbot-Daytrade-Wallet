import { describe, expect, it } from 'vite-plus/test'
import { formatMoney, formatPct, formatTimeCt, startOfChicagoDay } from './format'

describe('formatMoney', () => {
    it('signs positive and negative currency amounts', () => {
        expect(formatMoney(1234.5)).toBe('+$1,234.50')
        expect(formatMoney(-1234.5)).toBe('-$1,234.50')
        expect(formatMoney(-0.1)).toBe('-$0.10')
        expect(formatMoney(0)).toBe('$0.00')
        expect(formatMoney(null)).toBe('')
        expect(formatMoney(10, { signed: false })).toBe('$10.00')
    })
})

describe('formatPct', () => {
    it('signs percents and keeps two decimals', () => {
        expect(formatPct(3)).toBe('+3.00%')
        expect(formatPct(-12.5)).toBe('-12.50%')
        expect(formatPct(0)).toBe('0.00%')
        expect(formatPct(null)).toBe('')
        expect(formatPct(62.5, { signed: false })).toBe('62.50%')
    })
})

describe('formatTimeCt', () => {
    it('labels Chicago time as CT and follows the daylight-saving offset', () => {
        expect(formatTimeCt('2026-01-15T18:00:00.000Z')).toBe('Jan 15, 2026, 12:00 PM CT')
        expect(formatTimeCt('2026-07-15T18:00:00.000Z')).toBe('Jul 15, 2026, 1:00 PM CT')
        // Spring forward 2026 is March 8. One UTC hour crosses the missing local hour.
        expect(formatTimeCt('2026-03-08T07:30:00.000Z')).toBe('Mar 8, 2026, 1:30 AM CT')
        expect(formatTimeCt('2026-03-08T08:30:00.000Z')).toBe('Mar 8, 2026, 3:30 AM CT')
        expect(formatTimeCt('not-a-date')).toBe('')
    })
})

describe('startOfChicagoDay', () => {
    it('uses CST in January and CDT in July', () => {
        expect(startOfChicagoDay(new Date('2026-01-15T18:00:00.000Z')).toISOString()).toBe('2026-01-15T06:00:00.000Z')
        expect(startOfChicagoDay(new Date('2026-07-15T18:00:00.000Z')).toISOString()).toBe('2026-07-15T05:00:00.000Z')
    })
})
