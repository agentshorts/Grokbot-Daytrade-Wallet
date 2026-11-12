<script setup lang="ts">
import PositionList from '@/components/PositionList.vue'
import StatePanel from '@/components/shell/StatePanel.vue'
import WalletCard from '@/components/WalletCard.vue'
import { useDashboard } from '@/composables/useDashboard'
import { useFilterQuery } from '@/composables/useFilterQuery'

const { view, status, error, refresh } = useDashboard()
const { filters, setWallet } = useFilterQuery()

function selectWallet(key: string) {
    void setWallet(filters.wallet === key ? null : key)
}
</script>

<template>
    <div data-testid="wallets" class="space-y-6">
        <div>
            <h1 class="text-xl font-semibold text-gray-900 dark:text-white/90">Wallets</h1>
            <p class="mt-1 text-sm text-gray-500 dark:text-gray-400">Agent-defined wallets. Select one to keep it in the link.</p>
        </div>

        <StatePanel v-if="status === 'loading' && !view" tone="loading" title="Loading wallets" />
        <StatePanel v-else-if="status === 'error' && !view" tone="error" title="Could not load wallets" :body="error || 'Try again in a moment.'">
            <button type="button" class="rounded-lg bg-brand-500 px-3 py-1.5 text-sm font-medium text-white" @click="refresh()">Try again</button>
        </StatePanel>
        <StatePanel v-else-if="view?.emptyBook" title="No wallets yet" body="The agent has not recorded a wallet." />
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
            <section class="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
                <WalletCard
                    v-for="wallet in view.wallets"
                    :key="wallet.key"
                    :wallet="wallet"
                    :selected="filters.wallet === wallet.key"
                    @select="selectWallet(wallet.key)"
                />
            </section>
            <section class="min-w-0 overflow-hidden rounded-2xl border border-gray-200 bg-white dark:border-gray-800 dark:bg-white/[0.03]">
                <div class="px-4 py-4 md:px-5">
                    <h2 class="text-base font-semibold text-gray-800 dark:text-white/90">Open positions</h2>
                    <p class="mt-1 text-sm text-gray-500 dark:text-gray-400">A position with no price mark is flagged and left out of exposure.</p>
                </div>
                <PositionList :positions="view.positions" />
            </section>
        </template>
    </div>
</template>
