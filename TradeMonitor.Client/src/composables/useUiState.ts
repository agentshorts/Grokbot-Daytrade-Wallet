import { computed, ref, watch, type WritableComputedRef } from 'vue'
import { readSectionHidden, readShowArchived, readViewFilter, writeSectionHidden, writeShowArchived, writeViewFilter } from '@/lib/uiState'
import { browserStorage } from '@/lib/predictionContract'

export function useSectionCollapsed(id: string) {
    const storage = browserStorage()
    const collapsed = ref(readSectionHidden(storage, id))
    watch(collapsed, (value) => {
        writeSectionHidden(storage, id, value)
    })
    return collapsed
}

export function useViewFilter(view: string) {
    const storage = browserStorage()
    const text = ref(readViewFilter(storage, view))
    watch(text, (value) => {
        writeViewFilter(storage, view, value)
    })
    return text
}

let showArchived: WritableComputedRef<boolean> | undefined

/** One shared flag so the header and every wallet list follow the same choice. */
export function useShowArchived() {
    if (!showArchived) {
        const stored = ref(readShowArchived(browserStorage()))
        showArchived = computed({
            get: () => stored.value,
            set: (value: boolean) => {
                stored.value = value
                writeShowArchived(browserStorage(), value)
            },
        })
    }
    return showArchived
}
