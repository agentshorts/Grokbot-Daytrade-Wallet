import { ref, reactive } from 'vue'

const names = reactive<Record<string, string>>({})
const recordedAt = ref<string | null>(null)

export function rememberWalletNames(rows: { key: string; name: string }[]) {
    for (const row of rows) {
        if (row.key && row.name) names[row.key] = row.name
    }
}

export function walletLabelFor(key: string): string {
    return names[key] ?? ''
}

export function setRecordedAt(value: string | null) {
    recordedAt.value = value
}

export function walletRecordedAt(): string | null {
    return recordedAt.value
}

export function latestActivity(values: (string | null | undefined)[]): string | null {
    const stamps = values.filter((value): value is string => !!value).sort()
    return stamps[stamps.length - 1] ?? null
}
