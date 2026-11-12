import { describe, expect, it } from 'vite-plus/test'
import { changePercent } from '@/api'
import type { HistoryEntry, PnlDay, PositionRow, WalletCardModel, WalletInfo } from '@/api/types'
import { buildWalletDetail, buildWalletList, DASH, distanceToTarget, markIsStale } from './walletView'

const now = new Date('2026-11-12T16:30:00.000Z')

describe('distanceToTarget', () => {
    it('is the target minus the mark, and is null when either price is missing', () => {
        expect(distanceToTarget(15, 12)).toEqual({ amount: 3, percent: 25 })
        expect(distanceToTarget(9, null)).toBeNull()
        expect(distanceToTarget(null, 12)).toBeNull()
        expect(distanceToTarget(10, 0)).toBeNull()
    })
})

describe('markIsStale', () => {
    it('uses a configurable age and treats the exact threshold as fresh', () => {
        expect(markIsStale('2026-11-12T16:14:00.000Z', now, 15)).toBe(true)
        expect(markIsStale('2026-11-12T16:15:00.000Z', now, 15)).toBe(false)
        expect(markIsStale('2026-11-12T16:20:00.000Z', now, 15)).toBe(false)
        expect(markIsStale(null, now, 15)).toBe(false)
        expect(markIsStale('2026-11-12T16:24:00.000Z', now, 5)).toBe(true)
    })
})

function wallet(partial: Partial<WalletInfo>): WalletInfo {
    return {
        id: 1,
        key: 'paper',
        name: 'Paper',
        mode: 'paper',
        venue: 'Coinbase',
        baseCurrency: 'USD',
        note: '',
        isArchived: false,
        sortOrder: 1,
        createdAt: '2026-11-08T15:00:00.000Z',
        ...partial,
    }
}

function card(partial: Partial<WalletCardModel>): WalletCardModel {
    return {
        id: 1,
        key: 'paper',
        name: 'Paper',
        mode: 'paper',
        baseCurrency: 'USD',
        isArchived: false,
        sortOrder: 1,
        totalValue: 214,
        valueChange: 14,
        cash: 203,
        swept: 10,
        openExposure: 11,
        unmarkedCost: 11,
        positionsValue: 11,
        valuedAtCost: true,
        realizedPnl: 14,
        unrealizedPnl: null,
        unrealizedChange: null,
        pnl: null,
        fees: 8,
        deposits: 200,
        withdrawals: 0,
        sweeps: 10,
        winRate: null,
        wins: 0,
        losses: 0,
        scratches: 0,
        tradeCount: 1,
        openPositions: 1,
        lastActivityAt: '2026-11-11T15:00:00.000Z',
        sparkline: [],
        ...partial,
    }
}

