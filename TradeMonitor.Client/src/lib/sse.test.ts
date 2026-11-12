import { describe, expect, it } from 'vite-plus/test'
import { priceChannel, reconnectDelayMs, sseBaseUrl } from './sse'

describe('server events helpers', () => {
    it('backs off and then stays at thirty seconds', () => {
        expect(reconnectDelayMs(0)).toBe(1000)
        expect(reconnectDelayMs(1)).toBe(2000)
        expect(reconnectDelayMs(2)).toBe(4000)
        expect(reconnectDelayMs(4)).toBe(16000)
        expect(reconnectDelayMs(5)).toBe(30000)
        expect(reconnectDelayMs(10)).toBe(30000)
        expect(priceChannel(' btc-usd ')).toBe('price:BTC-USD')
        expect(sseBaseUrl('', 'http://127.0.0.1:5050')).toBe('http://127.0.0.1:5050')
        expect(sseBaseUrl('https://app.example', 'http://127.0.0.1:5050')).toBe('https://app.example')
    })
})
