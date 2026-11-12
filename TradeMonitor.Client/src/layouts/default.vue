<script setup lang="ts">
import { onMounted } from 'vue'
import TradeDrawer from '@/components/TradeDrawer.vue'
import AppHeader from '@/components/shell/AppHeader.vue'
import AppSidebar from '@/components/shell/AppSidebar.vue'
import { provideDashboard } from '@/composables/useDashboard'
import { useFilterQuery } from '@/composables/useFilterQuery'
import { installSidebar, useSidebar } from '@/composables/useSidebar'
import { syncTheme } from '@/composables/useTheme'

installSidebar()
useFilterQuery()
provideDashboard()
const { open } = useSidebar()

onMounted(() => {
    syncTheme()
})
</script>

<template>
    <div data-testid="app-shell" class="min-h-screen bg-gray-50 text-gray-800 dark:bg-gray-900 dark:text-gray-100">
        <AppSidebar />
        <div class="min-w-0 transition-[padding] duration-300 ease-in-out" :class="open ? 'xl:ps-[290px]' : 'xl:ps-[90px]'">
            <AppHeader />
            <main class="mx-auto w-full min-w-0 max-w-screen-2xl p-4 md:p-6">
                <RouterView />
            </main>
        </div>
        <TradeDrawer />
    </div>
</template>
