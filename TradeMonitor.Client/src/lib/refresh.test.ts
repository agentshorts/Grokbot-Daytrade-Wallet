import { describe, expect, it, vi, afterEach } from 'vite-plus/test'
import { REFRESH_MS, startPolling } from './refresh'

afterEach(() => {
    vi.useRealTimers()
})

describe('startPolling', () => {
    it('runs on the refresh interval and skips a hidden document', () => {
        vi.useFakeTimers()
        const calls: number[] = []
        const stop = startPolling(() => calls.push(1), REFRESH_MS)
        expect(calls).toEqual([])
        vi.advanceTimersByTime(REFRESH_MS)
        expect(calls).toEqual([1])
        vi.advanceTimersByTime(REFRESH_MS)
        expect(calls).toEqual([1, 1])
        Object.defineProperty(document, 'hidden', { configurable: true, get: () => true })
        vi.advanceTimersByTime(REFRESH_MS)
        expect(calls).toEqual([1, 1])
        stop()
        Object.defineProperty(document, 'hidden', { configurable: true, get: () => false })
    })
})
