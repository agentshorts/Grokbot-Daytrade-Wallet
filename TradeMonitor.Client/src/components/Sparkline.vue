<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{ values: number[]; positive: boolean }>()

const geometry = computed(() => {
    const values = props.values
    if (values.length < 2) return null
    const min = Math.min(...values)
    const max = Math.max(...values)
    const span = max - min || 1
    const line = values
        .map((value, index) => {
            const x = (index / (values.length - 1)) * 120
            const y = 32 - ((value - min) / span) * 28 - 2
            return `${x.toFixed(2)},${y.toFixed(2)}`
        })
        .join(' ')
    return { line, area: `${line} 120,36 0,36` }
})
</script>

<template>
    <svg v-if="geometry" viewBox="0 0 120 36" class="h-9 w-full" preserveAspectRatio="none" role="img" aria-label="Value over the selected range">
        <polygon :points="geometry.area" :class="positive ? 'text-success-500' : 'text-error-500'" fill="currentColor" opacity="0.16" />
        <polyline
            :points="geometry.line"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linejoin="round"
            stroke-linecap="round"
            vector-effect="non-scaling-stroke"
            :class="positive ? 'text-success-500' : 'text-error-500'"
        />
    </svg>
    <p v-else class="text-xs text-gray-400">No history in this range</p>
</template>
