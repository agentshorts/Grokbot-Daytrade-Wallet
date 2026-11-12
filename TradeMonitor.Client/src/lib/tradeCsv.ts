import { formatTimeCt } from '@/lib/format'
import { modeLabel, priceKindLabel, sideLabel, statusLabel } from '@/lib/tradeQuery'
import type { TradeRow } from '@/api'

const HEADERS = [
    'Time (CT)',
    'Wallet',
    'Wallet name',
    'Mode',
    'Symbol',
    'Title',
    'Side',
    'Qty',
    'Buy price',
    'Price kind',
    'Exit or mark',
    'P&L',
    'P&L %',
    'Fees',
    'Realized P&L',
    'Unrealized P&L',
    'Status',
    'Note',
]

export function buildTradeCsv(rows: TradeRow[]): string {
    const lines = [HEADERS.map(cell).join(',')]
    for (const row of rows) {
        lines.push(
            [
                formatTimeCt(row.executedAt),
                row.wallet,
                row.walletName,
                modeLabel(row.mode),
                row.symbol,
                row.title ?? '',
                sideLabel(row.side),
                num(row.qty),
                num(row.buyPrice),
                priceKindLabel(row.priceKind),
                num(row.currentOrExitPrice),
                num(row.pnl),
                num(row.pnlPercent),
                num(row.fee),
                num(row.realizedPnl),
                num(row.unrealizedPnl),
                statusLabel(row.status),
                row.note ?? '',
            ]
                .map(cell)
                .join(','),
        )
    }
    return `${lines.join('\n')}\n`
}

export function downloadCsv(filename: string, csv: string) {
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' })
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = filename
    document.body.appendChild(link)
    link.click()
    link.remove()
    URL.revokeObjectURL(url)
}

function num(value: number | null | undefined): string {
    if (value == null || Number.isNaN(value)) return ''
    return String(value)
}

function cell(value: string): string {
    if (/[",\n\r]/.test(value)) return `"${value.replace(/"/g, '""')}"`
    return value
}
