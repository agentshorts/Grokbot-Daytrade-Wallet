import { describe, expect, it } from 'vite-plus/test'
import { includeArchivedQuery, visibleWallets } from './visibleWallets'

const rows = [
    { key: 'paper', isArchived: false },
    { key: 'old-book', isArchived: true },
    { key: 'coinbase', isArchived: false },
]

describe('visibleWallets', () => {
    it('hides archived wallets unless show archived is on', () => {
        expect(visibleWallets(rows, false).map((row) => row.key)).toEqual(['paper', 'coinbase'])
        expect(visibleWallets(rows, true).map((row) => row.key)).toEqual(['paper', 'old-book', 'coinbase'])
    })

    it('does not ask the ledger for archived wallets unless show archived is on', () => {
        expect(includeArchivedQuery(false)).toEqual({})
        expect(includeArchivedQuery(true)).toEqual({ includeArchived: true })
    })
})
