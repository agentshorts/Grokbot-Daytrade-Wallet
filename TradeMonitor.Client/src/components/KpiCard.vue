<script setup lang="ts">
import MoneyCell from '@/components/MoneyCell.vue'
import PctCell from '@/components/PctCell.vue'
import type { Kpi } from '@/lib/book'

defineProps<{ kpi: Kpi; loading?: boolean; error?: string | null }>()
</script>

<template>
    <article class="min-w-0 rounded-2xl border border-gray-200 bg-white p-4 md:p-5 dark:border-gray-800 dark:bg-white/[0.03]" :data-testid="`kpi-${kpi.id}`">
        <p class="text-sm text-gray-500 dark:text-gray-400">{{ kpi.label }}</p>
        <div v-if="loading" class="mt-3 h-7 w-24 animate-pulse rounded bg-gray-100 dark:bg-gray-800" role="status">
            <span class="sr-only">Loading {{ kpi.label }}</span>
        </div>
        <p v-else-if="error" class="mt-3 text-sm text-error-600 dark:text-error-400" role="alert">{{ error }}</p>
        <p v-else-if="kpi.value == null" class="mt-3 text-sm text-gray-500 dark:text-gray-400">{{ kpi.emptyLabel || 'n/a' }}</p>
        <p v-else class="mt-2 text-lg font-semibold text-gray-800 dark:text-white/90">
            <MoneyCell v-if="kpi.display === 'money'" :value="kpi.value" :signed="kpi.signed" />
            <PctCell v-else-if="kpi.display === 'pct'" :value="kpi.value" :signed="kpi.signed" />
            <span v-else class="tabular-nums">{{ kpi.value }}</span>
        </p>
        <p v-if="kpi.hint && !loading && !error" class="mt-2 text-xs text-warning-600 dark:text-warning-400">{{ kpi.hint }}</p>
    </article>
</template>
