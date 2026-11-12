import { describe, expect, it } from 'vite-plus/test'
import { LedgerReadError, type LedgerClient, type LedgerQuery } from '@/api'
import { rangeStart } from '@/lib/book'
import { loadDashboard } from '@/lib/loadDashboard'
import type { Filters } from '@/lib/filters'

const now = new Date('2026-11-12T02:18:00.000Z')

function recording(partial: Partial<LedgerClient> = {}) {
    const calls: { method: string; query?: LedgerQuery; key?: string }[] = []
    const client = {
        async summary(query?: LedgerQuery) {
            calls.push({ method: 'summary', query })
            return {
                totals: {
                    wallets: 0,
                    baseCurrency: 'USD',
                    mixedCurrencies: false,
                    totalValue: 0,
                    valueChange: 0,
                    cash: 0,
                    swept: 0,
                    openExposure: 0,
                    unmarkedCost: 0,
                    positionsValue: 0,
                    valuedAtCost: false,
                    realizedPnl: 0,
                    unrealizedPnl: null,
                    unrealizedChange: null,
                    pnl: null,
                    fees: 0,
                    deposits: 0,
                    withdrawals: 0,
                    sweeps: 0,
                    winRate: null,
                    wins: 0,
                    losses: 0,
                    scratches: 0,
                    tradeCount: 0,
                    openPositions: 0,
                },
                modes: [],
                wallets: [],
            }
        },
        async equity(query?: LedgerQuery) {
            calls.push({ method: 'equity', query })
            return { wallets: [], total: [] }
        },
        async pnlDaily(query?: LedgerQuery) {
            calls.push({ method: 'pnlDaily', query })
            return { results: [], wallets: [] }
        },
        async pnlBySymbol(query?: LedgerQuery) {
            calls.push({ method: 'pnlBySymbol', query })
            return { results: [] }
        },
        async fees(query?: LedgerQuery) {
            calls.push({ method: 'fees', query })
            return { total: 0, days: [] }
        },
        async positions(query?: LedgerQuery) {
            calls.push({ method: 'positions', query })
            return { markStaleAfterMinutes: 15, results: [] }
        },
        async trades(query?: LedgerQuery) {
            calls.push({ method: 'trades', query })
            return { total: 0, skip: 0, take: 0, results: [] }
        },
        async trade() {
            throw new Error('trade detail is not part of the overview load')
        },
        async wallets() {
            calls.push({ method: 'wallets' })
            return { results: [] }
        },
        async wallet() {
            throw new Error('wallet detail is not part of the overview load')
        },
        async walletBalances() {
            throw new Error('balances are not part of the overview load')
        },
        async walletHistory() {
            throw new Error('history is not part of the overview load')
        },
        async priceMarks(query?: LedgerQuery) {
            calls.push({ method: 'priceMarks', query })
            return { total: 0, skip: 0, take: 0, results: [] }
        },
        ...partial,
    } satisfies LedgerClient
    return { client, calls }
}

describe('loadDashboard', () => {
    it('asks the read API for the URL mode, range, and wallet', async () => {
        const { client, calls } = recording()
        const filters: Filters = { mode: 'paper', range: '30d', wallet: 'paper' }
        const view = await loadDashboard(filters, now, client)
        expect(view.emptyBook).toBe(true)
        const summary = calls.find((call) => call.method === 'summary')
        expect(summary?.query).toMatchObject({
            mode: 'paper',
            wallet: 'paper',
            from: rangeStart('30d', now)?.toISOString(),
            to: now.toISOString(),
        })
        expect(calls.find((call) => call.method === 'trades')?.query).toMatchObject({ take: 200, orderBy: 'time', descending: true })
        expect(calls.find((call) => call.method === 'positions')?.query).toEqual({ mode: 'paper', wallet: 'paper', status: 'open' })
        expect(calls.every((call) => call.method !== 'trade')).toBe(true)
    })

    it('omits the date bounds for the all range', async () => {
        const { client, calls } = recording()
        await loadDashboard({ mode: 'all', range: 'all', wallet: null }, now, client)
        const query = calls.find((call) => call.method === 'summary')?.query
        expect(query?.mode).toBe('all')
        expect(query?.wallet).toBeNull()
        expect(query?.from).toBeUndefined()
        expect(query?.to).toBeUndefined()
    })

    it('turns an unknown wallet into no matching rows', async () => {
        const { client } = recording({
            async wallets() {
                return {
                    results: [
                        {
                            id: 1,
                            key: 'paper',
                            name: 'Paper',
                            mode: 'paper',
                            venue: '',
                            baseCurrency: 'USD',
                            note: '',
                            isArchived: false,
                            sortOrder: 1,
                            createdAt: null,
                        },
                    ],
                }
            },
            async summary() {
                throw new LedgerReadError(404, 'UnknownWallet', "Unknown wallet 'missing'.")
            },
        })
        const view = await loadDashboard({ mode: 'all', range: '30d', wallet: 'missing' }, now, client)
        expect(view.emptyBook).toBe(false)
        expect(view.noMatches).toBe(true)
        expect(view.walletNames.paper).toBe('Paper')
    })
})
