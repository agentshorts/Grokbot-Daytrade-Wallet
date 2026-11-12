<script setup lang="ts">
defineProps<{
    label: string
    value: string
    options: { value: string; label: string }[]
    testId?: string
}>()

const emit = defineEmits<{ select: [value: string] }>()
</script>

<template>
    <div class="flex max-w-full flex-wrap gap-1 rounded-lg bg-gray-100 p-1 dark:bg-white/5" role="group" :aria-label="label" :data-testid="testId">
        <button
            v-for="option in options"
            :key="option.value"
            type="button"
            class="rounded-md px-3 py-1.5 text-sm font-medium transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-500"
            :class="
                option.value === value
                    ? 'bg-white text-gray-900 shadow-theme-xs dark:bg-gray-800 dark:text-white'
                    : 'text-gray-500 hover:text-gray-800 dark:text-gray-400 dark:hover:text-gray-200'
            "
            :aria-pressed="option.value === value"
            @click="emit('select', option.value)"
        >
            {{ option.label }}
        </button>
    </div>
</template>
