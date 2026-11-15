import { describe, expect, it } from 'vite-plus/test'
import { liveHref, liveQuery, primarySymbol, readLiveSelection } from './liveQuery'

const products = [{ productId: 'BTC-USD' }, { productId: 'ETH-USD' }]
const wallets = [{ key: 'paper' }, { key: 'coinbase' }]

describe('live wallet links', () => {
    it('picks a single symbol or the strictly larger holding', () => {
        expect(primarySymbol([])).toBeNull()
        expect(primarySymbol([{ symbol: 'SOL-USD', qty: 2, cost: 300 }])).toBe('SOL-USD')
        expect(
            primarySymbol([
                { symbol: 'ETH-USD', qty: 0.1, cost: 320.1 },
                { symbol: 'BTC-USD', qty: 0.006, cost: 384.3 },
                { symbol: 'SOL-USD', qty: 0, cost: 0, open: false },
            ]),
        ).toBe('BTC-USD')
        expect(
            primarySymbol([
                { symbol: 'ETH-USD', qty: 1, cost: 10 },
                { symbol: 'BTC-USD', qty: 1, cost: 10 },
            ]),
        ).toBeNull()
    })

    it('builds a new-window live link and reads the query back', () => {
        expect(liveHref('paper', 'BTC-USD')).toBe('/live?wallet=paper&symbol=BTC-USD')
        expect(liveHref('coinbase', null)).toBe('/live?wallet=coinbase')
        expect(readLiveSelection({ wallet: 'paper', symbol: 'eth-usd' }, wallets, products, 'BTC-USD')).toEqual({
            walletKey: 'paper',
            symbol: 'ETH-USD',
            unknownWallet: null,
        })
        expect(readLiveSelection({ wallet: 'missing', symbol: 'NOPE-USD' }, wallets, products, 'BTC-USD')).toEqual({
            walletKey: '',
            symbol: 'BTC-USD',
            unknownWallet: 'missing',
        })
        expect(liveQuery('paper', 'ETH-USD', null)).toEqual({ wallet: 'paper', symbol: 'ETH-USD' })
        expect(liveQuery('', 'BTC-USD', 'missing')).toEqual({ wallet: 'missing', symbol: 'BTC-USD' })
    })
})
