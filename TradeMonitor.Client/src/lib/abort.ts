/** True when a superseded fetch was cancelled. Callers keep the current screen. */
export function isAbortError(err: unknown): boolean {
    return err instanceof Error && err.name === 'AbortError'
}

export interface AbortGate {
    next(): AbortSignal
    abort(): void
}

/** Abort the previous read, then return the signal for the one that replaces it. */
export function createAbortGate(): AbortGate {
    let current = new AbortController()
    return {
        next() {
            current.abort()
            current = new AbortController()
            return current.signal
        },
        abort() {
            current.abort()
        },
    }
}
