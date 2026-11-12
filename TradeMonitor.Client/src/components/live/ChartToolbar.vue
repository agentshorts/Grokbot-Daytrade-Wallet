<script setup lang="ts">
import { ref } from 'vue'
import { intervalPresets, parseInterval } from '@/lib/chartInterval'
import { quickRanges, type ChartRange } from '@/lib/chartRange'
import { chartTypes, type BaselineSource, type ChartType, type DeltaMode } from '@/lib/chartSeries'

const props = defineProps<{
    chartType: ChartType
    favoriteTypes: ChartType[]
    interval: string
    favoriteIntervals: string[]
    customIntervals: string[]
    range: ChartRange
    fromInput: string
    toInput: string
    zoneLabel: string
    showVolume: boolean
    baselineSource: BaselineSource
    baselinePrice: number | null
    deltaMode: DeltaMode
    hint: string
    showBaseline: boolean
    showSwing: boolean
    swingNotice: string
}>()

const emit = defineEmits<{
    type: [value: ChartType]
    interval: [value: string]
    'add-interval': [value: string]
    'remove-interval': [value: string]
    range: [value: ChartRange]
    from: [value: string]
    to: [value: string]
    zone: []
    volume: []
    'baseline-source': [value: BaselineSource]
    'baseline-price': [value: number]
    'delta-mode': [value: DeltaMode]
    swing: []
}>()

const custom = ref('')
const intervalError = ref('')
const favorites = () => chartTypes.filter((item) => props.favoriteTypes.includes(item.id))
const presetKeys: readonly string[] = intervalPresets
const choices = () => [...intervalPresets, ...props.customIntervals.filter((item) => !presetKeys.includes(item))]

function addCustom() {
    const parsed = parseInterval(custom.value)
    if (!parsed) {
        intervalError.value = 'Use a number and a unit, such as 7m or 4h.'
        return
    }
    intervalError.value = ''
    custom.value = ''
    emit('add-interval', parsed.key)
}
</script>

