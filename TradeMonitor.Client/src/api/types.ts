// Normalized ledger read models. Property names match the JSON the read API returns.
// Nullable money stays null: a missing price mark is not zero.

export type WalletModeName = 'real' | 'paper'
export type TradeSideName = 'buy' | 'sell'
export type TradeKindName = 'fill' | 'settlement'
export type PositionStatus = 'open' | 'closed'

export interface LedgerQuery {
    mode?: string | null
    wallet?: string | null
    from?: string | null
    to?: string | null
    includeArchived?: boolean
    symbol?: string | null
    status?: string | null
    side?: string | null
    outcome?: string | null
    q?: string | null
    orderBy?: string | null
    descending?: boolean | null
    take?: number | null
    skip?: number | null
    kind?: string | null
}

export interface PnlTotals {
    wallets: number
    baseCurrency: string | null
    mixedCurrencies: boolean
    totalValue: number
    valueChange: number
    cash: number
    swept: number
    openExposure: number
    unmarkedCost: number
    positionsValue: number
    valuedAtCost: boolean
    realizedPnl: number
    unrealizedPnl: number | null
    unrealizedChange: number | null
    pnl: number | null
    fees: number
    deposits: number
    withdrawals: number
    sweeps: number
    winRate: number | null
    wins: number
    losses: number
    scratches: number
    tradeCount: number
    openPositions: number
}

export interface EquityPoint {
    at: string
    totalValue: number
    cash: number
    positionsValue: number
    openExposure: number
    unmarkedCost: number
    swept: number
    unrealizedPnl: number | null
    valuedAtCost: boolean
}

export interface WalletCardModel {
    id: number
    key: string
    name: string
    mode: WalletModeName
    baseCurrency: string
    isArchived: boolean
    sortOrder: number
    totalValue: number
    valueChange: number
    cash: number
    swept: number
    openExposure: number
    unmarkedCost: number
    positionsValue: number
    valuedAtCost: boolean
    realizedPnl: number
    unrealizedPnl: number | null
    unrealizedChange: number | null
    pnl: number | null
    fees: number
    deposits: number
    withdrawals: number
    sweeps: number
    winRate: number | null
    wins: number
    losses: number
    scratches: number
    tradeCount: number
    openPositions: number
    lastActivityAt: string | null
    sparkline: EquityPoint[]
}

export interface SummaryResponse {
    totals: PnlTotals
    modes: { mode: string; totals: PnlTotals }[]
    wallets: WalletCardModel[]
}

export interface WalletEquity {
    wallet: string
    name: string
    mode: WalletModeName
    points: EquityPoint[]
}

export interface EquityResponse {
    wallets: WalletEquity[]
    total: EquityPoint[]
}

export interface PnlDay {
    date: string
    realizedPnl: number
    unrealizedChange: number | null
    pnl: number | null
    fees: number
    deposits: number
    withdrawals: number
    sweeps: number
    totalValue: number
    valueChange: number
    cash: number
    positionsValue: number
    openExposure: number
    unmarkedCost: number
    valuedAtCost: boolean
    trades: number
}

export interface PnlDailyResponse {
    results: PnlDay[]
    wallets: { wallet: string; name: string; mode: WalletModeName; days: PnlDay[] }[]
}

export interface SymbolPnlRow {
    symbol: string
    realizedPnl: number
    unrealizedPnl: number | null
    unrealizedChange: number | null
    pnl: number | null
    fees: number
    trades: number
    wins: number
    losses: number
    qty: number
    marketValue: number
    valuedAtCost: boolean
}

export interface SymbolPnlResponse {
    results: SymbolPnlRow[]
}

export interface FeesResponse {
    total: number
    days: { date: string; fees: number }[]
}