describe('buildWalletDetail', () => {
    const balances = { cashIsNegative: false, hasDeposits: true }
    const position = (partial: Partial<PositionRow>): PositionRow => ({
        wallet: 'paper',
        walletName: 'Paper',
        mode: 'paper',
        symbol: 'ETH-USD',
        status: 'open',
        qty: 1,
        avgPrice: 10,
        costBasis: 10,
        lastMarkPrice: 12,
        lastMarkAt: '2026-11-12T16:20:00.000Z',
        markIsStale: false,
        markStaleAfterMinutes: 15,
        unrealizedPnl: 2,
        unrealizedPnlPercent: 20,
        realizedPnl: 0,
        feesPaid: 0,
        marketValue: 12,
        valuedAtCost: false,
        targetPrice: 15,
        distanceToTarget: 3,
        distanceToTargetPercent: 25,
        ...partial,
    })

    it('copies summary figures and leaves unknown unrealized P&L null', () => {
        const detail = buildWalletDetail({
            wallet: wallet({}),
            card: card({}),
            equity: [],
            days: [{ date: '2026-11-11T00:00:00.000Z', pnl: null, fees: 1 } as PnlDay],
            feeTotal: 8,
            feeDays: [],
            positions: [
                position({
                    symbol: 'SOL-USD',
                    lastMarkPrice: null,
                    lastMarkAt: null,
                    unrealizedPnl: null,
                    unrealizedPnlPercent: null,
                    targetPrice: 9,
                    distanceToTarget: null,
                    distanceToTargetPercent: null,
                }),
            ],
            staleAfterMinutes: 15,
            trades: [],
            history: [],
            balances,
            range: 'all',
            now,
        })
        expect(detail.totalValue).toBe(214)
        expect(detail.cash).toBe(203)
        expect(detail.swept).toBe(10)
        expect(detail.fees).toBe(8)
        expect(detail.change).toBe(14)
        expect(detail.changePct).toBe(changePercent(214, 14))
        expect(detail.unrealizedPnl).toBeNull()
        expect(detail.mode).toBe('paper')
        expect(detail.openPositions[0]?.unrealizedPnl).toBeNull()
        expect(detail.dailyHasGap).toBe(true)
        expect(DASH).toBe('\u2014')
    })

    it('flags a stale mark from the server or from the configured age', () => {
        const detail = buildWalletDetail({
            wallet: wallet({}),
            card: card({ unrealizedPnl: 2 }),
            equity: [],
            days: [],
            feeTotal: 8,
            feeDays: [],
            positions: [
                position({ symbol: 'OLD', lastMarkAt: '2026-11-12T16:00:00.000Z', markIsStale: false }),
                position({ symbol: 'FRESH', lastMarkAt: '2026-11-12T16:29:00.000Z', markIsStale: true }),
            ],
            staleAfterMinutes: 15,
            trades: [],
            history: [],
            balances,
            range: '30d',
            now,
        })
        expect(detail.openPositions.find((row) => row.symbol === 'OLD')?.stale).toBe(true)
        expect(detail.openPositions.find((row) => row.symbol === 'FRESH')?.stale).toBe(true)
        expect(detail.openPositions[0]?.distanceToTarget).toBe(3)
        expect(distanceToTarget(15, 12)?.amount).toBe(detail.openPositions[0]?.distanceToTarget)
    })

    it('shows the starting balance banner only when cash is negative and no deposit is recorded', () => {
        const history: HistoryEntry[] = [
            {
                wallet: 'paper',
                kind: 'Deposit',
                id: 1,
                clientId: 'dep-1',
                at: '2026-11-08T15:00:00.000Z',
                symbol: null,
                side: null,
                qty: null,
                price: null,
                fee: null,
                amount: 1000,
                currency: 'USD',
                title: null,
                note: 'Starting cash',
                orderRef: null,
                targetPrice: null,
                recordedAt: '2026-11-08T15:00:00.000Z',
            },
        ]
        const missing = buildWalletDetail({
            wallet: wallet({}),
            card: card({}),
            equity: [],
            days: [],
            feeTotal: 0,
            feeDays: [],
            positions: [],
            staleAfterMinutes: 15,
            trades: [],
            history,
            balances: { cashIsNegative: true, hasDeposits: false },
            range: 'all',
            now,
        })
        const funded = buildWalletDetail({
            wallet: wallet({}),
            card: card({}),
            equity: [],
            days: [],
            feeTotal: 0,
            feeDays: [],
            positions: [],
            staleAfterMinutes: 15,
            trades: [],
            history,
            balances: { cashIsNegative: true, hasDeposits: true },
            range: 'all',
            now,
        })
        expect(missing.startingBalanceMissing).toBe(true)
        expect(missing.transfers.map((row) => row.kind)).toEqual(['deposit'])
        expect(funded.startingBalanceMissing).toBe(false)
    })
})

describe('buildWalletList', () => {
    it('keeps archived wallets and the real or paper mode', () => {
        const list = buildWalletList(
            [
                wallet({ key: 'paper', name: 'Paper', mode: 'paper', sortOrder: 1 }),
                wallet({ key: 'coinbase', name: 'Coinbase', mode: 'real', venue: 'Coinbase', sortOrder: 2 }),
                wallet({ key: 'old-book', name: 'Old book', mode: 'paper', isArchived: true, sortOrder: 3 }),
            ],
            [
                card({ key: 'paper' }),
                card({ key: 'coinbase', name: 'Coinbase', mode: 'real', totalValue: 52, unrealizedPnl: 2, valuedAtCost: false, unmarkedCost: 0 }),
                card({ key: 'old-book', isArchived: true, totalValue: 5 }),
            ],
            { mode: 'all', range: 'all', wallet: null },
        )
        expect(list.emptyBook).toBe(false)
        expect(list.wallets.map((row) => row.key)).toEqual(['paper', 'coinbase', 'old-book'])
        expect(list.wallets[1]?.mode).toBe('real')
        expect(list.wallets[2]?.isArchived).toBe(true)

        const paper = buildWalletList([wallet({ key: 'paper', mode: 'paper' }), wallet({ key: 'coinbase', mode: 'real' })], [], {
            mode: 'paper',
            range: '30d',
            wallet: null,
        })
        expect(paper.wallets.map((row) => row.key)).toEqual(['paper'])
        expect(paper.noMatches).toBe(false)
    })
})
