/** Lazy price chart. The trades drawer is the only caller, so the chart library stays out of the shell. */
export function loadPriceChart() {
    return import('@/components/PriceChart.vue')
}
