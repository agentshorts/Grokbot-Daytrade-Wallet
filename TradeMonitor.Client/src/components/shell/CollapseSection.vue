<script setup lang="ts">
import { useSectionCollapsed } from '@/composables/useUiState'

const props = defineProps<{ id: string; label: string }>()
const collapsed = useSectionCollapsed(props.id)
</script>

<template>
    <section>
        <div class="mb-3 flex items-start justify-between gap-3">
            <div class="min-w-0 flex-1">
                <slot name="heading" />
            </div>
            <button
                type="button"
                class="shrink-0 rounded-md px-2 py-1 text-xs font-medium text-gray-500 hover:bg-gray-100 dark:text-gray-400 dark:hover:bg-white/5"
                :data-testid="`collapse-${id}`"
                :aria-expanded="!collapsed"
                @click="collapsed = !collapsed"
            >
                {{ collapsed ? `Show ${label}` : `Hide ${label}` }}
            </button>
        </div>
        <div v-show="!collapsed">
            <slot />
        </div>
    </section>
</template>
