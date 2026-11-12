import { inject, type InjectionKey } from 'vue'
import { live, type LiveClient } from '@/api/live'

export const liveClientKey: InjectionKey<LiveClient> = Symbol('liveClient')

export function useLive(): LiveClient {
    return inject(liveClientKey, live)
}
