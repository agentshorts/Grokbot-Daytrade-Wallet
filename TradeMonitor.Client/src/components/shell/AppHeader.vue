<script setup lang="ts">
// Header adapted from TailAdmin Vue (MIT): https://github.com/TailAdmin/vue-tailwind-admin-dashboard
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuth } from '@servicestack/vue'
import SegmentedControl from '@/components/shell/SegmentedControl.vue'
import ThemeToggle from '@/components/shell/ThemeToggle.vue'
import NavIcon from '@/components/shell/NavIcon.vue'
import TimeCell from '@/components/TimeCell.vue'
import { signout } from '@/lib/auth'
import type { DateRangeKey, LedgerMode } from '@/lib/filters'
import { walletLabelFor, walletRecordedAt } from '@/lib/walletNames'
import { useDashboard } from '@/composables/useDashboard'
import { useFilterQuery } from '@/composables/useFilterQuery'
import { useSidebar } from '@/composables/useSidebar'

const router = useRouter()
const route = useRoute()
const { user } = useAuth()
const { open, openMobile, isMobile, toggleSidebar } = useSidebar()
const { filters, setMode, setRange, setWallet } = useFilterQuery()
const { view } = useDashboard()

const expanded = computed(() => (isMobile.value ? openMobile.value : open.value))
const walletPage = computed(() => route.path === '/wallets' || route.path.startsWith('/wallet/'))
const walletLabel = computed(() => {
    const key = filters.wallet
    if (!key) return ''
    return walletLabelFor(key) || view.value?.walletNames[key] || key
})
const recordedAt = computed(() => (walletPage.value ? walletRecordedAt() : (view.value?.lastRecordedAt ?? '')))

const modes = [
    { value: 'all', label: 'All' },
    { value: 'real', label: 'Real' },
    { value: 'paper', label: 'Paper' },
]
const ranges = [
    { value: 'today', label: 'Today' },
    { value: '7d', label: '7D' },
    { value: '30d', label: '30D' },
    { value: 'all', label: 'All' },
]

function onMode(value: string) {
    if (value === 'all' || value === 'real' || value === 'paper') void setMode(value as LedgerMode)
}

function onRange(value: string) {
    if (value === 'today' || value === '7d' || value === '30d' || value === 'all') void setRange(value as DateRangeKey)
}

function onSignOut() {
    void signout(router)
}
</script>

<template>
    <header class="sticky top-0 z-30 border-b border-gray-200 bg-white dark:border-gray-800 dark:bg-gray-900">
        <div class="flex items-center gap-2 px-3 py-3 lg:px-6">
            <button
                type="button"
                class="inline-flex size-10 items-center justify-center rounded-lg border border-gray-200 text-gray-500 hover:bg-gray-50 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-500 dark:border-gray-800 dark:text-gray-400 dark:hover:bg-white/5"
                aria-label="Toggle sidebar"
                :aria-expanded="expanded"
                @click="toggleSidebar"
            >
                <NavIcon :name="openMobile ? 'close' : 'menu'" class="size-5" />
            </button>
            <RouterLink to="/" class="text-base font-semibold text-gray-900 xl:hidden dark:text-white">TradeMonitor</RouterLink>
            <div class="ms-auto flex items-center gap-2">
                <ThemeToggle />
                <RouterLink
                    v-if="user"
                    to="/profile"
                    class="hidden max-w-40 truncate rounded-lg px-2 py-1.5 text-sm font-medium text-gray-700 hover:bg-gray-50 sm:block dark:text-gray-200 dark:hover:bg-white/5"
                >
                    {{ user.userName }}
                </RouterLink>
                <button
                    v-if="user"
                    type="button"
                    class="rounded-lg px-2.5 py-1.5 text-sm font-medium text-gray-600 hover:bg-gray-50 dark:text-gray-300 dark:hover:bg-white/5"
                    @click="onSignOut"
                >
                    Sign out
                </button>
                <RouterLink v-else to="/signin" class="rounded-lg bg-brand-500 px-3 py-1.5 text-sm font-medium text-white hover:bg-brand-600">
                    Sign in
                </RouterLink>
            </div>
        </div>
        <div v-if="route.path !== '/live'" class="flex flex-col gap-3 px-3 pb-3 sm:flex-row sm:flex-wrap sm:items-center lg:px-6">
            <SegmentedControl label="Ledger mode" test-id="mode-switch" :value="filters.mode" :options="modes" @select="onMode" />
            <SegmentedControl label="Date range" test-id="range-switch" :value="filters.range" :options="ranges" @select="onRange" />
            <button
                v-if="filters.wallet"
                type="button"
                class="inline-flex max-w-full items-center gap-2 rounded-full bg-brand-50 px-3 py-1 text-sm font-medium text-brand-700 dark:bg-brand-500/15 dark:text-brand-300"
                @click="setWallet(null)"
            >
                <span class="truncate">{{ walletLabel }}</span>
                <span aria-hidden="true">×</span>
                <span class="sr-only">Clear wallet filter</span>
            </button>
            <p class="text-sm text-gray-500 sm:ms-auto dark:text-gray-400" data-testid="last-recorded">
                Last recorded
                <TimeCell v-if="recordedAt" class="text-gray-800 dark:text-gray-100" :value="recordedAt" />
                <span v-else>n/a</span>
            </p>
        </div>
    </header>
</template>
