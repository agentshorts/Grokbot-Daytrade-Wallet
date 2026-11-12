<script setup lang="ts">
import { computed, defineAsyncComponent, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import ChartPending from '@/components/shell/ChartPending.vue'
import KpiCard from '@/components/KpiCard.vue'
import MoneyCell from '@/components/MoneyCell.vue'
import StatePanel from '@/components/shell/StatePanel.vue'
import TradeList from '@/components/TradeList.vue'
import TransferList from '@/components/TransferList.vue'
import WalletPositions from '@/components/WalletPositions.vue'
import { balanceEquityOption, dailyPnlOption } from '@/lib/chartOptions'
import type { Kpi } from '@/lib/book'
import { useFilterQuery } from '@/composables/useFilterQuery'
import { useWalletDetail } from '@/composables/useWalletPages'
import { useTheme } from '@/composables/useTheme'
import { DASH, type WalletDetailModel } from '@/lib/walletView'

const EChart = defineAsyncComponent({
    loader: () => import('@/components/EChart.vue'),
    loadingComponent: ChartPending,
    delay: 0,
})

const route = useRoute()
const router = useRouter()
const { filters, setWallet, shellLocation } = useFilterQuery()
const { isDark } = useTheme()

function routeKey(): string {
    const params = route.params
    const raw = 'key' in params ? params.key : ''
    const text = typeof raw === 'string' ? raw : ''
    try {
        return decodeURIComponent(text)
    } catch {
        return text
    }
}

const key = computed(() => routeKey())
const { status, error, errorStatus, model, refresh } = useWalletDetail(() => key.value)

watch(
    key,
    (value) => {
        if (value && filters.wallet !== value) void setWallet(value)
    },
    { immediate: true },
)

watch(
    () => filters.wallet,
    (value) => {
        if (!value) {
            if (route.path.startsWith('/wallet/')) void router.push(shellLocation('/wallets'))
            return
        }
        if (value !== key.value) void router.push(shellLocation(`/wallet/${encodeURIComponent(value)}`, { wallet: value }))
    },
)

const equity = computed(() => (model.value && model.value.equity.length > 0 ? balanceEquityOption(model.value.equity, isDark.value) : null))
const daily = computed(() => (model.value && model.value.daily.some((day) => day.pnl != null) ? dailyPnlOption(model.value.daily, isDark.value) : null))

function kpis(detail: WalletDetailModel): Kpi[] {
    const currency = detail.currency
    return [
        {
            id: 'value',
            label: detail.closed ? 'Realized P&L' : 'Total value',
            display: 'money',
            value: detail.totalValue,
            signed: detail.closed,
            currency,
        },
        { id: 'cash', label: 'Cash', display: 'money', value: detail.cash, signed: false, currency },
        { id: 'swept', label: 'Swept', display: 'money', value: detail.swept, signed: false, currency },
        { id: 'fees', label: 'Fees paid', display: 'money', value: detail.fees, signed: false, currency },
        { id: 'change', label: 'Change', display: 'money', value: detail.change, signed: true, currency },
        { id: 'unrealized', label: 'Unrealized P&L', display: 'money', value: detail.unrealizedPnl, signed: true, currency, emptyLabel: DASH },
    ]
}
</script>

<template>
    <div data-testid="wallet-detail" class="space-y-6">
        <StatePanel v-if="status === 'loading' && !model" tone="loading" title="Loading wallet" />
        <StatePanel
            v-else-if="status === 'error' && !model"
            tone="error"
            :title="errorStatus === 404 ? 'Wallet not found' : 'Could not load wallet'"
            :body="error || 'Try again in a moment.'"
        >
            <RouterLink
                v-if="errorStatus === 401 || errorStatus === 403"
                to="/signin"
                class="rounded-lg bg-brand-500 px-3 py-1.5 text-sm font-medium text-white"
            >
                Sign in
            </RouterLink>
            <RouterLink
                v-else-if="errorStatus === 404"
                :to="shellLocation('/wallets', { wallet: null })"
                class="rounded-lg border border-gray-200 px-3 py-1.5 text-sm font-medium dark:border-gray-700"
            >
                All wallets
            </RouterLink>
            <button v-else type="button" class="rounded-lg bg-brand-500 px-3 py-1.5 text-sm font-medium text-white" @click="refresh()">Try again</button>
        </StatePanel>
        <template v-else-if="model">
            <div class="flex flex-wrap items-start justify-between gap-3">
                <div class="min-w-0">
                    <RouterLink :to="shellLocation('/wallets')" class="text-sm font-medium text-brand-600 hover:text-brand-700 dark:text-brand-400">
                        All wallets
                    </RouterLink>
                    <p v-if="model.venue && model.venue.toLowerCase() !== model.name.toLowerCase()" class="mt-2 text-sm text-gray-500 dark:text-gray-400">
                        {{ model.venue }}
                    </p>
                    <h1 class="mt-1 text-xl font-semibold text-gray-900 dark:text-white/90" :title="model.ticker || model.name">{{ model.name }}</h1>
                    <p v-if="model.ticker" class="mt-1 text-xs text-gray-500 dark:text-gray-400" :title="model.ticker">{{ model.ticker }}</p>
                    <span
                        v-if="model.isArchived"
                        class="mt-2 inline-flex rounded-full bg-gray-100 px-2 py-0.5 text-xs font-medium text-gray-600 dark:bg-white/10 dark:text-gray-300"
                    >
                        Archived
                    </span>
                    <span
                        v-if="model.badge"
                        data-testid="contract-badge"
                        class="mt-2 ms-2 inline-flex rounded-full bg-gray-100 px-2 py-0.5 text-xs font-medium text-gray-600 dark:bg-white/10 dark:text-gray-300"
                    >
                        {{ model.badge }}
                    </span>
                </div>
                <span
                    data-testid="wallet-badge"
                    class="rounded-full px-2.5 py-0.5 text-xs font-medium"
                    :class="
                        model.mode === 'real'
                            ? 'bg-success-50 text-success-700 dark:bg-success-500/15 dark:text-success-400'
                            : 'bg-brand-50 text-brand-700 dark:bg-brand-500/15 dark:text-brand-300'
                    "
                >
                    {{ model.mode === 'real' ? 'Real' : 'Paper' }}
                </span>
            </div>

            <p
                v-if="error"
                class="rounded-xl border border-error-200 bg-error-25 px-4 py-3 text-sm text-error-700 dark:border-error-800 dark:bg-error-500/10 dark:text-error-300"
                role="alert"
            >
                {{ error }}
                <button type="button" class="ms-2 font-medium underline" @click="refresh()">Try again</button>
            </p>

            <p
                v-if="model.startingBalanceMissing"
                data-testid="starting-balance"
                class="rounded-xl border border-warning-200 bg-warning-50 px-4 py-3 text-sm text-warning-700 dark:border-warning-500/30 dark:bg-warning-500/10 dark:text-warning-300"
                role="status"
            >
                Starting balance not recorded. Cash is negative because this wallet has no deposit.
            </p>

            <section class="grid grid-cols-2 gap-3 md:gap-4 xl:grid-cols-3">
                <KpiCard v-for="kpi in kpis(model)" :key="kpi.id" :kpi="kpi" />
            </section>
            <p class="text-sm text-gray-500 dark:text-gray-400">Header totals follow the selected range. Positions are the current book.</p>

            <section class="grid grid-cols-1 gap-4 xl:grid-cols-2">
                <article class="min-w-0 overflow-hidden rounded-2xl border border-gray-200 bg-white p-4 md:p-5 dark:border-gray-800 dark:bg-white/[0.03]">
                    <h2 class="text-base font-semibold text-gray-800 dark:text-white/90">Balance and equity</h2>
                    <p class="mt-1 mb-4 text-sm text-gray-500 dark:text-gray-400">
                        <template v-if="model.closed">Ends at the contract close. The line is realized P&L.</template>
                        <template v-else>Cash is the balance. Equity is cash plus open positions.</template>
                    </p>
                    <EChart :option="equity" :empty="!equity" />
                </article>
                <article class="min-w-0 overflow-hidden rounded-2xl border border-gray-200 bg-white p-4 md:p-5 dark:border-gray-800 dark:bg-white/[0.03]">
                    <h2 class="text-base font-semibold text-gray-800 dark:text-white/90">Daily P&L</h2>
                    <p class="mt-1 mb-4 text-sm text-gray-500 dark:text-gray-400">
                        Green is up, red is down. Transfers are left out.
                        <span v-if="model.dailyHasGap">A day with no price mark is left blank.</span>
                    </p>
                    <EChart :option="daily" :empty="!daily" empty-label="No priced P&L in this range" />
                </article>
            </section>

            <WalletPositions
                :open="model.openPositions"
                :closed="model.closedPositions"
                :currency="model.currency"
                :stale-after-minutes="model.staleAfterMinutes"
            />

            <section
                data-testid="wallet-fees"
                class="min-w-0 overflow-hidden rounded-2xl border border-gray-200 bg-white dark:border-gray-800 dark:bg-white/[0.03]"
            >
                <div class="px-4 py-4 md:px-5">
                    <h2 class="text-base font-semibold text-gray-800 dark:text-white/90">Fees</h2>
                    <p class="mt-1 text-sm text-gray-500 dark:text-gray-400">Fees in this range, from the ledger.</p>
                </div>
                <p v-if="model.feeDays.length === 0" class="px-5 py-8 text-sm text-gray-500 dark:text-gray-400">No fees in this range.</p>
                <div v-else class="max-w-full overflow-x-auto">
                    <table class="w-full text-sm">
                        <thead>
                            <tr
                                class="border-b border-gray-100 text-left text-xs font-medium tracking-wide text-gray-500 uppercase dark:border-gray-800 dark:text-gray-400"
                            >
                                <th class="px-4 py-3 font-medium">Day</th>
                                <th class="px-4 py-3 text-end font-medium">Fees</th>
                            </tr>
                        </thead>
                        <tbody>
                            <tr v-for="day in model.feeDays" :key="day.label" class="border-b border-gray-50 last:border-0 dark:border-gray-800/80">
                                <td class="px-4 py-3 text-gray-700 dark:text-gray-200">{{ day.label }}</td>
                                <td class="px-4 py-3 text-end"><MoneyCell :value="day.fees" :signed="false" :currency="model.currency" /></td>
                            </tr>
                        </tbody>
                    </table>
                </div>
            </section>

            <section
                data-testid="transfers"
                class="min-w-0 overflow-hidden rounded-2xl border border-gray-200 bg-white dark:border-gray-800 dark:bg-white/[0.03]"
            >
                <div class="px-4 py-4 md:px-5">
                    <h2 class="text-base font-semibold text-gray-800 dark:text-white/90">Transfers</h2>
                    <p class="mt-1 text-sm text-gray-500 dark:text-gray-400">Deposits, withdrawals, and sweeps in this range.</p>
                </div>
                <TransferList :transfers="model.transfers" />
            </section>

            <section
                data-testid="wallet-trades"
                class="min-w-0 overflow-hidden rounded-2xl border border-gray-200 bg-white dark:border-gray-800 dark:bg-white/[0.03]"
            >
                <div class="flex items-center justify-between gap-3 px-4 py-4 md:px-5">
                    <div>
                        <h2 class="text-base font-semibold text-gray-800 dark:text-white/90">Recent trades</h2>
                        <p class="mt-1 text-sm text-gray-500 dark:text-gray-400">Newest fills in this range. A dash means unrealized P&L is unknown.</p>
                    </div>
                    <RouterLink :to="shellLocation('/trades')" class="text-sm font-medium text-brand-600 hover:text-brand-700 dark:text-brand-400">
                        All trades
                    </RouterLink>
                </div>
                <TradeList :trades="model.trades" :missing-pnl="DASH" />
            </section>
        </template>
    </div>
</template>
