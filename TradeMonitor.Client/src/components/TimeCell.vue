<script setup lang="ts">
import { computed } from 'vue'
import { formatTimeCt, isoDateTime } from '@/lib/format'

const props = defineProps<{ value: string | number | Date | null | undefined }>()
const text = computed(() => formatTimeCt(props.value))
const iso = computed(() => isoDateTime(props.value))
</script>

<template>
    <time v-if="text" :datetime="iso">{{ text }}</time>
    <span v-else class="text-gray-400">n/a</span>
</template>
