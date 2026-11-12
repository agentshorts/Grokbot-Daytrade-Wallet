import { describe, expect, it } from 'vite-plus/test'
import { parseTradeQuery, writeTradeQuery } from './tradeQuery'

describe('parseTradeQuery', () => {
    it('reads filters, sort, page, and the open trade', () => {
        expect(
            parseTradeQuery({
                symbol: ' btc-usd ',
                side: 'SELL',
                outcome: 'won',
                q: ' clip ',
                order: 'pnl',
                dir: 'asc',
                page: '3',
                trade: '12',
                wallet: 'paper',
            }),
        ).toEqual({
            symbol: 'btc-usd',
            side: 'sell',
            outcome: 'won',
            q: 'clip',
            orderBy: 'pnl',
            descending: false,
            page: 3,
            tradeId: 12,
        })
    })

    it('drops unknown values and keeps defaults out of the url', () => {
        expect(parseTradeQuery({ side: 'short', outcome: 'maybe', order: 'title', page: '0', trade: 'nope' })).toEqual({
            symbol: null,
            side: null,
            outcome: null,
            q: null,
            orderBy: 'time',
            descending: true,
            page: 1,
            tradeId: null,
        })

        expect(writeTradeQuery({ mode: 'paper', wallet: 'paper' }, { page: 1, tradeId: null })).toEqual({
            mode: 'paper',
            wallet: 'paper',
        })
        expect(
            writeTradeQuery(
                { mode: 'paper' },
                {
                    symbol: 'ETH-USD',
                    side: 'buy',
                    outcome: 'lost',
                    q: 'note',
                    orderBy: 'fee',
                    descending: false,
                    page: 2,
                    tradeId: 9,
                },
            ),
        ).toEqual({
            mode: 'paper',
            symbol: 'ETH-USD',
            side: 'buy',
            outcome: 'lost',
            q: 'note',
            order: 'fee',
            dir: 'asc',
            page: '2',
            trade: '9',
        })
    })

    it('keeps an open trade when a filter changes and can close it without dropping the filter', () => {
        const open = writeTradeQuery({ wallet: 'paper', side: 'buy' }, { tradeId: 4 })
        expect(open).toEqual({ wallet: 'paper', side: 'buy', trade: '4' })
        expect(writeTradeQuery(open, { tradeId: null })).toEqual({ wallet: 'paper', side: 'buy' })
    })
})
