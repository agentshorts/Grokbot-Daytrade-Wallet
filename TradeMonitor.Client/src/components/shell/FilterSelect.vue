<script setup lang="ts">
defineProps<{
    label: string
    value: string
    testId: string
    emptyLabel: string
    options: { value: string; label: string }[]
}>()

const emit = defineEmits<{ change: [value: string] }>()

function onChange(event: Event) {
    const target = event.target
    if (!(target instanceof HTMLSelectElement)) return
    emit('change', target.value.trim())
}
</script>

<template>
    <label class="block w-full text-sm sm:w-56">
        <span class="mb-1 block text-xs font-medium tracking-wide text-gray-400 uppercase">{{ label }}</span>
        <span class="relative block">
            <select
                class="w-full appearance-none rounded-lg border border-gray-200 bg-white bg-none py-1.5 ps-3 pe-9 text-sm text-gray-800 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-500 dark:border-gray-700 dark:bg-gray-900 dark:text-white/90"
                :aria-label="label"
                :data-testid="testId"
                :value="value"
                @change="onChange"
            >
                <option value="">{{ emptyLabel }}</option>
                <option v-for="option in options" :key="option.value" :value="option.value">{{ option.label }}</option>
            </select>
            <svg
                viewBox="0 0 20 20"
                fill="none"
                aria-hidden="true"
                class="pointer-events-none absolute end-2.5 top-1/2 size-4 -translate-y-1/2 text-gray-500 dark:text-gray-400"
            >
                <path d="M5 7.5 10 12.5 15 7.5" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" />
            </svg>
        </span>
    </label>
</template>
