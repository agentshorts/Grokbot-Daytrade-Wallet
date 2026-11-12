<script setup lang="ts">
import NavIcon from '@/components/shell/NavIcon.vue'
import { useTheme } from '@/composables/useTheme'

const { isDark, toggleTheme } = useTheme()
</script>

<template>
    <button
        type="button"
        class="inline-flex size-10 items-center justify-center rounded-lg border border-gray-200 text-gray-500 hover:bg-gray-50 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-500 dark:border-gray-800 dark:text-gray-400 dark:hover:bg-white/5"
        :aria-label="isDark ? 'Switch to light mode' : 'Switch to dark mode'"
        :aria-pressed="isDark"
        @click="toggleTheme"
    >
        <NavIcon :name="isDark ? 'sun' : 'moon'" class="size-5" />
    </button>
</template>
