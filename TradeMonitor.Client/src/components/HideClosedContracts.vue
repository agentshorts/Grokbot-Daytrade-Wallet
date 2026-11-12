<script setup lang="ts">
defineProps<{ count: number }>()
const hide = defineModel<boolean>({ required: true })
</script>

<template>
    <label data-testid="hide-closed-contracts" class="inline-flex items-center gap-2 text-sm text-gray-600 dark:text-gray-300">
        <input v-model="hide" type="checkbox" class="size-4 rounded border-gray-300 text-brand-500 focus:ring-brand-500 dark:border-gray-600" />
        Hide closed contracts
        <span class="text-gray-400">({{ count }})</span>
    </label>
</template>
