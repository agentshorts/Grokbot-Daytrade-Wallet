import { LedgerReadError } from '@/api/errors'
import { asRecord, pick, readFailure, readMoney, readTime } from '@/api/parse'

export interface LiveProduct {
    productId: string
    base: string
    quote: string
    name: string
    price: number | null
}

export interface LiveCandle {
    start: number
    open: number
    high: number
    low: number
    close: number
}

export interface LiveTick {
    productId: string
    price: number
    at: string | null
}

export interface TrackedSymbol {
    productId: string
    name: string
    sortOrder: number
    listed: boolean
}

export interface TrackedSymbols {
    catalogLoaded: boolean
    results: TrackedSymbol[]
}

export interface LiveTradeMark {
    tradeId: number
    side: LiveSide
    kind: string
    qty: number
    price: number
    fee: number
    executedAt: string | null
    simulated: boolean
}

export interface LiveLot {
    tradeId: number
    symbol: string
    qty: number
    buyPrice: number
    cost: number
    price: number | null
    unrealizedPnl: number | null
    unrealizedPnlPercent: number | null
    executedAt: string | null
}

export interface LiveAverage {
    openQty: number
    openCost: number
    avgCost: number
    sellQty: number
    price: number | null
    matchedCost: number
    profit: number | null
}

export type LiveSide = 'Buy' | 'Sell'
export type LiveOrderType = 'Market' | 'Limit' | 'StopLoss' | 'TakeProfit'
export type LiveCosting = 'Fifo' | 'Lot' | 'Average'

export interface LiveOrder {
    id: number
    symbol: string
    side: LiveSide
    type: LiveOrderType
    qty: number
    triggerPrice: number | null
    costing: LiveCosting
    lotId: number | null
    status: string
    lastError: string | null
    createdAt: string | null
}

export interface LiveBook {
    wallet: string
    walletName: string
    mode: 'Paper' | 'Real'
    simulated: boolean
    simulationId: string | null
    symbol: string
    price: number | null
    cash: number
    realizedPnl: number
    lots: LiveLot[]
    average: LiveAverage
    orders: LiveOrder[]
    tradeId: number | null
}

export interface PaperWalletResult {
    key: string
    name: string
    cash: number
}

export interface PlaceOrderInput {
    symbol: string
    side: LiveSide
    type: LiveOrderType
    qty: number
    triggerPrice?: number | null
    costing?: LiveCosting
    lotId?: number | null
}

export interface LiveClient {
    products(): Promise<LiveProduct[]>
    candles(product: string, granularity: string): Promise<LiveCandle[]>
    bars(product: string, granularity: string): Promise<LiveCandle[]>
    ticker(product: string): Promise<LiveTick | null>
    tracked(): Promise<TrackedSymbols>
    addSymbol(product: string): Promise<TrackedSymbols>
    removeSymbol(product: string): Promise<TrackedSymbols>
    reorderSymbols(products: string[]): Promise<TrackedSymbols>
    trades(wallet: string, symbol: string, simulationId?: string | null): Promise<LiveTradeMark[]>
    createPaperWallet(name: string, startingCash: number): Promise<PaperWalletResult>
    book(wallet: string, symbol: string, sellQty?: number): Promise<LiveBook>
    placePaper(wallet: string, order: PlaceOrderInput): Promise<LiveBook>
    cancelPaper(id: number): Promise<LiveBook>
    movePaper(id: number, triggerPrice: number): Promise<LiveBook>
    openSimulation(wallet: string, symbol: string): Promise<LiveBook>
    simulation(id: string, symbol: string, sellQty?: number): Promise<LiveBook>
    placeSimulation(id: string, order: PlaceOrderInput): Promise<LiveBook>
    cancelSimulation(id: string, orderId: number): Promise<LiveBook>
    moveSimulation(id: string, orderId: number, triggerPrice: number): Promise<LiveBook>
    resetSimulation(id: string, symbol: string): Promise<LiveBook>
}

function text(record: Record<string, unknown>, name: string): string {
    const value = pick(record, name)
    return typeof value === 'string' ? value : ''
}

function num(record: Record<string, unknown>, name: string): number {
    return readMoney(pick(record, name)) ?? 0
}

function side(value: string): LiveSide {
    return value.toLowerCase() === 'sell' ? 'Sell' : 'Buy'
}

function orderType(value: string): LiveOrderType {
    const key = value.toLowerCase()
    if (key === 'limit') return 'Limit'
    if (key === 'stoploss' || key === 'stop-loss') return 'StopLoss'
    if (key === 'takeprofit' || key === 'take-profit') return 'TakeProfit'
    return 'Market'
}

function costing(value: string): LiveCosting {
    const key = value.toLowerCase()
    if (key === 'lot') return 'Lot'
    if (key === 'average') return 'Average'
    return 'Fifo'
}

