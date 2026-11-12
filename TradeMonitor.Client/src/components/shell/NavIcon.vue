<script setup lang="ts">
defineProps<{ name: 'grid' | 'trades' | 'wallet' | 'menu' | 'close' | 'sun' | 'moon' }>()
</script>

<template>
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
        <template v-if="name === 'grid'">
            <rect x="3.5" y="3.5" width="7" height="7" rx="1.5" />
            <rect x="13.5" y="3.5" width="7" height="7" rx="1.5" />
            <rect x="3.5" y="13.5" width="7" height="7" rx="1.5" />
            <rect x="13.5" y="13.5" width="7" height="7" rx="1.5" />
        </template>
        <template v-else-if="name === 'trades'">
            <path d="M4 7h16" />
            <path d="M16 3l4 4-4 4" />
            <path d="M20 17H4" />
            <path d="M8 13l-4 4 4 4" />
        </template>
        <template v-else-if="name === 'wallet'">
            <path d="M3 8.5h18v11H3z" />
            <path d="M3 8.5V6.5h14" />
            <path d="M16 13.5h3" />
        </template>
        <template v-else-if="name === 'menu'">
            <path d="M4 7h16M4 12h16M4 17h16" />
        </template>
        <template v-else-if="name === 'close'">
            <path d="M6 6l12 12M18 6L6 18" />
        </template>
        <template v-else-if="name === 'sun'">
            <circle cx="12" cy="12" r="4" />
            <path d="M12 3v2M12 19v2M3 12h2M19 12h2M5.6 5.6l1.4 1.4M17 17l1.4 1.4M18.4 5.6L17 7M7 17l-1.4 1.4" />
        </template>
        <template v-else>
            <path d="M21 14.5A8.5 8.5 0 1 1 9.5 3 7 7 0 0 0 21 14.5z" />
        </template>
    </svg>
</template>
