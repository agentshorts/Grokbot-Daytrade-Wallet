<script setup lang="ts">
import MoneyCell from '@/components/MoneyCell.vue'
import TimeCell from '@/components/TimeCell.vue'
import type { TransferView } from '@/lib/walletView'

defineProps<{ transfers: TransferView[] }>()

function label(kind: TransferView['kind']) {
    if (kind === 'deposit') return 'Deposit'
    if (kind === 'withdrawal') return 'Withdrawal'
    return 'Sweep'
}
</script>

<template>
    <p v-if="transfers.length === 0" class="px-5 py-8 text-sm text-gray-500 dark:text-gray-400">No transfers in this range.</p>
    <div v-else class="max-w-full overflow-x-auto">
        <table class="w-full text-sm">
            <thead>
                <tr
                    class="border-b border-gray-100 text-left text-xs font-medium tracking-wide text-gray-500 uppercase dark:border-gray-800 dark:text-gray-400"
                >
                    <th class="px-4 py-3 font-medium">Time</th>
                    <th class="px-4 py-3 font-medium">Kind</th>
                    <th class="px-4 py-3 text-end font-medium">Amount</th>
                    <th class="px-4 py-3 font-medium">Note</th>
                </tr>
            </thead>
            <tbody>
                <tr v-for="transfer in transfers" :key="transfer.id" class="border-b border-gray-50 last:border-0 dark:border-gray-800/80">
                    <td class="px-4 py-3 whitespace-nowrap text-gray-600 dark:text-gray-300"><TimeCell :value="transfer.at" /></td>
                    <td class="px-4 py-3 font-medium text-gray-800 dark:text-gray-100">{{ label(transfer.kind) }}</td>
                    <td class="px-4 py-3 text-end">
                        <MoneyCell :value="transfer.kind === 'deposit' ? transfer.amount : -transfer.amount" :currency="transfer.currency" />
                    </td>
                    <td class="px-4 py-3 text-gray-500 dark:text-gray-400">{{ transfer.note }}</td>
                </tr>
            </tbody>
        </table>
    </div>
</template>
