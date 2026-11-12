const labels: Record<string, string> = {
    open: 'Open',
    closed: 'Closed',
    'settled-0': 'Settled at 0',
    'settled-1': 'Settled at 1',
    settled: 'Settled',
    none: 'No trades',
}

export function settlementLabel(code: string): string {
    return labels[code] ?? (code || 'Open')
}
