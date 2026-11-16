<script setup lang="ts">
import { useSectionCollapsed } from '@/composables/useUiState'

const props = withDefaults(
    defineProps<{
        view: string
        modelValue: string
        placeholder?: string
        collapsible?: boolean
    }>(),
    {
        placeholder: 'Filter by ticker, wallet, symbol, or side',
        collapsible: true,
    },
)

const emit = defineEmits<{ 'update:modelValue': [value: string] }>()
const collapsed = useSectionCollapsed(`${props.view}-filter`)

function onInput(event: Event) {
    emit('update:modelValue', (event.target as HTMLInputElement).value)
}
</script>

<template>
    <div class="flex flex-col gap-2" :data-testid="`text-filter-${view}`">
        <div v-if="collapsible" class="flex items-center gap-2">
            <button
                type="button"
                class="rounded-md px-2 py-1 text-xs font-medium text-gray-500 hover:bg-gray-100 dark:text-gray-400 dark:hover:bg-white/5"
                :data-testid="`text-filter-toggle-${view}`"
                :aria-expanded="!collapsed"
                @click="collapsed = !collapsed"
            >
                {{ collapsed ? 'Show filter' : 'Hide filter' }}
            </button>
            <button
                v-if="modelValue"
                type="button"
                class="text-sm font-medium text-brand-600"
                :data-testid="`text-filter-clear-${view}`"
                @click="emit('update:modelValue', '')"
            >
                Clear
            </button>
            <p v-if="collapsed && modelValue" class="truncate text-xs text-gray-500">{{ modelValue }}</p>
        </div>
        <label v-show="!collapsible || !collapsed" class="block max-w-md text-sm">
            <span class="sr-only">Filter {{ view }}</span>
            <input
                :value="modelValue"
                type="search"
                class="w-full rounded-lg border border-gray-200 bg-white px-3 py-1.5 text-sm dark:border-gray-700 dark:bg-gray-900"
                :placeholder="placeholder"
                :data-testid="`text-filter-input-${view}`"
                @input="onInput"
            />
        </label>
    </div>
</template>