export interface PositionRow {
    wallet: string
    walletName: string
    mode: WalletModeName
    symbol: string
    status: PositionStatus
    qty: number
    avgPrice: number
    costBasis: number
    lastMarkPrice: number | null
    lastMarkAt: string | null
    markIsStale: boolean
    markStaleAfterMinutes: number
    unrealizedPnl: number | null
    unrealizedPnlPercent: number | null
    realizedPnl: number
    feesPaid: number
    marketValue: number
    valuedAtCost: boolean
    targetPrice: number | null
    distanceToTarget: number | null
    distanceToTargetPercent: number | null
}

export interface PositionsResponse {
    markStaleAfterMinutes: number
    results: PositionRow[]
}

export interface TradeRow {
    id: number
    wallet: string
    walletName: string
    mode: WalletModeName
    symbol: string
    title: string | null
    side: TradeSideName
    kind: TradeKindName
    qty: number
    price: number
    fee: number
    executedAt: string
    recordedAt: string
    note: string | null
    status: PositionStatus
    buyPrice: number
    currentOrExitPrice: number | null
    priceKind: string
    exitPrice: number | null
    lastMarkPrice: number | null
    lastMarkAt: string | null
    realizedPnl: number
    unrealizedPnl: number | null
    pnl: number | null
    pnlPercent: number | null
    remainingQty: number
    valuedAtCost: boolean
    targetPrice: number | null
    orderRef: string | null
    clientId: string
}

export interface TradesResponse {
    total: number
    skip: number
    take: number
    results: TradeRow[]
}

export interface TradeFill {
    tradeId: number
    side: TradeSideName
    symbol: string
    qty: number
    price: number
    buyFee: number
    sellFee: number
    cost: number
    proceeds: number
    realizedPnl: number
    executedAt: string
}

export interface ChartMarkerPoint {
    tradeId: number
    side: TradeSideName
    at: string
    price: number
    qty: number
}

export interface PriceMarkRow {
    id: number
    clientId: string | null
    symbol: string
    price: number
    at: string
    source: string | null
    wallet: string | null
    recordedAt: string
}

export interface TradeDetailResponse {
    result: TradeRow
    fills: TradeFill[]
    markers: ChartMarkerPoint[]
    priceMarks: PriceMarkRow[]
    hasPriceMarks: boolean
    chartFrom: string
    chartTo: string
}

export interface WalletInfo {
    id: number
    key: string
    name: string
    mode: WalletModeName
    venue: string
    baseCurrency: string
    note: string
    isArchived: boolean
    sortOrder: number
    createdAt: string | null
}

export interface WalletsResponse {
    results: WalletInfo[]
}

export interface WalletResponse {
    result: WalletInfo
}

export interface PositionBalance {
    symbol: string
    qty: number
    costBasis: number
    avgPrice: number
    lastMarkPrice: number | null
    lastMarkAt: string | null
    unrealizedPnl: number | null
    unrealizedPnlPercent: number | null
    marketValue: number
    valuedAtCost: boolean
    realizedPnl: number
    feesPaid: number
}

export interface WalletBalances {
    id: number
    key: string
    name: string
    mode: WalletModeName
    baseCurrency: string
    isArchived: boolean
    cash: number
    swept: number
    fees: number
    realizedPnl: number
    unrealizedPnl: number | null
    positionsValue: number
    totalValue: number
    cashIsNegative: boolean
    hasDeposits: boolean
    valuedAtCost: boolean
    positions: PositionBalance[]
}

export interface WalletBalancesResponse {
    result: WalletBalances
}

export interface HistoryEntry {
    wallet: string
    kind: string
    id: number
    clientId: string
    at: string
    symbol: string | null
    side: TradeSideName | null
    qty: number | null
    price: number | null
    fee: number | null
    amount: number | null
    currency: string | null
    title: string | null
    note: string | null
    orderRef: string | null
    targetPrice: number | null
    recordedAt: string
}

export interface WalletHistoryResponse {
    total: number
    skip: number
    take: number
    results: HistoryEntry[]
}

export interface PriceMarksResponse {
    total: number
    skip: number
    take: number
    results: PriceMarkRow[]
}
