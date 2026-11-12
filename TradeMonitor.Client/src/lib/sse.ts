export type LiveLink = 'off' | 'live' | 'reconnecting'

// 1s, 2s, 4s, 8s, 16s, then 30s.
export function reconnectDelayMs(attempt: number): number {
    const step = Math.max(0, Math.floor(attempt))
    return Math.min(30_000, 1000 * 2 ** Math.min(step, 5))
}

export function priceChannel(product: string): string {
    return `price:${product.trim().toUpperCase()}`
}

export function sseBaseUrl(baseUrl: string, origin: string): string {
    return baseUrl || origin
}
