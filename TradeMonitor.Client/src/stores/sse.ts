import { ServerEventsClient } from '@servicestack/client'
import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { LiveTick } from '@/api/live'
import { client } from '@/lib/gateway'
import { parseLiveTick } from '@/lib/liveSeries'
import { priceChannel, reconnectDelayMs, sseBaseUrl, type LiveLink } from '@/lib/sse'

type TickListener = (tick: LiveTick) => void

// One Server Events connection for the live chart. Channels are reference counted
// so two callers can share price:BTC-USD and the last one to leave unsubscribes.
export const useSseStore = defineStore('sse', () => {
    const link = ref<LiveLink>('off')
    const serverEvents = ref<ServerEventsClient | null>(null)
    const listeners = new Set<TickListener>()
    const channelRefCounts = new Map<string, number>()
    const pendingChannels = new Set<string>()
    let attempt = 0
    let timer = 0
    let replacing = false
    let ignoreStop = false

    function onTick(body: unknown) {
        const tick = parseLiveTick(body)
        if (!tick) return
        for (const listener of listeners) listener(tick)
    }

    function flushPending() {
        const sse = serverEvents.value
        if (link.value !== 'live' || !sse?.getSubscriptionId()) return
        for (const channel of pendingChannels) {
            void sse.subscribeToChannels(channel).then(
                () => {
                    if (serverEvents.value === sse) pendingChannels.delete(channel)
                },
                () => pendingChannels.add(channel),
            )
        }
    }

    function start() {
        if (serverEvents.value) return
        const origin = typeof window !== 'undefined' ? window.location.origin : ''
        const sse = new ServerEventsClient(sseBaseUrl(client.baseUrl, origin), ['live'], {
            handlers: {
                onConnect: () => {
                    link.value = 'live'
                    attempt = 0
                    flushPending()
                },
                onException: () => fail(),
            },
            receivers: {
                price: {
                    tick: onTick,
                },
            },
        })
        serverEvents.value = sse
        sse.start()
    }

    function fail() {
        if (replacing || ignoreStop) return
        replacing = true
        link.value = 'reconnecting'
        const current = serverEvents.value
        serverEvents.value = null
        for (const [channel, count] of channelRefCounts) {
            if (count > 0) pendingChannels.add(channel)
        }
        ignoreStop = true
        try {
            void current?.stop()
        } catch {
            // stop() reports the same drop. ignoreStop skips that re-entry.
        }
        queueMicrotask(() => {
            ignoreStop = false
        })
        replacing = false
        window.clearTimeout(timer)
        const wait = reconnectDelayMs(attempt)
        attempt += 1
        timer = window.setTimeout(() => start(), wait)
    }

    function stop() {
        window.clearTimeout(timer)
        replacing = true
        const current = serverEvents.value
        serverEvents.value = null
        try {
            void current?.stop()
        } catch {
            // The page is leaving. A failed unregister can be ignored.
        }
        replacing = false
        link.value = 'off'
        attempt = 0
    }

    function retain(channel: string) {
        const count = channelRefCounts.get(channel) ?? 0
        channelRefCounts.set(channel, count + 1)
        if (count > 0) return
        const sse = serverEvents.value
        if (link.value === 'live' && sse?.getSubscriptionId()) {
            void sse.subscribeToChannels(channel).catch(() => pendingChannels.add(channel))
            return
        }
        pendingChannels.add(channel)
    }

    function release(channel: string) {
        const count = channelRefCounts.get(channel) ?? 0
        if (count <= 1) {
            channelRefCounts.delete(channel)
            pendingChannels.delete(channel)
            const sse = serverEvents.value
            if (sse?.getSubscriptionId()) void sse.unsubscribeFromChannels(channel).catch(() => undefined)
            return
        }
        channelRefCounts.set(channel, count - 1)
    }

    function subscribePrice(product: string, onPrice: TickListener) {
        start()
        const channel = priceChannel(product)
        const wanted = product.trim().toUpperCase()
        const listener: TickListener = (tick) => {
            if (tick.productId && tick.productId.toUpperCase() !== wanted) return
            onPrice(tick)
        }
        listeners.add(listener)
        retain(channel)
        return () => {
            listeners.delete(listener)
            release(channel)
        }
    }

    return { link, start, stop, subscribePrice }
})
