<script setup lang="ts">
import { computed } from 'vue'
import { chartMenu, type ChartHit } from '@/lib/chartMenu'

const props = defineProps<{
    hit: ChartHit
    x: number
    y: number
    pendingMoveId?: number
}>()

const emit = defineEmits<{ action: [id: string] }>()

const actions = computed(() => chartMenu(props.hit, { pendingMoveId: props.pendingMoveId }))

function tone(value: 'buy' | 'sell' | 'neutral') {
    if (value === 'buy') return 'text-success-600'
    if (value === 'sell') return 'text-error-500'
    return 'text-gray-800 dark:text-gray-100'
}
</script>

<template>
    <ul
        role="menu"
        data-testid="chart-menu"
        class="fixed z-50 min-w-56 rounded-xl border border-gray-200 bg-white py-1 shadow-lg dark:border-gray-700 dark:bg-gray-900"
        :style="{ left: `${x}px`, top: `${y}px` }"
    >
        <li v-for="action in actions" :key="action.id">
            <button
                type="button"
                role="menuitem"
                class="block w-full px-3 py-2 text-start text-sm font-medium hover:bg-gray-50 dark:hover:bg-white/5"
                :class="tone(action.tone)"
                :data-testid="`menu-${action.id}`"
                @click="emit('action', action.id)"
            >
                {{ action.label }}
            </button>
        </li>
    </ul>
</template>
