<script setup lang="ts">
import { computed, ref } from 'vue'
import type { LiveProduct } from '@/api/live'

const props = defineProps<{ products: LiveProduct[]; product: string }>()
const emit = defineEmits<{ select: [id: string] }>()

const query = ref('')
const open = ref(false)

const shown = computed(() => {
    const needle = query.value.trim().toLowerCase()
    return props.products
        .filter((item) => !needle || item.productId.toLowerCase().includes(needle) || item.name.toLowerCase().includes(needle))
        .slice(0, 50)
})

function choose(id: string) {
    emit('select', id)
    open.value = false
    query.value = ''
}

function onEnter() {
    const first = shown.value[0]
    if (first) choose(first.productId)
}
</script>

<template>
    <div class="relative min-w-0">
        <button
            type="button"
            data-testid="product-picker"
            class="inline-flex max-w-full items-center gap-2 rounded-lg border border-gray-200 bg-white px-3 py-1.5 text-sm font-semibold text-gray-900 dark:border-gray-700 dark:bg-gray-900 dark:text-white"
            @click="open = !open"
        >
            <span class="truncate">{{ product || 'Choose a product' }}</span>
            <span aria-hidden="true" class="text-gray-400">▾</span>
        </button>
        <div
            v-if="open"
            class="absolute start-0 z-40 mt-1 w-72 rounded-xl border border-gray-200 bg-white p-2 shadow-lg dark:border-gray-700 dark:bg-gray-900"
        >
            <input
                v-model="query"
                data-testid="product-search"
                class="w-full rounded-lg border border-gray-200 px-2 py-1.5 text-sm dark:border-gray-700 dark:bg-gray-950"
                placeholder="Search USD products"
                @keydown.enter.prevent="onEnter"
            />
            <ul class="mt-1 max-h-64 overflow-y-auto">
                <li v-for="item in shown" :key="item.productId">
                    <button
                        type="button"
                        class="flex w-full items-baseline justify-between gap-3 rounded-lg px-2 py-1.5 text-start text-sm hover:bg-gray-50 dark:hover:bg-white/5"
                        @click="choose(item.productId)"
                    >
                        <span class="font-medium text-gray-900 dark:text-white">{{ item.productId }}</span>
                        <span class="truncate text-gray-500">{{ item.name }}</span>
                    </button>
                </li>
                <li v-if="shown.length === 0" class="px-2 py-2 text-sm text-gray-500">No matching USD product.</li>
            </ul>
        </div>
    </div>
</template>
