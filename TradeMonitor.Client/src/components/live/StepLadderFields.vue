<script setup lang="ts">
import { ref, watch } from 'vue'
import { readStepLadderInputs, type StepLadderInputs } from '@/lib/stepLadder'

const props = defineProps<{
    stepIncrease: number
    dormantThreshold: number
    dormantPeriod: number
}>()

const emit = defineEmits<{
    change: [value: StepLadderInputs]
}>()

const step = ref(String(props.stepIncrease))
const threshold = ref(String(props.dormantThreshold))
const period = ref(String(props.dormantPeriod))
const error = ref('')

watch(
    () => [props.stepIncrease, props.dormantThreshold, props.dormantPeriod],
    () => {
        step.value = String(props.stepIncrease)
        threshold.value = String(props.dormantThreshold)
        period.value = String(props.dormantPeriod)
    },
)

function commit() {
    const next = readStepLadderInputs(step.value, threshold.value, period.value)
    if (!next.ok) {
        error.value = next.error
        return
    }
    error.value = ''
    emit('change', next.value)
}
</script>

<template>
    <div class="space-y-2" data-testid="step-ladder-fields">
        <label class="block text-xs font-medium text-gray-500">
            Step increase
            <input v-model="step" data-testid="step-increase" class="mt-1 w-full rounded-md border border-gray-200 bg-white px-2 py-1 text-sm dark:border-gray-700 dark:bg-gray-950" type="number" min="0" step="any" @change="commit" />
        </label>
        <label class="block text-xs font-medium text-gray-500">
            Dormant threshold
            <input v-model="threshold" data-testid="dormant-threshold" class="mt-1 w-full rounded-md border border-gray-200 bg-white px-2 py-1 text-sm dark:border-gray-700 dark:bg-gray-950" type="number" min="0" step="any" @change="commit" />
        </label>
        <label class="block text-xs font-medium text-gray-500">
            Dormant period
            <input v-model="period" data-testid="dormant-period" class="mt-1 w-full rounded-md border border-gray-200 bg-white px-2 py-1 text-sm dark:border-gray-700 dark:bg-gray-950" type="number" min="1" step="1" @change="commit" />
        </label>
        <p v-if="error" data-testid="step-ladder-error" class="text-xs text-error-500">{{ error }}</p>
    </div>
</template>