<template>
    <div data-testid="chart-toolbar" class="flex flex-wrap items-center gap-2 border-b border-gray-200 px-3 py-2 dark:border-gray-800" @click.stop>
        <div class="flex flex-wrap items-center gap-1">
            <button
                v-for="item in favorites()"
                :key="item.id"
                type="button"
                :data-testid="`chart-type-${item.id}`"
                class="rounded-md px-2 py-1 text-xs font-medium"
                :class="item.id === chartType ? 'bg-brand-500 text-white' : 'text-gray-600 hover:bg-gray-100 dark:text-gray-300 dark:hover:bg-white/5'"
                :aria-pressed="item.id === chartType"
                @click="emit('type', item.id)"
            >
                {{ item.short }}
            </button>
            <select data-testid="chart-type" class="rounded-md border border-gray-200 bg-white px-2 py-1 text-xs dark:border-gray-700 dark:bg-gray-950" :value="chartType" @change="emit('type', ($event.target as HTMLSelectElement).value as ChartType)">
                <option v-for="item in chartTypes" :key="item.id" :value="item.id">{{ item.label }}</option>
            </select>
        </div>
        <div class="flex flex-wrap items-center gap-1">
            <button
                v-for="item in favoriteIntervals"
                :key="item"
                type="button"
                :data-testid="`interval-${item}`"
                class="rounded-md px-2 py-1 text-xs font-medium"
                :class="item === interval ? 'bg-gray-900 text-white dark:bg-white dark:text-gray-900' : 'text-gray-600 hover:bg-gray-100 dark:text-gray-300 dark:hover:bg-white/5'"
                @click="emit('interval', item)"
            >
                {{ item }}
            </button>
            <select data-testid="interval-select" class="rounded-md border border-gray-200 bg-white px-2 py-1 text-xs dark:border-gray-700 dark:bg-gray-950" :value="interval" @change="emit('interval', ($event.target as HTMLSelectElement).value)">
                <option v-for="item in choices()" :key="item" :value="item">{{ item }}</option>
            </select>
            <form class="flex items-center gap-1" @submit.prevent="addCustom">
                <input v-model="custom" data-testid="custom-interval" class="w-16 rounded-md border border-gray-200 px-2 py-1 text-xs dark:border-gray-700 dark:bg-gray-950" placeholder="7m" />
                <button type="submit" data-testid="custom-interval-add" class="rounded-md px-2 py-1 text-xs text-gray-600">Add</button>
            </form>
            <button v-for="item in customIntervals" :key="`drop-${item}`" type="button" class="text-xs text-gray-400" :data-testid="`remove-interval-${item}`" @click="emit('remove-interval', item)">Remove {{ item }}</button>
        </div>
        <div class="flex flex-wrap items-center gap-1">
            <button
                v-for="item in quickRanges"
                :key="item"
                type="button"
                :data-testid="`range-${item}`"
                class="rounded-md px-2 py-1 text-xs font-medium"
                :class="item === range ? 'bg-gray-900 text-white dark:bg-white dark:text-gray-900' : 'text-gray-600 hover:bg-gray-100 dark:text-gray-300 dark:hover:bg-white/5'"
                @click="emit('range', item)"
            >
                {{ item }}
            </button>
            <input data-testid="range-from" class="rounded-md border border-gray-200 px-2 py-1 text-xs dark:border-gray-700 dark:bg-gray-950" type="datetime-local" :value="fromInput" @change="emit('from', ($event.target as HTMLInputElement).value)" />
            <input data-testid="range-to" class="rounded-md border border-gray-200 px-2 py-1 text-xs dark:border-gray-700 dark:bg-gray-950" type="datetime-local" :value="toInput" @change="emit('to', ($event.target as HTMLInputElement).value)" />
        </div>
        <button type="button" data-testid="chart-zone" class="rounded-md px-2 py-1 text-xs font-medium text-gray-600" @click="emit('zone')">{{ zoneLabel }}</button>
        <button type="button" data-testid="chart-volume" class="rounded-md px-2 py-1 text-xs font-medium" :class="showVolume ? 'bg-gray-900 text-white dark:bg-white dark:text-gray-900' : 'text-gray-600'" :aria-pressed="showVolume" @click="emit('volume')">Volume</button>
        <button type="button" data-testid="swing-toggle" class="rounded-md px-2 py-1 text-xs font-medium" :class="showSwing ? 'bg-gray-900 text-white dark:bg-white dark:text-gray-900' : 'text-gray-600'" :aria-pressed="showSwing" @click="emit('swing')">Targets: Swing</button>
        <p v-if="showSwing && swingNotice" data-testid="swing-notice" class="text-xs text-gray-500">{{ swingNotice }}</p>
        <div v-if="showBaseline" class="flex items-center gap-1">
            <select data-testid="baseline-source" class="rounded-md border border-gray-200 bg-white px-2 py-1 text-xs dark:border-gray-700 dark:bg-gray-950" :value="baselineSource" @change="emit('baseline-source', ($event.target as HTMLSelectElement).value as BaselineSource)">
                <option value="first">First close</option>
                <option value="average">Average cost</option>
                <option value="typed">Typed price</option>
            </select>
            <input
                v-if="baselineSource === 'typed'"
                data-testid="baseline-price"
                class="w-24 rounded-md border border-gray-200 px-2 py-1 text-xs dark:border-gray-700 dark:bg-gray-950"
                type="number"
                min="0"
                step="any"
                :value="baselinePrice ?? ''"
                @change="emit('baseline-price', Number(($event.target as HTMLInputElement).value))"
            />
        </div>
        <button v-if="chartType === 'delta' || chartType === 'vertex-baseline'" type="button" data-testid="delta-mode" class="rounded-md px-2 py-1 text-xs text-gray-600" @click="emit('delta-mode', deltaMode === 'change' ? 'percent' : 'change')">
            {{ deltaMode === 'change' ? 'Price change' : 'Percent from start' }}
        </button>
        <p v-if="intervalError" data-testid="interval-error" class="text-xs text-error-500">{{ intervalError }}</p>
        <p v-if="hint" data-testid="chart-hint" class="text-xs text-gray-500">{{ hint }}</p>
    </div>
</template>
