import { createPinia, setActivePinia } from 'pinia'
import { afterEach, describe, expect, it, vi } from 'vite-plus/test'
import type { LiveTick } from '@/api/live'

const bag = vi.hoisted(() => {
    const instances: {
        baseUrl: string
        channels: string[]
        options: {
            handlers: { onConnect: () => void; onException: (error?: unknown) => void }
            receivers: { price: { tick: (body: unknown) => void } }
        }
    }[] = []

    class SseFake {
        channels: string[] = []
        options: (typeof instances)[number]['options']

        constructor(
            public baseUrl: string,
            _channels: string[],
            options: (typeof instances)[number]['options'],
        ) {
            this.options = options
            instances.push(this)
        }

        start() {}

        stop() {
            this.options.handlers.onException(new Error('unregister'))
            return Promise.resolve()
        }

        getSubscriptionId() {
            return 'sub'
        }

        subscribeToChannels(channel: string) {
            this.channels.push(channel)
            return Promise.resolve()
        }

        unsubscribeFromChannels() {
            return Promise.resolve()
        }
    }

    return { instances, SseFake }
})

vi.mock('@servicestack/client', () => ({ ServerEventsClient: bag.SseFake }))
vi.mock('@/lib/gateway', () => ({ client: { baseUrl: '' } }))

import { useSseStore } from './sse'

afterEach(() => {
    vi.useRealTimers()
    bag.instances.length = 0
})

describe('sse store', () => {
    it('delivers ticks and opens a new connection after a drop', async () => {
        vi.useFakeTimers()
        setActivePinia(createPinia())
        const store = useSseStore()
        const ticks: LiveTick[] = []
        store.subscribePrice('btc-usd', (tick) => ticks.push(tick))
        expect(bag.instances).toHaveLength(1)
        expect(bag.instances[0]?.baseUrl).toBe(window.location.origin)

        bag.instances[0]?.options.handlers.onConnect()
        expect(store.link).toBe('live')
        expect(bag.instances[0]?.channels).toContain('price:BTC-USD')
        bag.instances[0]?.options.receivers.price.tick({ productId: 'BTC-USD', price: 100.25, at: '2026-06-15T12:00:00Z' })
        bag.instances[0]?.options.receivers.price.tick({ productId: 'ETH-USD', price: 9, at: '2026-06-15T12:00:00Z' })
        expect(ticks.map((tick) => tick.price)).toEqual([100.25])

        bag.instances[0]?.options.handlers.onException(new Error('drop'))
        expect(store.link).toBe('reconnecting')
        await vi.advanceTimersByTimeAsync(1000)
        expect(bag.instances).toHaveLength(2)
        bag.instances[1]?.options.handlers.onConnect()
        expect(store.link).toBe('live')
        expect(bag.instances[1]?.channels).toContain('price:BTC-USD')
    })
})
