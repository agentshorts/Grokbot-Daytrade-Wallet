<script setup lang="ts">
import { computed, defineAsyncComponent } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import ChartPending from '@/components/shell/ChartPending.vue'
import StatePanel from '@/components/shell/StatePanel.vue'
import TradeList from '@/components/TradeList.vue'
import type { TradeRecord } from '@/lib/book'
import { toChartPoints, toTradeMarkers } from '@/lib/priceSeries'
import { useDashboard } from '@/composables/useDashboard'
import { useFilterQuery } from '@/composables/useFilterQuery'
import { useTheme } from '@/composables/useTheme'

const PriceChart = defineAsyncComponent({
    loader: () => import('@/components/PriceChart.vue'),
    loadingComponent: ChartPending,
    delay: 0,
})

const route = useRoute()
const router = useRouter()
const { view, status, error, refresh } = useDashboard()
const { filters, setWallet, shellLocation } = useFilterQuery()
const { isDark } = useTheme()

const symbols = computed(() => {
    const found: string[] = []
    for (const trade of view.value?.trades ?? []) {
        if (!found.includes(trade.symbol)) found.push(trade.symbol)
    }
    return found
})

const selectedSymbol = computed(() => {
    const requested = typeof route.query.symbol === 'string' ? route.query.symbol : ''
    if (requested && symbols.value.includes(requested)) return requested
    return symbols.value[0] ?? ''
})

const priceModel = computed(() => {
    const symbol = selectedSymbol.value
    const marks = (view.value?.marks ?? []).filter((mark) => mark.symbol === symbol)
    const trades = (view.value?.trades ?? []).filter((trade) => trade.symbol === symbol)
    return {
        title: trades[0]?.title || marks[0]?.symbol || symbol,
        points: toChartPoints([
            ...marks.map((mark) => ({ at: mark.at, price: mark.price })),
            ...trades.map((trade) => ({ at: trade.executedAt, price: trade.price })),
        ]),
        markers: toTradeMarkers(trades),
    }
})

function selectSymbol(symbol: string) {
    void router.replace(shellLocation('/trades', { symbol }))
}

function selectTrade(trade: TradeRecord) {
    selectSymbol(trade.symbol)
}
</script>

<template>
    <div data-testid="trades" class="space-y-6">
        <div>
            <h1 class="text-xl font-semibold text-gray-900 dark:text-white/90">Trades</h1>
            <p class="mt-1 text-sm text-gray-500 dark:text-gray-400">Fills in this view. Choose a row to put that symbol on the price chart.</p>
        </div>

        <StatePanel v-if="status === 'loading' && !view" tone="loading" title="Loading trades" />
        <StatePanel v-else-if="status === 'error' && !view" tone="error" title="Could not load trades" :body="error || 'Try again in a moment.'">
            <button type="button" class="rounded-lg bg-brand-500 px-3 py-1.5 text-sm font-medium text-white" @click="refresh()">Try again</button>
        </StatePanel>
        <StatePanel v-else-if="view?.emptyBook" title="No wallets yet" body="Trades show up after the agent records a wallet." />
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
            <article class="min-w-0 overflow-hidden rounded-2xl border border-gray-200 bg-white p-4 md:p-5 dark:border-gray-800 dark:bg-white/[0.03]">
                <div class="mb-4 flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
                    <div>
                        <h2 class="text-base font-semibold text-gray-800 dark:text-white/90">{{ priceModel.title || 'Price' }}</h2>
                        <p class="mt-1 text-sm text-gray-500 dark:text-gray-400">Recorded marks with buy and sell markers at the fill.</p>
                    </div>
                    <div class="flex flex-wrap gap-2">
                        <button
                            v-for="symbol in symbols"
                            :key="symbol"
                            type="button"
                            class="rounded-full px-3 py-1 text-sm font-medium"
                            :class="symbol === selectedSymbol ? 'bg-brand-500 text-white' : 'bg-gray-100 text-gray-600 dark:bg-white/5 dark:text-gray-300'"
                            :aria-pressed="symbol === selectedSymbol"
                            @click="selectSymbol(symbol)"
                        >
                            {{ symbol }}
                        </button>
                    </div>
                </div>
                <PriceChart :points="priceModel.points" :markers="priceModel.markers" :dark="isDark" :empty="priceModel.points.length === 0" />
            </article>
            <section class="min-w-0 overflow-hidden rounded-2xl border border-gray-200 bg-white dark:border-gray-800 dark:bg-white/[0.03]">
                <TradeList :trades="view.trades" :selected-symbol="selectedSymbol" @select="selectTrade" />
            </section>
        </template>
    </div>
</template>
