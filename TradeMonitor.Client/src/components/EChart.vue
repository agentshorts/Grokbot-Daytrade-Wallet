<script setup lang="ts">
// vue-echarts 8 with ECharts 6. Only the chart types this shell draws are registered.
import { BarChart, LineChart } from 'echarts/charts'
import { DataZoomComponent, GridComponent, LegendComponent, TooltipComponent } from 'echarts/components'
import { use } from 'echarts/core'
import { CanvasRenderer } from 'echarts/renderers'
import { computed, nextTick, ref, watch } from 'vue'
import VChart from 'vue-echarts'
import type { EChartsOption } from 'echarts'
import { useTheme } from '@/composables/useTheme'

use([CanvasRenderer, LineChart, BarChart, GridComponent, TooltipComponent, LegendComponent, DataZoomComponent])

const props = withDefaults(
    defineProps<{
        option?: Record<string, unknown> | null
        loading?: boolean
        error?: string | null
        empty?: boolean
        emptyLabel?: string
    }>(),
    { emptyLabel: 'Nothing recorded in this range' },
)

const chartOption = computed(() => (props.option ?? {}) as EChartsOption)
const updateOptions = { notMerge: true }
const chartRef = ref<{ setOption: (option: EChartsOption, opts?: { notMerge?: boolean }) => void; resize: () => void } | null>(null)
const { isDark } = useTheme()

watch(isDark, () => {
    const paint = () => {
        if (!props.option) return
        chartRef.value?.setOption(chartOption.value, updateOptions)
        chartRef.value?.resize()
    }
    void nextTick(() => requestAnimationFrame(() => requestAnimationFrame(paint)))
})
</script>

<template>
    <div v-if="loading" class="h-[280px] animate-pulse rounded-xl bg-gray-100 dark:bg-gray-800" role="status">
        <span class="sr-only">Loading chart</span>
    </div>
    <p v-else-if="error" class="flex h-[280px] items-center justify-center text-sm text-error-600 dark:text-error-400" role="alert">
        {{ error }}
    </p>
    <p v-else-if="empty || !option" class="flex h-[280px] items-center justify-center px-4 text-center text-sm text-gray-500 dark:text-gray-400">
        {{ emptyLabel }}
    </p>
    <div v-else class="h-[280px] w-full min-w-0" style="height: 280px">
        <VChart ref="chartRef" class="h-full w-full" :option="chartOption" :update-options="updateOptions" autoresize />
    </div>
</template>
