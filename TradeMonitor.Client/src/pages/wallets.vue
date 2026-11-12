<script setup lang="ts">
import { useRouter } from 'vue-router'
import StatePanel from '@/components/shell/StatePanel.vue'
import WalletCard from '@/components/WalletCard.vue'
import { useFilterQuery } from '@/composables/useFilterQuery'
import { useWalletList } from '@/composables/useWalletPages'

const router = useRouter()
const { filters, setWallet, shellLocation } = useFilterQuery()
const { status, error, errorStatus, model, refresh } = useWalletList()

function selectWallet(key: string) {
    void router.push(shellLocation(`/wallet/${encodeURIComponent(key)}`, { wallet: key }))
}
</script>

<template>
    <div data-testid="wallets" class="space-y-6">
        <div>
            <h1 class="text-xl font-semibold text-gray-900 dark:text-white/90">Wallets</h1>
            <p class="mt-1 text-sm text-gray-500 dark:text-gray-400">Agent-defined wallets. Open one to keep it in the link.</p>
        </div>

        <p
            v-if="error && model"
            class="rounded-xl border border-error-200 bg-error-25 px-4 py-3 text-sm text-error-700 dark:border-error-800 dark:bg-error-500/10 dark:text-error-300"
            role="alert"
        >
            {{ error }}
            <button type="button" class="ms-2 font-medium underline" @click="refresh()">Try again</button>
        </p>

        <StatePanel v-if="status === 'loading' && !model" tone="loading" title="Loading wallets" />
        <StatePanel v-else-if="status === 'error' && !model" tone="error" title="Could not load wallets" :body="error || 'Try again in a moment.'">
            <RouterLink
                v-if="errorStatus === 401 || errorStatus === 403"
                to="/signin"
                class="rounded-lg bg-brand-500 px-3 py-1.5 text-sm font-medium text-white"
            >
                Sign in
            </RouterLink>
            <button v-else type="button" class="rounded-lg bg-brand-500 px-3 py-1.5 text-sm font-medium text-white" @click="refresh()">Try again</button>
        </StatePanel>
        <StatePanel v-else-if="model?.emptyBook" title="No wallets yet" body="The agent has not recorded a wallet." />
        <StatePanel v-else-if="model?.noMatches" title="Nothing in this view" body="No wallet matches the current mode and wallet filter.">
            <button
                v-if="filters.wallet"
                type="button"
                class="rounded-lg border border-gray-200 px-3 py-1.5 text-sm font-medium dark:border-gray-700"
                @click="setWallet(null)"
            >
                Clear wallet
            </button>
        </StatePanel>
        <section v-else-if="model" class="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
            <WalletCard
                v-for="wallet in model.wallets"
                :key="wallet.key"
                :wallet="wallet"
                :selected="filters.wallet === wallet.key"
                @select="selectWallet(wallet.key)"
            />
        </section>
    </div>
</template>
