import { ref, watch } from 'vue'
import { readSectionHidden, readViewFilter, writeSectionHidden, writeViewFilter } from '@/lib/uiState'
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
