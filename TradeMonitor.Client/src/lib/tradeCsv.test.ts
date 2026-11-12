import { describe, expect, it } from 'vite-plus/test'
import { buildTradeCsv } from './tradeCsv'
import type { TradeRow } from '@/api'

function row(partial: Partial<TradeRow> = {}): TradeRow {
    return {
        id: 1,
        wallet: 'paper',
        walletName: 'Paper',
        mode: 'paper',
        symbol: 'BTC-USD',
        title: 'First clip',
        side: 'buy',
        qty: 0.01,
        price: 64000,
        fee: 0.5,
        executedAt: '2026-11-02T15:04:00.000Z',
        recordedAt: '2026-11-02T15:04:00.000Z',
        note: 'Paper fill',
        status: 'open',
        buyPrice: 64000,
        currentOrExitPrice: 64100,
        priceKind: 'mark',
        exitPrice: null,
        lastMarkPrice: 64100,
        lastMarkAt: '2026-11-02T15:10:00.000Z',
        realizedPnl: 0,
        unrealizedPnl: 1,
        pnl: 1,
        pnlPercent: 0.16,
        remainingQty: 0.01,
        valuedAtCost: false,
        targetPrice: 64500,
        orderRef: 'ord-1',
        clientId: 'fill-1',
        ...partial,
    }
}

describe('buildTradeCsv', () => {
    it('exports the current rows and leaves unknown unrealized p&l blank', () => {
        const csv = buildTradeCsv([
            row(),
            row({
                id: 2,
                symbol: 'SOL-USD',
                title: 'Say "hi", then',
                note: 'a, note',
                unrealizedPnl: null,
                pnl: null,
                pnlPercent: null,
                currentOrExitPrice: null,
                priceKind: 'unknown',
                status: 'open',
            }),
        ])
        const lines = csv.trimEnd().split('\n')
        expect(lines[0]).toContain('Time (CT)')
        expect(lines[0]).toContain('Unrealized P&L')
        expect(lines[1]).toContain('BTC-USD')
        expect(lines[1]).toContain('CT')
        expect(lines[2]).toContain('"Say ""hi"", then"')
        expect(lines[2]).toContain('"a, note"')
        expect(lines[2]).not.toContain('null')
    })
})