function parseLot(value: unknown): LiveLot | null {
    const record = asRecord(value)
    if (!record) return null
    return {
        tradeId: num(record, 'tradeId'),
        symbol: text(record, 'symbol'),
        qty: num(record, 'qty'),
        buyPrice: num(record, 'buyPrice'),
        cost: num(record, 'cost'),
        price: readMoney(pick(record, 'price')),
        unrealizedPnl: readMoney(pick(record, 'unrealizedPnl')),
        unrealizedPnlPercent: readMoney(pick(record, 'unrealizedPnlPercent')),
        executedAt: text(record, 'executedAt') || null,
    }
}

function parseOrder(value: unknown): LiveOrder | null {
    const record = asRecord(value)
    if (!record) return null
    const lot = readMoney(pick(record, 'lotId'))
    return {
        id: num(record, 'id'),
        symbol: text(record, 'symbol'),
        side: side(text(record, 'side')),
        type: orderType(text(record, 'type')),
        qty: num(record, 'qty'),
        triggerPrice: readMoney(pick(record, 'triggerPrice')),
        costing: costing(text(record, 'costing')),
        lotId: lot != null && lot > 0 ? lot : null,
        status: text(record, 'status'),
        lastError: text(record, 'lastError') || null,
        createdAt: text(record, 'createdAt') || null,
    }
}

export function parseBook(body: unknown): LiveBook {
    const record = asRecord(body) ?? {}
    const average = asRecord(pick(record, 'average')) ?? {}
    const lots = Array.isArray(pick(record, 'lots')) ? (pick(record, 'lots') as unknown[]) : []
    const orders = Array.isArray(pick(record, 'orders')) ? (pick(record, 'orders') as unknown[]) : []
    const mode = text(record, 'mode').toLowerCase() === 'real' ? 'Real' : 'Paper'
    return {
        wallet: text(record, 'wallet'),
        walletName: text(record, 'walletName'),
        mode,
        simulated: pick(record, 'simulated') === true,
        simulationId: text(record, 'simulationId') || null,
        symbol: text(record, 'symbol'),
        price: readMoney(pick(record, 'price')),
        cash: num(record, 'cash'),
        realizedPnl: num(record, 'realizedPnl'),
        lots: lots.map(parseLot).filter((lot): lot is LiveLot => lot != null),
        average: {
            openQty: num(average, 'openQty'),
            openCost: num(average, 'openCost'),
            avgCost: num(average, 'avgCost'),
            sellQty: num(average, 'sellQty'),
            price: readMoney(pick(average, 'price')),
            matchedCost: num(average, 'matchedCost'),
            profit: readMoney(pick(average, 'profit')),
        },
        orders: orders.map(parseOrder).filter((order): order is LiveOrder => order != null && order.status.toLowerCase() !== 'cancelled'),
        tradeId: readMoney(pick(record, 'tradeId')),
    }
}

function parseProducts(body: unknown): LiveProduct[] {
    const record = asRecord(body)
    const rows = record && Array.isArray(pick(record, 'results')) ? (pick(record, 'results') as unknown[]) : []
    return rows.flatMap((row) => {
        const item = asRecord(row)
        if (!item) return []
        const productId = text(item, 'productId')
        if (!productId) return []
        return [
            {
                productId,
                base: text(item, 'base'),
                quote: text(item, 'quote') || 'USD',
                name: text(item, 'name') || productId,
                price: readMoney(pick(item, 'price')),
            },
        ]
    })
}

function parseCandles(body: unknown): LiveCandle[] {
    const record = asRecord(body)
    const rows = record && Array.isArray(pick(record, 'results')) ? (pick(record, 'results') as unknown[]) : []
    return rows.flatMap((row) => {
        const item = asRecord(row)
        if (!item) return []
        return [
            {
                start: num(item, 'start'),
                open: num(item, 'open'),
                high: num(item, 'high'),
                low: num(item, 'low'),
                close: num(item, 'close'),
            },
        ]
    })
}

function parseTracked(body: unknown): TrackedSymbols {
    const record = asRecord(body) ?? {}
    const rows = Array.isArray(pick(record, 'results')) ? (pick(record, 'results') as unknown[]) : []
    return {
        catalogLoaded: pick(record, 'catalogLoaded') === true,
        results: rows.flatMap((row) => {
            const item = asRecord(row)
            if (!item) return []
            const productId = text(item, 'productId')
            if (!productId) return []
            return [
                {
                    productId,
                    name: text(item, 'name') || productId,
                    sortOrder: num(item, 'sortOrder'),
                    listed: pick(item, 'listed') !== false,
                },
            ]
        }),
    }
}

function parseTrades(body: unknown): LiveTradeMark[] {
    const record = asRecord(body)
    const rows = record && Array.isArray(pick(record, 'results')) ? (pick(record, 'results') as unknown[]) : []
    return rows.flatMap((row) => {
        const item = asRecord(row)
        if (!item) return []
        const price = readMoney(pick(item, 'price'))
        if (price == null || price <= 0) return []
        return [
            {
                tradeId: num(item, 'tradeId'),
                side: side(text(item, 'side')),
                kind: text(item, 'kind') || 'Fill',
                qty: num(item, 'qty'),
                price,
                fee: num(item, 'fee'),
                executedAt: readTime(pick(item, 'executedAt')),
                simulated: pick(item, 'simulated') === true,
            },
        ]
    })
}

