export interface OpenLot {
    tradeId: number
    qty: number
    buyPrice: number
    cost: number
    executedAt?: string | null
}

export interface QuotedLot extends OpenLot {
    price: number | null
    unrealizedPnl: number | null
    unrealizedPnlPercent: number | null
}

export interface AverageQuote {
    openQty: number
    openCost: number
    avgCost: number
    sellQty: number
    price: number | null
    matchedCost: number
    profit: number | null
}

/** Profit is remaining quantity times price, minus remaining cost. Cost includes buy fees. */
export function quoteLots(lots: OpenLot[], price: number | null): QuotedLot[] {
    const quoted = lots
        .filter((lot) => lot.qty > 0)
        .map((lot) => {
            const unrealizedPnl = price == null ? null : lot.qty * price - lot.cost
            const unrealizedPnlPercent = unrealizedPnl != null && lot.cost !== 0 ? (unrealizedPnl / lot.cost) * 100 : null
            return { ...lot, price, unrealizedPnl, unrealizedPnlPercent }
        })
    quoted.sort((a, b) => {
        const left = a.unrealizedPnl ?? Number.NEGATIVE_INFINITY
        const right = b.unrealizedPnl ?? Number.NEGATIVE_INFINITY
        if (right !== left) return right - left
        const time = (a.executedAt ?? '').localeCompare(b.executedAt ?? '')
        if (time !== 0) return time
        return a.tradeId - b.tradeId
    })
    return quoted
}

export function averageQuote(openQty: number, openCost: number, sellQty: number, price: number | null): AverageQuote {
    const qty = sellQty < 0 ? 0 : sellQty
    const matchedCost = openQty === 0 || qty === 0 ? 0 : (openCost * qty) / openQty
    return {
        openQty,
        openCost,
        avgCost: openQty === 0 ? 0 : openCost / openQty,
        sellQty: qty,
        price,
        matchedCost,
        profit: price == null ? null : qty * price - matchedCost,
    }
}
