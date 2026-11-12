<script setup lang="ts">
import MoneyCell from '@/components/MoneyCell.vue'
import PctCell from '@/components/PctCell.vue'
import Sparkline from '@/components/Sparkline.vue'
import TimeCell from '@/components/TimeCell.vue'
import type { WalletSummary } from '@/lib/book'
import { liveHref } from '@/lib/liveQuery'

defineProps<{
    wallet?: WalletSummary | null
    selected?: boolean
    loading?: boolean
    error?: string | null
    empty?: boolean
}>()

const emit = defineEmits<{ select: [] }>()
</script>

<template>
    <div v-if="loading" class="h-48 animate-pulse rounded-2xl border border-gray-200 bg-gray-100 dark:border-gray-800 dark:bg-gray-800" role="status">
        <span class="sr-only">Loading wallet</span>
    </div>
    <div
        v-else-if="error"
        class="rounded-2xl border border-error-200 bg-error-25 p-5 text-sm text-error-700 dark:border-error-800 dark:bg-error-500/10 dark:text-error-300"
        role="alert"
    >
        {{ error }}
    </div>
    <div
        v-else-if="empty || !wallet"
        class="rounded-2xl border border-dashed border-gray-300 bg-white p-5 text-sm text-gray-500 dark:border-gray-700 dark:bg-white/[0.03] dark:text-gray-400"
    >
        No wallets yet
    </div>
    <article
        v-else
        tabindex="0"
        class="min-w-0 cursor-pointer rounded-2xl border bg-white p-5 text-start transition-shadow focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-500 dark:bg-white/[0.03]"
        :class="
            selected
                ? 'border-brand-300 shadow-theme-sm ring-2 ring-brand-500/30 dark:border-brand-700'
                : 'border-gray-200 hover:shadow-theme-sm dark:border-gray-800'
        "
        :aria-pressed="selected"
        :data-testid="wallet ? `wallet-${wallet.key}` : undefined"
        @click="emit('select')"
        @keydown.enter.self.prevent="emit('select')"
    >
        <div class="flex items-start justify-between gap-3">
            <div class="min-w-0">
                <p
                    v-if="wallet.venue && wallet.venue.toLowerCase() !== wallet.name.toLowerCase()"
                    class="truncate text-sm font-medium text-gray-500 dark:text-gray-400"
                >
                    {{ wallet.venue }}
                </p>
                <h3 class="truncate text-base font-semibold text-gray-900 dark:text-white">
                    <a
                        :href="liveHref(wallet.key, wallet.liveSymbol)"
                        target="_blank"
                        rel="noopener"
                        class="hover:text-brand-600 dark:hover:text-brand-300"
                        data-testid="wallet-live-name"
                        @click.stop
                    >
                        {{ wallet.name }}
                    </a>
                </h3>
            </div>
            <span
                class="shrink-0 rounded-full px-2.5 py-0.5 text-xs font-medium"
                :class="
                    wallet.mode === 'real'
                        ? 'bg-success-50 text-success-700 dark:bg-success-500/15 dark:text-success-400'
                        : 'bg-brand-50 text-brand-700 dark:bg-brand-500/15 dark:text-brand-300'
                "
            >
                {{ wallet.mode === 'real' ? 'Real' : 'Paper' }}
            </span>
        </div>
        <a
            :href="liveHref(wallet.key, wallet.liveSymbol)"
            target="_blank"
            rel="noopener"
            data-testid="open-in-live"
            class="mt-3 inline-flex items-center gap-1 rounded-lg border border-gray-200 px-2 py-1 text-xs font-medium text-gray-700 hover:border-brand-300 hover:text-brand-600 dark:border-gray-700 dark:text-gray-200 dark:hover:text-brand-300"
            @click.stop
        >
            <svg viewBox="0 0 16 16" class="size-3.5" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="1.5">
                <path d="M6 3.5H3.5v9h9V10" />
                <path d="M8.5 3.5H12.5V7.5" />
                <path d="M12.5 3.5 7 9" />
            </svg>
            Open in Live
        </a>
        <span
            v-if="wallet.isArchived"
            class="mt-2 inline-flex rounded-full bg-gray-100 px-2 py-0.5 text-xs font-medium text-gray-600 dark:bg-white/10 dark:text-gray-300"
        >
            Archived
        </span>
        <div class="mt-4 flex items-end justify-between gap-3">
            <MoneyCell class="text-lg font-semibold" :value="wallet.value" :signed="false" :currency="wallet.currency" />
            <PctCell :value="wallet.changePct" />
        </div>
        <p class="mt-1 text-xs text-gray-500 dark:text-gray-400">
            <MoneyCell :value="wallet.change" :currency="wallet.currency" />
            over this range
        </p>
        <div class="mt-3">
            <Sparkline :values="wallet.sparkline" :positive="wallet.change >= 0" />
        </div>
        <div class="mt-3 flex items-center justify-between gap-2 text-xs text-gray-500 dark:text-gray-400">
            <span>{{ wallet.openPositions }} open</span>
            <span class="flex items-center gap-2">
                <span v-if="wallet.unmarked" class="text-warning-600 dark:text-warning-400">No price mark</span>
                <TimeCell :value="wallet.lastActivityAt" />
            </span>
        </div>
    </article>
</template>
