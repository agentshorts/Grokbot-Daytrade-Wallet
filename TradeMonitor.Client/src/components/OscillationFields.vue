<script setup lang="ts">
import { lookbackChoices } from '@/lib/oscillationPrefs'

const props = defineProps<{ lookbackHours: number; thresholdCents: number }>()
const emit = defineEmits<{ change: [value: { lookbackHours: number; thresholdCents: number }] }>()

function onLookback(event: Event) {
    const hours = Number((event.target as HTMLSelectElement).value)
    if (!Number.isFinite(hours)) return
    emit('change', { lookbackHours: hours, thresholdCents: props.thresholdCents })
}

function onThreshold(event: Event) {
    const cents = Number((event.target as HTMLInputElement).value)
    if (!Number.isFinite(cents) || cents <= 0 || cents > 50) return
    emit('change', {
        lookbackHours: props.lookbackHours,
        thresholdCents: Math.round(cents * 10) / 10,
    })
}
</script>

<template>
    <div class="grid gap-3" data-testid="oscillation-fields">
        <label class="text-xs font-medium text-gray-500">
            Lookback
            <select
                data-testid="oscillation-lookback"
                class="mt-1 w-full rounded-lg border border-gray-200 bg-white px-2 py-1.5 text-sm dark:border-gray-700 dark:bg-gray-950"
                :value="String(lookbackHours)"
                @change="onLookback"
            >
                <option v-for="item in lookbackChoices" :key="item.hours" :value="String(item.hours)">
                    {{ item.label }}
                </option>
            </select>
        </label>
        <label class="text-xs font-medium text-gray-500">
            Reversal threshold
            <span class="mt-1 flex items-center gap-2">
                <input
                    data-testid="oscillation-threshold"
                    class="w-full rounded-lg border border-gray-200 bg-white px-2 py-1.5 text-sm dark:border-gray-700 dark:bg-gray-950"
                    type="number"
                    min="0.5"
                    max="50"
                    step="0.5"
                    :value="thresholdCents"
                    @change="onThreshold"
                />
                <span>c</span>
            </span>
        </label>
    </div>
</template>
