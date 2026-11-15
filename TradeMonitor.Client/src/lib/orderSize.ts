export interface ProductSteps {
    baseIncrement: number
    quoteIncrement: number
    baseMinSize: number
    minMarketFunds: number
}

export interface SpendQuote {
    qty: number
    spend: number
    cost: number
    error: string | null
}

const PERCENT_PREFIX = 'trademonitor.live.spendPercent.'

function places(increment: number): number {
    const text = increment.toString()
    const exp = /e-(\d+)$/.exec(text)
    if (exp) return Number(exp[1])
    const dot = text.split('.')[1]
    return dot ? dot.length : 0
}

export function floorTo(value: number, increment: number): number {
    if (value <= 0) return 0
    if (!(increment > 0)) return value
    const steps = Math.floor(value / increment + 1e-8)
    const raw = steps * increment
    const digits = places(increment)
    const factor = 10 ** digits
    return Math.round(raw * factor) / factor
}

export function fromSpend(spend: number, price: number, cash: number, steps: ProductSteps): SpendQuote {
    if (!(price > 0)) return { qty: 0, spend: 0, cost: 0, error: 'Price must be greater than zero.' }
    const capped = Math.min(Math.max(spend, 0), Math.max(cash, 0))
    const spent = floorTo(capped, steps.quoteIncrement)
    const qty = floorTo(spent / price, steps.baseIncrement)
    const cost = qty * price
    if (steps.minMarketFunds > 0 && spent < steps.minMarketFunds)
        return { qty, spend: spent, cost, error: `Spend is below the ${steps.minMarketFunds} USD minimum.` }
    if (steps.baseMinSize > 0 && qty < steps.baseMinSize)
        return { qty, spend: spent, cost, error: `Quantity is below the minimum size of ${steps.baseMinSize}.` }
    if (qty <= 0) return { qty: 0, spend: spent, cost: 0, error: 'Quantity must be greater than zero.' }
    return { qty, spend: spent, cost, error: null }
}

export function sellAmount(amount: number, mode: 'usd' | 'qty', price: number, held: number, steps: ProductSteps): SpendQuote {
    const cap = mode === 'qty' ? held : held * Math.max(price, 0)
    const clamped = Math.min(Math.max(amount, 0), Math.max(cap, 0))
    const qty = mode === 'qty' ? floorTo(clamped, steps.baseIncrement) : fromSpend(clamped, price, cap, steps).qty
    const heldQty = floorTo(held, steps.baseIncrement)
    const cappedQty = Math.min(qty, heldQty > 0 ? heldQty : held)
    const spend = price > 0 ? cappedQty * price : 0
    if (steps.baseMinSize > 0 && cappedQty < steps.baseMinSize)
        return { qty: cappedQty, spend, cost: spend, error: `Quantity is below the minimum size of ${steps.baseMinSize}.` }
    if (steps.minMarketFunds > 0 && spend < steps.minMarketFunds)
        return { qty: cappedQty, spend, cost: spend, error: `Order is below the ${steps.minMarketFunds} USD minimum.` }
    if (cappedQty <= 0) return { qty: 0, spend: 0, cost: 0, error: 'Quantity must be greater than zero.' }
    return { qty: cappedQty, spend, cost: spend, error: null }
}

export function readSpendPercent(storage: { getItem(key: string): string | null }, wallet: string): number | null {
    if (!wallet) return null
    const raw = Number(storage.getItem(PERCENT_PREFIX + wallet))
    if (!Number.isFinite(raw)) return null
    return Math.min(100, Math.max(0, raw))
}

export function writeSpendPercent(storage: { setItem(key: string, value: string): void }, wallet: string, percent: number) {
    if (!wallet) return
    storage.setItem(PERCENT_PREFIX + wallet, String(Math.min(100, Math.max(0, percent))))
}
