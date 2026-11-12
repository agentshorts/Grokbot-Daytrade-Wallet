import { rangeStart } from '@/lib/book'
import type { DateRangeKey } from '@/lib/filters'

/** Inclusive UTC bounds for a shell date range. `all` omits both ends. */
export function rangeBounds(range: DateRangeKey, now: Date): { from?: string; to?: string } {
    const start = rangeStart(range, now)
    if (!start) return {}
    return { from: start.toISOString(), to: now.toISOString() }
}
