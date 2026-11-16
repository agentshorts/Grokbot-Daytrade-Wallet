import { describe, expect, it } from 'vite-plus/test'
import { createLedgerClient, type LedgerTransport } from '@/api/client'

describe('contract read', () => {
    it('requests the contract with GET and keeps a missing price null', async () => {
        const calls: { path: string; method?: string }[] = []
        const transport: LedgerTransport = {
            async get(path) {
                calls.push({ path })
                return {
                    status: 200,
                    body: {
                        result: {
                            ticker: 'KXBTCD-26NOV1617-T64999.99',
                            title: 'Bitcoin',
                            yesCents: null,
                            noLabel: 'implied',
                            holdings: [{ wallet: 'pm-x', name: 'BTC', side: 'YES', qty: 1, avgCost: 0.4, unrealizedPnl: null, fills: [] }],
                        },
                    },
                }
            },
        }
        const client = createLedgerClient(transport)
        const response = await client.contract({ ticker: 'KXBTCD-26NOV1617-T64999.99', lookbackHours: 24, thresholdCents: 5, allAvailable: true })
        expect(calls[0]?.path).toBe('/api/predictions/contract?ticker=KXBTCD-26NOV1617-T64999.99&lookbackHours=24&thresholdCents=5&allAvailable=true')
        expect(response.result.yesCents).toBeNull()
        expect(response.result.holdings[0]?.unrealizedPnl).toBeNull()
        expect(response.result.noLabel).toBe('implied')
        expect(JSON.stringify(calls)).not.toMatch(/POST|PUT|PATCH|DELETE/)
    })
})
