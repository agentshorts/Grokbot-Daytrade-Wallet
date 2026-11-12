export const REFRESH_MS = 15_000

/** Call `run` on a timer. The caller does the first load itself. */
export function startPolling(run: () => void, intervalMs = REFRESH_MS): () => void {
    const tick = () => {
        if (typeof document !== 'undefined' && document.hidden) return
        run()
    }
    const id = setInterval(tick, intervalMs)
    return () => clearInterval(id)
}
