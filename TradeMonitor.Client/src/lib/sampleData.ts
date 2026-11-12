import { chicagoDateTime, roundTo, startOfChicagoDay } from '@/lib/format'
import type { DashboardSnapshot, EquityPoint, PnlBarPoint, PositionRecord, PriceMarkRecord, TradeRecord, TradeSide, WalletRecord } from '@/lib/book'
import type { WalletMode } from '@/lib/filters'

const SPECS = {
    'BTC-USD': { title: 'Bitcoin', start: 94880, end: 101640, wave: 2200 },
    'ETH-USD': { title: 'Ether', start: 3310, end: 3588, wave: 120 },
    'SOL-USD': { title: 'Solana', start: 147.2, end: 168.4, wave: 8 },
    'CPI-YES': { title: 'CPI above 3%', start: 0.44, end: 0.52, wave: 0.05 },
} as const

type SymbolKey = keyof typeof SPECS

interface Lot {
    tradeId: number
    qty: number
    unitCost: number
}

interface Book {
    cash: number
    lots: Map<SymbolKey, Lot[]>
}

interface PlannedTrade {
    at: Date
    day: number
    walletKey: string
    symbol: SymbolKey
    side: TradeSide
    qty: number
    price?: number
    note: string
}

const WALLETS: WalletRecord[] = [
    {
        id: 1,
        key: 'coinbase',
        name: 'Coinbase',
        mode: 'real',
        venue: 'Coinbase',
        baseCurrency: 'USD',
        note: 'Spot book recorded by the agent.',
        isArchived: false,
        sortOrder: 1,
    },
    {
        id: 2,
        key: 'paper-swing',
        name: 'Paper Swing',
        mode: 'paper',
        venue: 'Paper',
        baseCurrency: 'USD',
        note: 'Paper swings. Nothing is sent to an exchange.',
        isArchived: false,
        sortOrder: 2,
    },
    {
        id: 3,
        key: 'kalshi',
        name: 'Kalshi',
        mode: 'real',
        venue: 'Kalshi',
        baseCurrency: 'USD',
        note: 'Event contracts.',
        isArchived: false,
        sortOrder: 3,
    },
]

function priceAt(symbol: SymbolKey, day: number): number {
    const spec = SPECS[symbol]
    const t = Math.min(1, Math.max(0, (day + 59) / 59))
    const wave = Math.sin((day + 59) * 0.7) * spec.wave
    const raw = spec.start + (spec.end - spec.start) * t + wave
    return roundTo(Math.max(symbol === 'CPI-YES' ? 0.05 : 1, raw), 2)
}

function priceOnToday(symbol: SymbolKey, date: Date, now: Date): number {
    const start = startOfChicagoDay(now).getTime()
    const frac = Math.min(1, Math.max(0, (date.getTime() - start) / 86_400_000))
    const base = priceAt(symbol, -1) + (priceAt(symbol, 0) - priceAt(symbol, -1)) * frac
    const wobble = Math.sin((date.getTime() - start) / 3_600_000) * SPECS[symbol].wave * 0.08
    return roundTo(Math.max(symbol === 'CPI-YES' ? 0.05 : 1, base + wobble), 2)
}

function feeFor(symbol: SymbolKey, notional: number): number {
    if (symbol === 'CPI-YES') return roundTo(Math.max(0.2, notional * 0.01), 2)
    return roundTo(Math.max(0.5, notional * 0.0006), 2)
}

