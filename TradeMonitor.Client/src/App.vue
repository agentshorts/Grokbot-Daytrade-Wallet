<script setup lang="ts">
import { useHead } from '@unhead/vue'

// https://github.com/vueuse/head
// you can use this to manipulate the document head in any components,
// they will be rendered correctly in the html results with vite-ssg
useHead({
    title: 'TradeMonitor',
    meta: [{ name: 'description', content: 'Local dashboard for trading wallets.' }],
    bodyAttrs: {
        class: 'bg-gray-50 font-outfit text-gray-800 antialiased dark:bg-gray-900 dark:text-gray-100',
    },
})
</script>

<template>
    <RouterView />
</template>
