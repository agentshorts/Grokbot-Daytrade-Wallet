import { describe, expect, it } from 'vite-plus/test'
import { filterRows, rowMatches } from './textFilter'

const rows = [
    { symbol: 'BTC-USD', wallet: 'Coinbase', side: 'buy' },
    { symbol: 'ETH-USD', wallet: 'Paper', side: 'sell' },
]

const fields = (row: (typeof rows)[number]) => [row.symbol, row.wallet, row.side]

describe('filterRows', () => {
    it('keeps every row when the query is blank', () => {
        expect(filterRows(rows, '   ', fields)).toBe(rows)
        expect(rowMatches('', ['BTC-USD'])).toBe(true)
    })

    it('matches ticker, wallet, and side without case, and requires every word', () => {
        expect(filterRows(rows, '  btc  ', fields).map((row) => row.symbol)).toEqual(['BTC-USD'])
        expect(filterRows(rows, 'BUY', fields).map((row) => row.symbol)).toEqual(['BTC-USD'])
        expect(filterRows(rows, 'paper sell', fields).map((row) => row.symbol)).toEqual(['ETH-USD'])
        expect(filterRows(rows, 'btc sell', fields)).toEqual([])
        expect(rowMatches('eth', [null, undefined, '', 'ETH-USD'])).toBe(true)
    })
})
