import { describe, expect, it } from 'vite-plus/test'
import {
    clipContractChart,
    filterClosedContracts,
    formatContractName,
    HIDE_CLOSED_CONTRACTS_KEY,
    parseContractTicker,
    presentPrediction,
    readHideClosedContracts,
    writeHideClosedContracts,
    type PredictionFacts,
} from './predictionContract'

const noon = 'KXBTCD-26NOV1212-T71499.99-KALSHI'
const ten = 'KXBTCD-26NOV1210-T71199.99-KALSHI'
const confirmedNoon = '2026-11-12T17:00:00.000Z'
const confirmedTen = '2026-11-12T15:00:00.000Z'

describe('contract ticker names', () => {
    it('parses the sample ticker and shows the confirmed close in the viewer zone', () => {
        const parsed = parseContractTicker(`Coinbase ${noon}`)
        expect(parsed).toMatchObject({
            raw: noon,
            series: 'KXBTCD',
            asset: 'BTC',
            year: 2026,
            month: 11,
            day: 12,
            hour: 12,
            strike: 71499.99,
            direction: 'above',
        })
        expect(formatContractName(parsed!, confirmedNoon, 'America/New_York')).toBe('BTC above $71,500 at 12 PM EST, Nov 12')
        expect(formatContractName(parsed!, confirmedNoon, 'UTC')).toBe('BTC above $71,500 at 5 PM UTC, Nov 12')
        expect(formatContractName(parsed!, confirmedNoon, 'America/Chicago')).toBe('BTC above $71,500 at 11 AM CST, Nov 12')
    })

    it('parses another strike, date, and hour, and uses the close timestamp rather than the ticker hour', () => {
        const other = parseContractTicker(`pm-${ten}`)
        expect(other).toMatchObject({ asset: 'BTC', day: 12, hour: 10, strike: 71199.99, direction: 'above' })
        expect(formatContractName(other!, confirmedTen, 'America/New_York')).toBe('BTC above $71,200 at 10 AM EST, Nov 12')
        expect(formatContractName(other!, confirmedTen, 'UTC')).toBe('BTC above $71,200 at 3 PM UTC, Nov 12')

        const december = parseContractTicker('KXETHD-26DEC0115-T2499.99-KALSHI')
        expect(december).toMatchObject({ asset: 'ETH', month: 12, day: 1, hour: 15, strike: 2499.99 })
        expect(formatContractName(december!, '2026-12-01T20:00:00Z', 'America/New_York')).toBe('ETH above $2,500 at 3 PM EST, Dec 1')

        const below = parseContractTicker('KXBTCD-26NOV1212-B68000.01')
        expect(below).toMatchObject({ direction: 'below', strike: 68000.01, hour: 12 })
        expect(formatContractName(below!, confirmedNoon, 'UTC')).toBe('BTC below $68,000 at 5 PM UTC, Nov 12')

        const parsed = parseContractTicker(noon)!
        expect(formatContractName(parsed, confirmedTen, 'America/New_York')).toBe('BTC above $71,500 at 10 AM EST, Nov 12')
    })

    it('keeps the raw ticker when parsing or the close lookup is missing', () => {
        expect(parseContractTicker('pm-win')).toBeNull()
        expect(formatContractName(parseContractTicker(noon)!, 'not-a-time', 'UTC')).toBeNull()
        expect(formatContractName(parseContractTicker(noon)!, confirmedNoon, 'Not/AZone')).toBeNull()
        const facts = baseFacts({ closeAt: null, name: `Coinbase ${noon}` })
        expect(presentPrediction(facts).name).toBe(`Coinbase ${noon}`)
        expect(presentPrediction(baseFacts({ key: 'pm-win', name: 'Winner', symbol: '', closeAt: confirmedNoon })).name).toBe('Winner')
    })
})

