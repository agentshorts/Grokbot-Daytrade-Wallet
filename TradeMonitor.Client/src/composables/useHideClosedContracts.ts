import { ref, watch } from 'vue'
import { browserStorage, readHideClosedContracts, writeHideClosedContracts } from '@/lib/predictionContract'

export function useHideClosedContracts() {
    const storage = browserStorage()
    const hide = ref(readHideClosedContracts(storage))
    watch(hide, (value) => {
        writeHideClosedContracts(storage, value)
    })
    return hide
}