function plannedTrades(now: Date): PlannedTrade[] {
    const recent = new Date(Math.max(startOfChicagoDay(now).getTime() + 60_000, now.getTime() - 15 * 60_000))
    const rows: PlannedTrade[] = [
        { day: -52, walletKey: 'coinbase', symbol: 'BTC-USD', side: 'buy', qty: 0.15, note: 'Opened the BTC lot.', at: chicagoDateTime(now, -52, 10, 15) },
        { day: -48, walletKey: 'paper-swing', symbol: 'SOL-USD', side: 'buy', qty: 30, note: 'Paper SOL starter.', at: chicagoDateTime(now, -48, 11, 5) },
        { day: -45, walletKey: 'kalshi', symbol: 'CPI-YES', side: 'buy', qty: 800, note: 'CPI contract.', at: chicagoDateTime(now, -45, 9, 40) },
        { day: -40, walletKey: 'coinbase', symbol: 'ETH-USD', side: 'buy', qty: 2.5, note: 'Added ether.', at: chicagoDateTime(now, -40, 14, 10) },
        { day: -33, walletKey: 'paper-swing', symbol: 'BTC-USD', side: 'buy', qty: 0.06, note: 'Paper BTC.', at: chicagoDateTime(now, -33, 13, 20) },
        {
            day: -24,
            walletKey: 'coinbase',
            symbol: 'BTC-USD',
            side: 'sell',
            qty: 0.08,
            note: 'Trimmed BTC into strength.',
            at: chicagoDateTime(now, -24, 15, 5),
        },
        {
            day: -20,
            walletKey: 'kalshi',
            symbol: 'CPI-YES',
            side: 'sell',
            qty: 500,
            price: 0.36,
            note: 'Cut the CPI position.',
            at: chicagoDateTime(now, -20, 12, 0),
        },
        {
            day: -16,
            walletKey: 'paper-swing',
            symbol: 'SOL-USD',
            side: 'sell',
            qty: 10,
            price: 140,
            note: 'Stopped out a slice of SOL.',
            at: chicagoDateTime(now, -16, 10, 45),
        },
        { day: -11, walletKey: 'coinbase', symbol: 'BTC-USD', side: 'buy', qty: 0.05, note: 'Bought a dip.', at: chicagoDateTime(now, -11, 11, 30) },
        { day: -7, walletKey: 'paper-swing', symbol: 'SOL-USD', side: 'buy', qty: 12, note: 'Added SOL.', at: chicagoDateTime(now, -7, 15, 15) },
        { day: -4, walletKey: 'kalshi', symbol: 'CPI-YES', side: 'buy', qty: 200, note: 'Reopened a smaller CPI line.', at: chicagoDateTime(now, -4, 9, 10) },
        { day: -2, walletKey: 'coinbase', symbol: 'ETH-USD', side: 'sell', qty: 1, note: 'Took some ether off.', at: chicagoDateTime(now, -2, 16, 5) },
        {
            day: -1,
            walletKey: 'paper-swing',
            symbol: 'BTC-USD',
            side: 'sell',
            qty: 0.03,
            note: 'Scaled out of paper BTC.',
            at: chicagoDateTime(now, -1, 14, 50),
        },
        { day: 0, walletKey: 'coinbase', symbol: 'ETH-USD', side: 'buy', qty: 0.4, note: 'Latest ether add.', at: recent },
    ]
    return rows
}

function marketValue(book: Book, priceFor: (symbol: SymbolKey) => number): number {
    let value = book.cash
    for (const [symbol, lots] of book.lots) {
        const qty = lots.reduce((total, lot) => total + lot.qty, 0)
        value += qty * priceFor(symbol)
    }
    return roundTo(value, 2)
}

function applyTrade(book: Book, trade: PlannedTrade, id: number, price: number): { pnl: number | null; pnlPct: number | null } {
    const fee = feeFor(trade.symbol, trade.qty * price)
    const lots = book.lots.get(trade.symbol) ?? []
    book.lots.set(trade.symbol, lots)
    if (trade.side === 'buy') {
        const unitCost = (trade.qty * price + fee) / trade.qty
        lots.push({ tradeId: id, qty: trade.qty, unitCost })
        book.cash = roundTo(book.cash - trade.qty * price - fee, 2)
        return { pnl: null, pnlPct: null }
    }
    let left = trade.qty
    let costRemoved = 0
    while (left > 1e-10) {
        const lot = lots[0]
        if (!lot) throw new Error(`Sell of ${trade.symbol} exceeds the open lot`)
        const take = Math.min(lot.qty, left)
        costRemoved += take * lot.unitCost
        lot.qty -= take
        left -= take
        if (lot.qty <= 1e-8) lots.shift()
    }
    const pnl = roundTo(trade.qty * price - fee - costRemoved, 2)
    const pnlPct = costRemoved > 0 ? roundTo((pnl / costRemoved) * 100, 2) : 0
    book.cash = roundTo(book.cash + trade.qty * price - fee, 2)
    return { pnl, pnlPct }
}

function pushEquity(target: EquityPoint[], walletKey: string, at: Date, value: number, grain: 'day' | 'hour') {
    target.push({ walletKey, at: at.toISOString(), value, grain })
}

function pushBar(target: PnlBarPoint[], walletKey: string, at: Date, pnl: number, grain: 'day' | 'hour') {
    target.push({ walletKey, at: at.toISOString(), pnl: roundTo(pnl, 2), grain })
}

