<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import type { LiveProduct, TrackedSymbol } from '@/api/live'
import { explainLedgerError } from '@/composables/useLedger'
import { useLive } from '@/composables/useLive'

const live = useLive()
const rows = ref<TrackedSymbol[]>([])
const catalog = ref<LiveProduct[]>([])
const query = ref('')
const error = ref('')
const open = ref(false)

const taken = computed(() => new Set(rows.value.map((row) => row.productId)))
const matches = computed(() => {
    const needle = query.value.trim().toLowerCase()
    return catalog.value
        .filter((item) => !taken.value.has(item.productId))
        .filter((item) => !needle || item.productId.toLowerCase().includes(needle) || item.name.toLowerCase().includes(needle))
        .slice(0, 50)
})

function fail(err: unknown) {
    error.value = explainLedgerError(err).message
}

async function load() {
    const [tracked, products] = await Promise.all([live.tracked(), live.products()])
    rows.value = tracked.results
    catalog.value = products
    error.value = ''
}

async function add(productId: string) {
    try {
        rows.value = (await live.addSymbol(productId)).results
        query.value = ''
        open.value = false
        error.value = ''
    } catch (err) {
        fail(err)
    }
}

async function remove(productId: string) {
    try {
        rows.value = (await live.removeSymbol(productId)).results
        error.value = ''
    } catch (err) {
        fail(err)
    }
}

async function move(index: number, delta: number) {
    const next = index + delta
    if (next < 0 || next >= rows.value.length) return
    const products = rows.value.map((row) => row.productId)
    const [item] = products.splice(index, 1)
    if (!item) return
    products.splice(next, 0, item)
    try {
        rows.value = (await live.reorderSymbols(products)).results
        error.value = ''
    } catch (err) {
        fail(err)
    }
}

onMounted(() => {
    void load().catch(fail)
})
</script>

<template>
    <div data-testid="settings-page" class="mx-auto max-w-xl space-y-6">
        <div>
            <h1 class="text-xl font-semibold text-gray-900 dark:text-white/90">Settings</h1>
            <p class="mt-1 text-sm text-gray-500 dark:text-gray-400">Symbols on the live chart, in the order they appear. The feed follows this list, plus any symbol with an open paper order or a held position.</p>
        </div>
        <p v-if="error" class="rounded-xl border border-error-200 bg-error-25 px-4 py-3 text-sm text-error-700" role="alert">{{ error }}</p>
        <div class="relative">
            <label class="block text-xs font-medium text-gray-500" for="symbol-search">Add a Coinbase USD product</label>
            <input
                id="symbol-search"
                v-model="query"
                data-testid="settings-search"
                class="mt-1 w-full rounded-lg border border-gray-200 bg-white px-3 py-2 text-sm dark:border-gray-700 dark:bg-gray-950"
                placeholder="Search USD products"
                @focus="open = true"
                @keydown.enter.prevent="matches[0] && add(matches[0].productId)"
            />
            <ul
                v-if="open || query.trim()"
                data-testid="settings-matches"
                class="absolute z-20 mt-1 max-h-64 w-full overflow-y-auto rounded-xl border border-gray-200 bg-white p-1 shadow-lg dark:border-gray-700 dark:bg-gray-900"
            >
                <li v-for="item in matches" :key="item.productId">
                    <button
                        type="button"
                        data-testid="settings-add"
                        class="flex w-full items-baseline justify-between gap-3 rounded-lg px-2 py-1.5 text-start text-sm hover:bg-gray-50 dark:hover:bg-white/5"
                        @click="add(item.productId)"
                    >
                        <span class="font-medium text-gray-900 dark:text-white">{{ item.productId }}</span>
                        <span class="truncate text-gray-500">{{ item.name }}</span>
                    </button>
                </li>
                <li v-if="matches.length === 0" class="px-2 py-2 text-sm text-gray-500">No matching USD product.</li>
            </ul>
        </div>
        <ol data-testid="settings-symbols" class="divide-y divide-gray-200 rounded-xl border border-gray-200 dark:divide-gray-800 dark:border-gray-800">
            <li v-for="(row, index) in rows" :key="row.productId" class="flex items-center gap-2 px-3 py-2" :data-product="row.productId">
                <span class="min-w-0 flex-1">
                    <span class="font-medium text-gray-900 dark:text-white">{{ row.productId }}</span>
                    <span v-if="!row.listed" class="ms-2 text-xs text-gray-500">Not on Coinbase</span>
                </span>
                <button type="button" data-testid="settings-up" class="rounded-md px-2 py-1 text-sm text-gray-500 disabled:opacity-30" :disabled="index === 0" @click="move(index, -1)">Up</button>
                <button type="button" data-testid="settings-down" class="rounded-md px-2 py-1 text-sm text-gray-500 disabled:opacity-30" :disabled="index === rows.length - 1" @click="move(index, 1)">Down</button>
                <button type="button" data-testid="settings-remove" class="rounded-md px-2 py-1 text-sm text-error-500" @click="remove(row.productId)">Remove</button>
            </li>
            <li v-if="rows.length === 0" class="px-3 py-4 text-sm text-gray-500">No symbols yet.</li>
        </ol>
    </div>
</template>
