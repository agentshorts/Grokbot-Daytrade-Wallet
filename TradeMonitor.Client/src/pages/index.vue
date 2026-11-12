<script setup lang="ts">
import { computed, defineAsyncComponent } from 'vue'
import { useRouter } from 'vue-router'
import ChartPending from '@/components/shell/ChartPending.vue'
import KpiCard from '@/components/KpiCard.vue'
import StatePanel from '@/components/shell/StatePanel.vue'
import TradeList from '@/components/TradeList.vue'
import WalletCard from '@/components/WalletCard.vue'
import { equityOption, pnlBarsOption, symbolPnlOption } from '@/lib/chartOptions'
import type { TradeRecord } from '@/lib/book'
import { useDashboard } from '@/composables/useDashboard'
import { useFilterQuery } from '@/composables/useFilterQuery'
import { useTheme } from '@/composables/useTheme'

const EChart = defineAsyncComponent({
    loader: () => import('@/components/EChart.vue'),
    loadingComponent: ChartPending,
    delay: 0,
})

const router = useRouter()
const { view, status, error, refresh } = useDashboard()
const { filters, setWallet, shellLocation } = useFilterQuery()
const { isDark } = useTheme()

const recent = computed(() => view.value?.trades.slice(0, 8) ?? [])
const equity = computed(() =>
    view.value && view.value.equity.some((series) => series.points.length > 0) ? equityOption(view.value.equity, isDark.value) : null,
)
const bars = computed(() => (view.value && view.value.pnlBars.length > 0 ? pnlBarsOption(view.value.pnlBars, isDark.value) : null))
const bySymbol = computed(() => (view.value && view.value.pnlBySymbol.length > 0 ? symbolPnlOption(view.value.pnlBySymbol, isDark.value) : null))

function selectWallet(key: string) {
    void setWallet(filters.wallet === key ? null : key)
}

function openTrade(trade: TradeRecord) {
    void router.push(shellLocation('/trades', { symbol: trade.symbol }))
}
</script>

<template>
    <div data-testid="overview" class="space-y-6">
        <div>
            <h1 class="text-xl font-semibold text-gray-900 dark:text-white/90">Overview</h1>
            <p class="mt-1 text-sm text-gray-500 dark:text-gray-400">Value, P&L and wallets for the current mode and range.</p>
        </div>

        <p
            v-if="error && view"
            class="rounded-xl border border-error-200 bg-error-25 px-4 py-3 text-sm text-error-700 dark:border-error-800 dark:bg-error-500/10 dark:text-error-300"
            role="alert"
        >
            {{ error }}
            <button type="button" class="ms-2 font-medium underline" @click="refresh()">Try again</button>
        </p>

        <StatePanel v-if="status === 'loading' && !view" tone="loading" title="Loading the book" body="Reading the latest recorded wallets and trades." />
        <StatePanel v-else-if="status === 'error' && !view" tone="error" title="Could not load the book" :body="error || 'Try again in a moment.'">
            <button type="button" class="rounded-lg bg-brand-500 px-3 py-1.5 text-sm font-medium text-white" @click="refresh()">Try again</button>
        </StatePanel>
        <StatePanel v-else-if="view?.emptyBook" title="No wallets yet" body="When the agent records a wallet, it will show up here." />
        <StatePanel v-else-if="view?.noMatches" title="Nothing in this view" body="No wallet matches the current mode and wallet filter.">
            <button
                v-if="filters.wallet"
                type="button"
                class="rounded-lg border border-gray-200 px-3 py-1.5 text-sm font-medium dark:border-gray-700"
                @click="setWallet(null)"
            >
                Clear wallet
            </button>
        </StatePanel>
        <template v-else-if="view">
            <section class="grid grid-cols-2 gap-3 md:gap-4 xl:grid-cols-4">
                <KpiCard v-for="kpi in view.kpis" :key="kpi.id" :kpi="kpi" />
            </section>
            <section class="grid grid-cols-1 gap-4 lg:grid-cols-3">
                <WalletCard
                    v-for="wallet in view.wallets"
                    :key="wallet.key"
                    :wallet="wallet"
                    :selected="filters.wallet === wallet.key"
                    @select="selectWallet(wallet.key)"
                />
            </section>
            <section class="grid grid-cols-1 gap-4 xl:grid-cols-2">
                <article class="min-w-0 overflow-hidden rounded-2xl border border-gray-200 bg-white p-4 md:p-5 dark:border-gray-800 dark:bg-white/[0.03]">
                    <h2 class="text-base font-semibold text-gray-800 dark:text-white/90">Equity</h2>
                    <p class="mt-1 mb-4 text-sm text-gray-500 dark:text-gray-400">Stacked by wallet. Scroll to zoom.</p>
                    <EChart :option="equity" :empty="!equity" />
                </article>
                <article class="min-w-0 overflow-hidden rounded-2xl border border-gray-200 bg-white p-4 md:p-5 dark:border-gray-800 dark:bg-white/[0.03]">
                    <h2 class="text-base font-semibold text-gray-800 dark:text-white/90">P&L</h2>
                    <p class="mt-1 mb-4 text-sm text-gray-500 dark:text-gray-400">Green is up, red is down. Transfers are left out.</p>
                    <EChart :option="bars" :empty="!bars" />
                </article>
            </section>
            <article class="min-w-0 overflow-hidden rounded-2xl border border-gray-200 bg-white p-4 md:p-5 dark:border-gray-800 dark:bg-white/[0.03]">
                <h2 class="text-base font-semibold text-gray-800 dark:text-white/90">P&L by symbol</h2>
                <p class="mt-1 mb-4 text-sm text-gray-500 dark:text-gray-400">Realized in this range, plus open unrealized.</p>
                <EChart :option="bySymbol" :empty="!bySymbol" />
            </article>
            <section class="min-w-0 overflow-hidden rounded-2xl border border-gray-200 bg-white dark:border-gray-800 dark:bg-white/[0.03]">
                <div class="flex items-center justify-between gap-3 px-4 py-4 md:px-5">
                    <h2 class="text-base font-semibold text-gray-800 dark:text-white/90">Recent trades</h2>
                    <RouterLink :to="shellLocation('/trades')" class="text-sm font-medium text-brand-600 hover:text-brand-700 dark:text-brand-400">
                        All trades
                    </RouterLink>
                </div>
                <TradeList :trades="recent" @select="openTrade" />
            </section>
        </template>
    </div>
</template>
