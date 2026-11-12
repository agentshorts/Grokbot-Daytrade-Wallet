<script setup lang="ts">
// Shell layout adapted from TailAdmin Vue (MIT): https://github.com/TailAdmin/vue-tailwind-admin-dashboard
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import NavIcon from '@/components/shell/NavIcon.vue'
import { useFilterQuery } from '@/composables/useFilterQuery'
import { useSidebar } from '@/composables/useSidebar'

const route = useRoute()
const { open, openMobile, isMobile, closeMobile } = useSidebar()
const { shellLocation } = useFilterQuery()

const items = [
    { label: 'Overview', path: '/', icon: 'grid' as const },
    { label: 'Trades', path: '/trades', icon: 'trades' as const },
    { label: 'Wallets', path: '/wallets', icon: 'wallet' as const },
    { label: 'Live', path: '/live', icon: 'live' as const },
    { label: 'Settings', path: '/settings', icon: 'settings' as const },
]

const wide = computed(() => isMobile.value || open.value)
const showLabels = computed(() => (isMobile.value ? true : open.value))

function active(path: string) {
    if (path === '/') return route.path === '/'
    if (path === '/wallets') return route.path === '/wallets' || route.path.startsWith('/wallet/')
    return route.path === path || route.path.startsWith(`${path}/`)
}
</script>

<template>
    <div v-if="openMobile" class="fixed inset-0 z-40 bg-gray-900/50 xl:hidden" @click="closeMobile" />
    <aside
        class="fixed inset-y-0 start-0 z-50 flex h-screen flex-col border-r border-gray-200 bg-white transition-all duration-300 ease-in-out dark:border-gray-800 dark:bg-gray-900"
        :class="[wide ? 'w-[290px]' : 'w-[90px]', openMobile ? 'translate-x-0' : '-translate-x-full xl:translate-x-0']"
        aria-label="Primary"
    >
        <div class="flex items-center px-5 py-6">
            <RouterLink :to="shellLocation('/')" class="flex items-center gap-2.5">
                <span class="flex size-9 shrink-0 items-center justify-center rounded-lg bg-brand-500 text-sm font-semibold text-white">T</span>
                <span v-if="showLabels" class="text-lg font-semibold tracking-tight text-gray-900 dark:text-white">TradeMonitor</span>
            </RouterLink>
        </div>
        <div class="custom-scrollbar flex-1 overflow-y-auto px-4 pb-6">
            <p class="mb-3 px-3 text-xs font-medium tracking-wide text-gray-400 uppercase" :class="showLabels ? 'text-start' : 'text-center'">
                {{ showLabels ? 'Menu' : '···' }}
            </p>
            <nav class="flex flex-col gap-1">
                <RouterLink
                    v-for="item in items"
                    :key="item.path"
                    :to="shellLocation(item.path)"
                    class="menu-item group"
                    :class="[active(item.path) ? 'menu-item-active' : 'menu-item-inactive', showLabels ? 'justify-start' : 'justify-center']"
                    :aria-current="active(item.path) ? 'page' : undefined"
                    :title="item.label"
                >
                    <span :class="active(item.path) ? 'menu-item-icon-active' : 'menu-item-icon-inactive'">
                        <NavIcon :name="item.icon" class="size-6" />
                    </span>
                    <span v-if="showLabels">{{ item.label }}</span>
                </RouterLink>
            </nav>
        </div>
        <div v-if="showLabels" class="px-4 pb-6">
            <p class="rounded-2xl bg-gray-50 px-4 py-4 text-sm text-gray-500 dark:bg-white/[0.03] dark:text-gray-400">
                Paper trades on Live are saved. A real wallet on Live is a simulation.
            </p>
        </div>
    </aside>
</template>
