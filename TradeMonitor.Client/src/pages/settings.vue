<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import type { LiveProduct, TrackedSymbol } from '@/api/live'
import { explainLedgerError } from '@/composables/useLedger'
import { useLive } from '@/composables/useLive'
import { atrHorizons, isAtrHorizon } from '@/lib/atrBands'
import { forecastHorizons, isForecastHorizon } from '@/lib/forecast'
import { intervalPresets } from '@/lib/chartInterval'
import { readChartPrefs, rememberInterval, removeCustomInterval, writeChartPrefs, type ChartPrefs } from '@/lib/chartPrefs'
import { quickRanges } from '@/lib/chartRange'
import { chartTypes, type ChartType } from '@/lib/chartSeries'

const live = useLive()
const chart = ref<ChartPrefs>(readChartPrefs(localStorage))
const customInterval = ref('')
const intervalError = ref('')
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

function saveChart(next: ChartPrefs) {
    chart.value = next
    writeChartPrefs(localStorage, next)
}

function toggleType(id: ChartType) {
    const has = chart.value.favoriteTypes.includes(id)
    const favoriteTypes = has ? chart.value.favoriteTypes.filter((item) => item !== id) : [...chart.value.favoriteTypes, id]
    saveChart({ ...chart.value, favoriteTypes: favoriteTypes.length ? favoriteTypes : [id] })
}

function toggleInterval(key: string) {
    const has = chart.value.favoriteIntervals.includes(key)
    const favoriteIntervals = has ? chart.value.favoriteIntervals.filter((item) => item !== key) : [...chart.value.favoriteIntervals, key]
    saveChart({ ...chart.value, favoriteIntervals: favoriteIntervals.length ? favoriteIntervals : [key] })
}

function onHorizon(value: string) {
    if (!isAtrHorizon(value)) return
    saveChart({ ...chart.value, atrHorizon: value })
}

function onForecastHorizon(value: string) {
    if (!isForecastHorizon(value)) return
    saveChart({ ...chart.value, forecastHorizon: value })
}