async function send(path: string, method: 'GET' | 'POST' | 'DELETE', body?: unknown): Promise<unknown> {
    const response = await fetch(path, {
        method,
        credentials: 'same-origin',
        redirect: 'manual',
        headers: { Accept: 'application/json', ...(body ? { 'Content-Type': 'application/json' } : {}) },
        ...(body ? { body: JSON.stringify(body) } : {}),
    })
    if (response.type === 'opaqueredirect' || (response.status >= 300 && response.status < 400)) {
        throw new LedgerReadError(401, 'Unauthorized', 'Sign in as an admin to use Live.')
    }
    const textBody = await response.text()
    const parsed = textBody ? JSON.parse(textBody) : null
    if (!response.ok) throw readFailure(parsed, response.status)
    return parsed
}

function orderBody(order: PlaceOrderInput): Record<string, unknown> {
    return {
        symbol: order.symbol,
        side: order.side,
        type: order.type,
        qty: order.qty,
        triggerPrice: order.type === 'Market' ? null : order.triggerPrice,
        costing: order.costing ?? 'Fifo',
        lotId: order.lotId ?? null,
    }
}

export function createLiveClient(): LiveClient {
    return {
        async products() {
            return parseProducts(await send('/api/live/products', 'GET'))
        },
        async candles(product, granularity) {
            const params = new URLSearchParams({ product, granularity })
            return parseCandles(await send(`/api/live/candles?${params}`, 'GET'))
        },
        async bars(product, granularity) {
            const params = new URLSearchParams({ product, granularity })
            return parseCandles(await send(`/api/live/bars?${params}`, 'GET'))
        },
        async ticker(product) {
            const body = asRecord(await send(`/api/live/ticker?product=${encodeURIComponent(product)}`, 'GET'))
            const result = body ? asRecord(pick(body, 'result')) : null
            if (!result) return null
            const price = readMoney(pick(result, 'price'))
            if (price == null) return null
            return { productId: text(result, 'productId') || product, price, at: readTime(pick(result, 'at')) }
        },
        async tracked() {
            return parseTracked(await send('/api/live/tracked', 'GET'))
        },
        async addSymbol(product) {
            return parseTracked(await send('/api/live/tracked', 'POST', { product }))
        },
        async removeSymbol(product) {
            return parseTracked(await send(`/api/live/tracked/${encodeURIComponent(product)}`, 'DELETE'))
        },
        async reorderSymbols(products) {
            return parseTracked(await send('/api/live/tracked/reorder', 'POST', { products }))
        },
        async trades(wallet, symbol, simulationId) {
            const params = new URLSearchParams({ wallet, symbol })
            if (simulationId) params.set('simulationId', simulationId)
            return parseTrades(await send(`/api/live/trades?${params}`, 'GET'))
        },
        async createPaperWallet(name, startingCash) {
            const body = asRecord(await send('/api/live/paper-wallets', 'POST', { name, startingCash })) ?? {}
            const result = asRecord(pick(body, 'result')) ?? {}
            return { key: text(result, 'key'), name: text(result, 'name') || name, cash: num(body, 'cash') }
        },
        async book(wallet, symbol, sellQty) {
            const params = new URLSearchParams({ wallet, symbol })
            if (sellQty != null) params.set('sellQty', String(sellQty))
            return parseBook(await send(`/api/live/book?${params}`, 'GET'))
        },
        async placePaper(wallet, order) {
            return parseBook(await send('/api/live/paper-orders', 'POST', { wallet, ...orderBody(order) }))
        },
        async cancelPaper(id) {
            return parseBook(await send(`/api/live/paper-orders/${id}/cancel`, 'POST', {}))
        },
        async movePaper(id, triggerPrice) {
            return parseBook(await send(`/api/live/paper-orders/${id}/move`, 'POST', { triggerPrice }))
        },
        async openSimulation(wallet, symbol) {
            return parseBook(await send('/api/live/simulations', 'POST', { wallet, symbol }))
        },
        async simulation(id, symbol, sellQty) {
            const params = new URLSearchParams({ symbol })
            if (sellQty != null) params.set('sellQty', String(sellQty))
            return parseBook(await send(`/api/live/simulations/${encodeURIComponent(id)}?${params}`, 'GET'))
        },
        async placeSimulation(id, order) {
            return parseBook(await send(`/api/live/simulations/${encodeURIComponent(id)}/orders`, 'POST', orderBody(order)))
        },
        async cancelSimulation(id, orderId) {
            return parseBook(await send(`/api/live/simulations/${encodeURIComponent(id)}/orders/${orderId}/cancel`, 'POST', {}))
        },
        async moveSimulation(id, orderId, triggerPrice) {
            return parseBook(await send(`/api/live/simulations/${encodeURIComponent(id)}/orders/${orderId}/move`, 'POST', { triggerPrice }))
        },
        async resetSimulation(id, symbol) {
            return parseBook(await send(`/api/live/simulations/${encodeURIComponent(id)}/reset`, 'POST', { symbol }))
        },
    }
}

export const live = createLiveClient()
