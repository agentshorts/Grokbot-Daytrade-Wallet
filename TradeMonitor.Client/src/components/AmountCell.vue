<script setup lang="ts">
import MoneyCell from '@/components/MoneyCell.vue'

withDefaults(
    defineProps<{
        value: number | null | undefined
        signed?: boolean
    }>(),
    { signed: true },
)
</script>

<template>
    <span v-if="value == null || Number.isNaN(value)" class="tabular-nums text-gray-400">-</span>
    <MoneyCell v-else :value="value" :signed="signed" />
</template>
