<script setup lang="ts">
import { computed } from 'vue'
import { formatPct } from '@/lib/format'

const props = withDefaults(
    defineProps<{
        value: number | null | undefined
        signed?: boolean
    }>(),
    { signed: true },
)

const text = computed(() => formatPct(props.value, { signed: props.signed }))
const tone = computed(() => {
    if (props.value == null || Number.isNaN(props.value)) return 'text-gray-400'
    if (props.value < 0) return 'text-error-600 dark:text-error-400'
    if (props.signed && props.value > 0) return 'text-success-600 dark:text-success-400'
    return 'text-gray-800 dark:text-white/90'
})
</script>

<template>
    <span class="tabular-nums" :class="tone">{{ text || 'n/a' }}</span>
</template>