/**
 * A deterministic sample book. Stands in for the ledger read API.
 * Cash, positions and P&L are replayed from the same fills the screens show.
 */
export function buildSample(now: Date): DashboardSnapshot {
    const books = new Map<string, Book>([
        ['coinbase', { cash: 45_000, lots: new Map() }],
        ['paper-swing', { cash: 15_000, lots: new Map() }],
        ['kalshi', { cash: 2_500, lots: new Map() }],
    ])
    const modes = new Map<string, WalletMode>(WALLETS.map((wallet) => [wallet.key, wallet.mode]))
    const names = new Map<string, string>(WALLETS.map((wallet) => [wallet.key, wallet.name]))
    const planned = plannedTrades(now).sort((a, b) => a.at.getTime() - b.at.getTime())
    const trades: TradeRecord[] = []
    const equity: EquityPoint[] = []
    const pnlBars: PnlBarPoint[] = []
    const marks: PriceMarkRecord[] = []
    const openBuy = new Map<number, { qty: number; unitCost: number }>()
    let nextId = 1
    const previousClose = new Map<string, number>()

    const flows = [{ day: -14, walletKey: 'coinbase', amount: -2000 }]

    const runTrade = (trade: PlannedTrade, price: number) => {
        const book = books.get(trade.walletKey)
        if (!book) throw new Error(`Missing wallet ${trade.walletKey}`)
        const id = nextId++
        const result = applyTrade(book, trade, id, price)
        if (book.cash < -0.01) throw new Error(`${trade.walletKey} cash went negative (${book.cash})`)
        const fee = feeFor(trade.symbol, trade.qty * price)
        const record: TradeRecord = {
            id,
            walletKey: trade.walletKey,
            walletName: names.get(trade.walletKey) ?? trade.walletKey,
            mode: modes.get(trade.walletKey) ?? 'paper',
            symbol: trade.symbol,
            title: SPECS[trade.symbol].title,
            side: trade.side,
            qty: trade.qty,
            price,
            fee,
            executedAt: trade.at.toISOString(),
            recordedAt: new Date(trade.at.getTime() + 30_000).toISOString(),
            note: trade.note,
            pnl: result.pnl,
            pnlPct: result.pnlPct,
            status: trade.side === 'sell' ? 'closed' : 'open',
        }
        trades.push(record)
        if (trade.side === 'buy') openBuy.set(id, { qty: trade.qty, unitCost: (trade.qty * price + fee) / trade.qty })
        marks.push({ symbol: trade.symbol, price, at: trade.at.toISOString(), walletKey: null })
    }

    for (let day = -59; day <= -1; day++) {
        for (const trade of planned.filter((item) => item.day === day)) {
            runTrade(trade, trade.price ?? priceAt(trade.symbol, day))
        }
        for (const flow of flows.filter((item) => item.day === day)) {
            const book = books.get(flow.walletKey)
            if (!book) continue
            book.cash = roundTo(book.cash + flow.amount, 2)
        }
        const at = chicagoDateTime(now, day, 16, 0)
        for (const wallet of WALLETS) {
            const book = books.get(wallet.key)!
            const value = marketValue(book, (symbol) => priceAt(symbol, day))
            pushEquity(equity, wallet.key, at, value, 'day')
            const prev = previousClose.get(wallet.key)
            const flow = flows.filter((item) => item.day === day && item.walletKey === wallet.key).reduce((total, item) => total + item.amount, 0)
            if (prev != null) pushBar(pnlBars, wallet.key, at, value - prev - flow, 'day')
            previousClose.set(wallet.key, value)
        }
        for (const symbol of Object.keys(SPECS) as SymbolKey[]) {
            marks.push({ symbol, price: priceAt(symbol, day), at: at.toISOString(), walletKey: null })
        }
    }

    const stamps: Date[] = []
    const todayStart = startOfChicagoDay(now).getTime()
    for (let time = todayStart; time < now.getTime(); time += 3_600_000) stamps.push(new Date(time))
    if (stamps.length === 0 || stamps[stamps.length - 1]!.getTime() !== now.getTime()) stamps.push(now)
    const todayTrades = planned.filter((trade) => trade.day === 0)
    let tradeCursor = 0
    const hourPrev = new Map<string, number>(previousClose)

    for (const stamp of stamps) {
        while (tradeCursor < todayTrades.length && todayTrades[tradeCursor]!.at.getTime() <= stamp.getTime()) {
            const trade = todayTrades[tradeCursor]!
            runTrade(trade, trade.price ?? priceOnToday(trade.symbol, trade.at, now))
            tradeCursor += 1
        }
        for (const wallet of WALLETS) {
            const book = books.get(wallet.key)!
            const value = marketValue(book, (symbol) => priceOnToday(symbol, stamp, now))
            pushEquity(equity, wallet.key, stamp, value, 'hour')
            const prev = hourPrev.get(wallet.key)
            if (prev != null) pushBar(pnlBars, wallet.key, stamp, value - prev, 'hour')
            hourPrev.set(wallet.key, value)
            if (stamp === stamps[stamps.length - 1]) {
                pushEquity(equity, wallet.key, stamp, value, 'day')
                const close = previousClose.get(wallet.key)
                if (close != null) pushBar(pnlBars, wallet.key, stamp, value - close, 'day')
            }
        }
        if (stamp.getTime() !== now.getTime()) {
            for (const symbol of Object.keys(SPECS) as SymbolKey[]) {
                marks.push({ symbol, price: priceOnToday(symbol, stamp, now), at: stamp.toISOString(), walletKey: null })
            }
        }
    }

    const freshMarkAt = new Date(now.getTime() - 4 * 60_000)
    for (const symbol of Object.keys(SPECS) as SymbolKey[]) {
        marks.push({ symbol, price: priceOnToday(symbol, freshMarkAt, now), at: freshMarkAt.toISOString(), walletKey: null })
    }

    const latestMark = new Map<SymbolKey, PriceMarkRecord>()
    for (const mark of marks) {
        const key = mark.symbol as SymbolKey
        const prev = latestMark.get(key)
        if (!prev || prev.at < mark.at) latestMark.set(key, mark)
    }

    for (const tracked of openBuy.values()) tracked.qty = 0
    for (const book of books.values()) {
        for (const lots of book.lots.values()) {
            for (const lot of lots) {
                const tracked = openBuy.get(lot.tradeId)
                if (tracked) tracked.qty += lot.qty
            }
        }
    }

    for (const trade of trades) {
        if (trade.side !== 'buy') continue
        const tracked = openBuy.get(trade.id)
        if (!tracked || tracked.qty <= 1e-8) {
            trade.status = 'closed'
            trade.pnl = null
            trade.pnlPct = null
            continue
        }
        const mark = latestMark.get(trade.symbol as SymbolKey)
        trade.status = 'open'
        if (!mark) {
            trade.pnl = null
            trade.pnlPct = null
            continue
        }
        const pnl = roundTo(tracked.qty * mark.price - tracked.qty * tracked.unitCost, 2)
        trade.pnl = pnl
        trade.pnlPct = tracked.unitCost !== 0 ? roundTo(((mark.price - tracked.unitCost) / tracked.unitCost) * 100, 2) : 0
    }

    const positions: PositionRecord[] = []
    for (const wallet of WALLETS) {
        const book = books.get(wallet.key)!
        for (const [symbol, lots] of book.lots) {
            const qty = roundTo(
                lots.reduce((total, lot) => total + lot.qty, 0),
                8,
            )
            if (qty <= 0) continue
            const cost = lots.reduce((total, lot) => total + lot.qty * lot.unitCost, 0)
            const mark = latestMark.get(symbol)
            const avgPrice = roundTo(cost / qty, symbol === 'CPI-YES' ? 4 : 2)
            positions.push({
                walletKey: wallet.key,
                symbol,
                title: SPECS[symbol].title,
                qty,
                avgPrice,
                lastMark: mark ? mark.price : null,
                lastMarkAt: mark ? mark.at : null,
                unrealizedPnl: mark ? roundTo(qty * mark.price - cost, 2) : null,
                unmarked: !mark,
            })
        }
    }

    const cashByWallet: Record<string, number> = {}
    for (const wallet of WALLETS) cashByWallet[wallet.key] = books.get(wallet.key)!.cash

    const recorded = [...trades.map((trade) => trade.recordedAt), ...marks.map((mark) => mark.at)]
    recorded.sort()

    return {
        asOf: now.toISOString(),
        lastRecordedAt: recorded[recorded.length - 1] ?? now.toISOString(),
        wallets: WALLETS,
        trades,
        marks,
        equity,
        pnlBars,
        positions,
        cashByWallet,
    }
}