function addCustom() {
    const next = rememberInterval(chart.value, customInterval.value)
    if (!next) {
        intervalError.value = 'Use a number and a unit, such as 7m or 4h.'
        return
    }
    intervalError.value = ''
    customInterval.value = ''
    saveChart(next)
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
        <section data-testid="chart-settings" class="space-y-4">
            <div>
                <h2 class="text-sm font-semibold text-gray-900 dark:text-white">Chart</h2>
                <p class="mt-1 text-sm text-gray-500">Defaults for the live chart on this browser. Favorites stay one click away in the header.</p>
            </div>
            <div class="grid gap-3 sm:grid-cols-2">
                <label class="text-xs font-medium text-gray-500">
                    Default type
                    <select data-testid="chart-default-type" class="mt-1 w-full rounded-lg border border-gray-200 bg-white px-2 py-1.5 text-sm dark:border-gray-700 dark:bg-gray-950" :value="chart.type" @change="saveChart({ ...chart, type: ($event.target as HTMLSelectElement).value as ChartType })">
                        <option v-for="item in chartTypes" :key="item.id" :value="item.id">{{ item.label }}</option>
                    </select>
                </label>
                <label class="text-xs font-medium text-gray-500">
                    Default interval
                    <select data-testid="chart-default-interval" class="mt-1 w-full rounded-lg border border-gray-200 bg-white px-2 py-1.5 text-sm dark:border-gray-700 dark:bg-gray-950" :value="chart.interval" @change="saveChart({ ...chart, interval: ($event.target as HTMLSelectElement).value })">
                        <option v-for="item in [...intervalPresets, ...chart.customIntervals]" :key="item" :value="item">{{ item }}</option>
                    </select>
                </label>
                <label class="text-xs font-medium text-gray-500">
                    Default range
                    <select data-testid="chart-default-range" class="mt-1 w-full rounded-lg border border-gray-200 bg-white px-2 py-1.5 text-sm dark:border-gray-700 dark:bg-gray-950" :value="chart.range === 'custom' ? 'All' : chart.range" @change="saveChart({ ...chart, range: ($event.target as HTMLSelectElement).value as ChartPrefs['range'] })">
                        <option v-for="item in quickRanges" :key="item" :value="item">{{ item }}</option>
                    </select>
                </label>
                <label class="text-xs font-medium text-gray-500">
                    Time zone
                    <select data-testid="chart-timezone" class="mt-1 w-full rounded-lg border border-gray-200 bg-white px-2 py-1.5 text-sm dark:border-gray-700 dark:bg-gray-950" :value="chart.timeZone" @change="saveChart({ ...chart, timeZone: ($event.target as HTMLSelectElement).value === 'utc' ? 'utc' : 'local' })">
                        <option value="local">Local time</option>
                        <option value="utc">UTC</option>
                    </select>
                </label>
            </div>
            <fieldset>
                <legend class="text-xs font-medium text-gray-500">Favorite chart types</legend>
                <div class="mt-2 flex flex-wrap gap-2">
                    <label v-for="item in chartTypes" :key="item.id" class="flex items-center gap-1 text-sm text-gray-700 dark:text-gray-200">
                        <input type="checkbox" :checked="chart.favoriteTypes.includes(item.id)" @change="toggleType(item.id)" />
                        {{ item.label }}
                    </label>
                </div>
            </fieldset>
            <fieldset>
                <legend class="text-xs font-medium text-gray-500">Favorite intervals</legend>
                <div class="mt-2 flex flex-wrap gap-2">
                    <label v-for="item in intervalPresets" :key="item" class="flex items-center gap-1 text-sm text-gray-700 dark:text-gray-200">
                        <input type="checkbox" :checked="chart.favoriteIntervals.includes(item)" @change="toggleInterval(item)" />
                        {{ item }}
                    </label>
                </div>
            </fieldset>
            <div class="flex flex-wrap items-center gap-4">
                <label class="flex items-center gap-2 text-sm text-gray-700 dark:text-gray-200">
                    <input data-testid="chart-swing" type="checkbox" :checked="chart.showSwing" @change="saveChart({ ...chart, showSwing: !chart.showSwing })" />
                    Targets: Swing
                </label>
                <label class="flex items-center gap-2 text-sm text-gray-700 dark:text-gray-200">
                    <input data-testid="chart-atr" type="checkbox" :checked="chart.showAtr" @change="saveChart({ ...chart, showAtr: !chart.showAtr })" />
                    Targets: ATR
                </label>
                <label class="text-xs font-medium text-gray-500">
                    ATR horizon
                    <select data-testid="chart-atr-horizon" class="ms-2 rounded-lg border border-gray-200 bg-white px-2 py-1.5 text-sm dark:border-gray-700 dark:bg-gray-950" :value="chart.atrHorizon" @change="onHorizon(($event.target as HTMLSelectElement).value)">
                        <option v-for="item in atrHorizons" :key="item.id" :value="item.id">{{ item.label }}</option>
                    </select>
                </label>
                <label class="flex items-center gap-2 text-sm text-gray-700 dark:text-gray-200">
                    <input data-testid="chart-forecast" type="checkbox" :checked="chart.showForecast" @change="saveChart({ ...chart, showForecast: !chart.showForecast })" />
                    Targets: Forecast
                </label>
                <label class="text-xs font-medium text-gray-500">
                    Forecast horizon
                    <select data-testid="chart-forecast-horizon" class="ms-2 rounded-lg border border-gray-200 bg-white px-2 py-1.5 text-sm dark:border-gray-700 dark:bg-gray-950" :value="chart.forecastHorizon" @change="onForecastHorizon(($event.target as HTMLSelectElement).value)">
                        <option v-for="item in forecastHorizons" :key="item.id" :value="item.id">{{ item.label }}</option>
                    </select>
                </label>
            </div>
            <form class="flex flex-wrap items-center gap-2" @submit.prevent="addCustom">
                <input v-model="customInterval" data-testid="settings-custom-interval" class="w-28 rounded-lg border border-gray-200 px-2 py-1.5 text-sm dark:border-gray-700 dark:bg-gray-950" placeholder="7m" />
                <button type="submit" class="rounded-lg px-3 py-1.5 text-sm text-gray-700 dark:text-gray-200">Save interval</button>
                <button v-for="item in chart.customIntervals" :key="item" type="button" class="text-sm text-error-500" @click="saveChart(removeCustomInterval(chart, item))">Remove {{ item }}</button>
            </form>
            <p v-if="intervalError" class="text-sm text-error-500">{{ intervalError }}</p>
        </section>
    </div>
</template>
