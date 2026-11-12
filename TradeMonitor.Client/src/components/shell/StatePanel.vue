<script setup lang="ts">
withDefaults(
    defineProps<{
        title: string
        body?: string
        tone?: 'neutral' | 'error' | 'loading'
    }>(),
    { tone: 'neutral' },
)
</script>

<template>
    <div
        class="rounded-2xl border border-gray-200 bg-white px-5 py-10 text-center dark:border-gray-800 dark:bg-white/[0.03]"
        :role="tone === 'error' ? 'alert' : 'status'"
    >
        <div v-if="tone === 'loading'" class="mx-auto mb-4 h-8 w-32 animate-pulse rounded-lg bg-gray-100 dark:bg-gray-800" />
        <h2 class="text-base font-semibold text-gray-800 dark:text-white/90">{{ title }}</h2>
        <p v-if="body" class="mx-auto mt-2 max-w-md text-sm text-gray-500 dark:text-gray-400">{{ body }}</p>
        <div v-if="$slots.default" class="mt-4">
            <slot />
        </div>
    </div>
</template>
