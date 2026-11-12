import { inject, type InjectionKey, type Ref } from 'vue'
import { storeToRefs } from 'pinia'
import type { LiveTick } from '@/api/live'
import type { LiveLink } from '@/lib/sse'
import { useSseStore } from '@/stores/sse'

export interface LiveStream {
    link: Ref<LiveLink>
    subscribe(product: string, onTick: (tick: LiveTick) => void): () => void
}

export const liveStreamKey: InjectionKey<LiveStream> = Symbol('liveStream')

export function useLiveStream(): LiveStream {
    const injected = inject(liveStreamKey, null)
    if (injected) return injected
    const store = useSseStore()
    const { link } = storeToRefs(store)
    return {
        link,
        subscribe(product, onTick) {
            return store.subscribePrice(product, onTick)
        },
    }
}