describe('closed contract P&L', () => {
    it('shows realized P&L and ends the chart at the close instead of zero', () => {
        const shown = presentPrediction(
            baseFacts({
                settlement: 'settled-1',
                openPositions: 0,
                marketValue: 0,
                marketChange: 0,
                lifetimeRealized: 12.5,
                rangeRealized: 12.5,
                sparkline: [
                    { at: '2026-11-12T16:00:00.000Z', value: 40, cash: 0 },
                    { at: '2026-11-12T17:00:00.000Z', value: 0, cash: 0 },
                    { at: '2026-11-12T18:30:00.000Z', value: 0, cash: 0 },
                ],
            }),
        )
        expect(shown.closed).toBe(true)
        expect(shown.badge).toBe('Settled')
        expect(shown.value).toBe(12.5)
        expect(shown.change).toBe(12.5)
        expect(shown.sparkline.at(-1)).toBe(12.5)
        expect(shown.sparkline).not.toContain(0)

        const line = clipContractChart(shownInput().sparkline, {
            closeAt: confirmedNoon,
            ledgerClosed: true,
            marketClosed: true,
            realized: 12.5,
            pinRealized: true,
        })
        expect(line.every((point) => Date.parse(point.at) <= Date.parse(confirmedNoon))).toBe(true)
        expect(Date.parse(line.at(-1)!.at)).toBe(Date.parse(confirmedNoon))
        expect(line.at(-1)?.value).toBe(12.5)
    })

    it('uses a Closed badge for a sale and keeps a losing settlement off zero', () => {
        const sold = presentPrediction(baseFacts({ settlement: 'closed', openPositions: 0, marketValue: 0, lifetimeRealized: 4, rangeRealized: 4 }))
        expect(sold.badge).toBe('Closed')
        expect(sold.value).toBe(4)

        const lost = presentPrediction(
            baseFacts({
                settlement: 'settled-0',
                openPositions: 0,
                marketValue: 0,
                marketChange: -40,
                lifetimeRealized: -7.625,
                rangeRealized: -7.625,
                sparkline: [
                    { at: '2026-11-12T16:00:00.000Z', value: 40 },
                    { at: '2026-11-12T17:00:00.000Z', value: 0 },
                ],
            }),
        )
        expect(lost.badge).toBe('Settled')
        expect(lost.value).toBe(-7.625)
        expect(lost.sparkline.at(-1)).toBe(-7.625)
    })

    it('leaves an open contract on its market value', () => {
        const open = presentPrediction(
            baseFacts({
                settlement: 'open',
                openPositions: 1,
                marketValue: 40,
                marketChange: 5,
                now: new Date('2026-11-12T16:40:00.000Z'),
                sparkline: [
                    { at: '2026-11-12T16:00:00.000Z', value: 35 },
                    { at: '2026-11-12T16:30:00.000Z', value: 40 },
                ],
            }),
        )
        expect(open.closed).toBe(false)
        expect(open.badge).toBeNull()
        expect(open.value).toBe(40)
        expect(open.change).toBe(5)
        expect(open.sparkline).toEqual([35, 40])
        expect(open.name).toBe('BTC above $71,500 at 12 PM EST, Nov 12')
        expect(open.ticker).toBe(noon)
    })
})

describe('hide closed contracts', () => {
    it('persists the toggle and filters only closed contracts', () => {
        const saved = new Map<string, string>()
        const storage = {
            getItem: (key: string) => saved.get(key) ?? null,
            setItem: (key: string, value: string) => {
                saved.set(key, value)
            },
        }
        expect(readHideClosedContracts(storage)).toBe(false)
        writeHideClosedContracts(storage, true)
        expect(saved.get(HIDE_CLOSED_CONTRACTS_KEY)).toBe('1')
        expect(readHideClosedContracts(storage)).toBe(true)
        writeHideClosedContracts(storage, false)
        expect(readHideClosedContracts(storage)).toBe(false)

        const rows = [
            { id: 'spot', closed: false },
            { id: 'open-pm', closed: false },
            { id: 'done', closed: true },
        ]
        expect(filterClosedContracts(rows, true, (row) => row.closed).map((row) => row.id)).toEqual(['spot', 'open-pm'])
        expect(filterClosedContracts(rows, false, (row) => row.closed).map((row) => row.id)).toEqual(['spot', 'open-pm', 'done'])
    })
})

function shownInput(): PredictionFacts {
    return baseFacts({
        sparkline: [
            { at: '2026-11-12T16:00:00.000Z', value: 40, cash: 0 },
            { at: '2026-11-12T17:00:00.000Z', value: 0, cash: 0 },
            { at: '2026-11-12T18:30:00.000Z', value: 0, cash: 0 },
        ],
    })
}

function baseFacts(partial: Partial<PredictionFacts>): PredictionFacts {
    return {
        key: `pm-${noon}`,
        name: `Coinbase ${noon}`,
        symbol: noon,
        kind: 'PredictionMarket',
        settlement: 'open',
        openPositions: 1,
        tradeCount: 2,
        marketValue: 0,
        marketChange: 0,
        lifetimeRealized: 0,
        rangeRealized: 0,
        closeAt: confirmedNoon,
        sparkline: [],
        now: new Date('2026-11-12T20:00:00.000Z'),
        timeZone: 'America/New_York',
        ...partial,
    }
}
