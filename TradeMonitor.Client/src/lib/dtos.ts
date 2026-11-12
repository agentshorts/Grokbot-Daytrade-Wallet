/* Options:
Date: 2026-11-12 15:42:19
Version: 10.50
Tip: To override a DTO option, remove "//" prefix before updating
BaseUrl: https://localhost:5051

//GlobalNamespace: 
//MakePropertiesOptional: False
//AddServiceStackTypes: True
//AddResponseStatus: False
//AddImplicitVersion: 
//AddDescriptionAsComments: True
//IncludeTypes: 
//ExcludeTypes: 
//DefaultImports: 
*/

// @ts-nocheck

export interface IReturn<T>
{
    createResponse(): T;
}

export interface IReturnVoid
{
    createResponse(): void;
}

export interface IHasSessionId
{
    sessionId?: string;
}

export interface IHasBearerToken
{
    bearerToken?: string;
}

export interface IPost
{
}

export interface IPatch
{
}

export interface IGet
{
}

export enum WalletMode
{
    Paper = 'Paper',
    Real = 'Real',
}

export enum TradeSide
{
    Buy = 'Buy',
    Sell = 'Sell',
}

export enum TradeKind
{
    Fill = 'Fill',
    Settlement = 'Settlement',
}

export enum CostingMethod
{
    Fifo = 'Fifo',
    Lot = 'Lot',
    Average = 'Average',
}

export enum TransferKind
{
    Deposit = 'Deposit',
    Withdrawal = 'Withdrawal',
    Sweep = 'Sweep',
}

export class PriceMarkWrite
{
    public symbol?: string;
    public price: number;
    public at?: string;
    public wallet?: string;
    public clientId?: string;
    public source?: string;

    public constructor(init?: Partial<PriceMarkWrite>) { (Object as any).assign(this, init); }
}

export enum LiveOrderType
{
    Market = 'Market',
    Limit = 'Limit',
    StopLoss = 'StopLoss',
    TakeProfit = 'TakeProfit',
}

// @DataContract
export class QueryBase
{
    // @DataMember(Order=1)
    public skip?: number;

    // @DataMember(Order=2)
    public take?: number;

    // @DataMember(Order=3)
    public orderBy?: string;

    // @DataMember(Order=4)
    public orderByDesc?: string;

    // @DataMember(Order=5)
    public include?: string;

    // @DataMember(Order=6)
    public fields?: string;

    // @DataMember(Order=7)
    public meta?: { [index:string]: string; };

    public constructor(init?: Partial<QueryBase>) { (Object as any).assign(this, init); }
}

export class QueryDb<T> extends QueryBase
{

    public constructor(init?: Partial<QueryDb<T>>) { super(init); (Object as any).assign(this, init); }
}

export class User
{
    public id: string;
    public userName: string;
    public firstName?: string;
    public lastName?: string;
    public displayName?: string;
    public profileUrl?: string;

    public constructor(init?: Partial<User>) { (Object as any).assign(this, init); }
}

export class Wallet
{
    public id: number;
    // @Required()
    // @StringLength(128)
    public key: string;

    // @Required()
    // @StringLength(200)
    public name: string;

    public mode: WalletMode;
    // @StringLength(64)
    public venue?: string;

    // @Required()
    // @StringLength(16)
    public baseCurrency: string;

    public note?: string;
    public isArchived: boolean;
    public sortOrder: number;
    public createdAt: string;

    public constructor(init?: Partial<Wallet>) { (Object as any).assign(this, init); }
}

// @DataContract
export class ResponseError
{
    // @DataMember(Order=1)
    public errorCode: string;

    // @DataMember(Order=2)
    public fieldName: string;

    // @DataMember(Order=3)
    public message: string;

    // @DataMember(Order=4)
    public meta?: { [index:string]: string; };

    public constructor(init?: Partial<ResponseError>) { (Object as any).assign(this, init); }
}

// @DataContract
export class ResponseStatus
{
    // @DataMember(Order=1)
    public errorCode: string;

    // @DataMember(Order=2)
    public message?: string;

    // @DataMember(Order=3)
    public stackTrace?: string;

    // @DataMember(Order=4)
    public errors?: ResponseError[];

    // @DataMember(Order=5)
    public meta?: { [index:string]: string; };

    public constructor(init?: Partial<ResponseStatus>) { (Object as any).assign(this, init); }
}

export class PositionBalance
{
    public symbol: string;
    public qty: number;
    public costBasis: number;
    public avgPrice: number;
    public lastMarkPrice?: number;
    public lastMarkAt?: string;
    public unrealizedPnl?: number;
    /** @description Unrealized P&L as a percent of cost basis. Null when unmarked or cost is zero. */
    public unrealizedPnlPercent?: number;
    public marketValue: number;
    public valuedAtCost: boolean;
    public realizedPnl: number;
    public feesPaid: number;

    public constructor(init?: Partial<PositionBalance>) { (Object as any).assign(this, init); }
}

export class WalletBalances
{
    public id: number;
    public key: string;
    public name: string;
    public mode: WalletMode;
    public baseCurrency: string;
    public isArchived: boolean;
    public cash: number;
    public swept: number;
    public fees: number;
    public realizedPnl: number;
    /** @description Null when an open position has no price mark. Zero when nothing is open. */
    public unrealizedPnl?: number;
    public positionsValue: number;
    public totalValue: number;
    public cashIsNegative: boolean;
    public hasDeposits: boolean;
    /** @description True when an open position is valued at cost because it has no price mark. */
    public valuedAtCost: boolean;
    public positions: PositionBalance[] = [];

    public constructor(init?: Partial<WalletBalances>) { (Object as any).assign(this, init); }
}

export class WalletHistoryEntry
{
    public wallet: string;
    public kind: string;
    public id: number;
    public clientId: string;
    public at: string;
    public symbol?: string;
    public side?: TradeSide;
    public qty?: number;
    public price?: number;
    public fee?: number;
    public amount?: number;
    public currency?: string;
    public title?: string;
    public note?: string;
    public orderRef?: string;
    public targetPrice?: number;
    public recordedAt: string;
    public costing: CostingMethod;
    public lotId?: number;

    public constructor(init?: Partial<WalletHistoryEntry>) { (Object as any).assign(this, init); }
}

export class TradeView
{
    public id: number;
    public wallet: string;
    public walletName: string;
    public mode: WalletMode;
    public walletId: number;
    public clientId: string;
    public symbol: string;
    public title?: string;
    public side: TradeSide;
    /** @description fill or settlement. */
    public kind: TradeKind;
    public qty: number;
    public price: number;
    public fee: number;
    public feeCurrency?: string;
    public executedAt: string;
    public orderRef?: string;
    public targetPrice?: number;
    public note?: string;
    public recordedAt: string;
    /** @description Fill price for a buy. Quantity-weighted buy price for a sell, before fees. Quote currency per unit. */
    public buyPrice: number;
    /** @description Exit price when the fill is closed, latest mark when it is open, null when an open fill has no mark. Quote currency per unit. */
    public currentOrExitPrice?: number;
    /** @description exit, mark, or unknown. */
    public priceKind: string;
    /** @description open or closed. */
    public status: string;
    /** @description Weighted exit price of the closed quantity. Null when none of the fill has been sold. */
    public exitPrice?: number;
    public lastMarkPrice?: number;
    public lastMarkAt?: string;
    /** @description Matched cost of closed quantity in the wallet base currency, including allocated buy fees. */
    public matchedCost: number;
    /** @description Proceeds of closed quantity in the wallet base currency, after allocated sell fees. */
    public proceeds: number;
    public realizedPnl: number;
    /** @description Null when quantity is still open and the symbol has no price mark. */
    public unrealizedPnl?: number;
    /** @description Realized plus unrealized P&L. Null when unrealized P&L is unknown. */
    public pnl?: number;
    /** @description P&L as a percent of cost. Null when P&L is unknown or cost is zero. */
    public pnlPercent?: number;
    public remainingQty: number;
    public valuedAtCost: boolean;
    /** @description fifo, lot, or average. Fifo matches oldest lots first. */
    public costing: CostingMethod;
    /** @description Buy trade id when costing is lot. */
    public lotId?: number;

    public constructor(init?: Partial<TradeView>) { (Object as any).assign(this, init); }
}

export class PriceMarkView
{
    public id: number;
    public clientId?: string;
    public symbol: string;
    public price: number;
    public at: string;
    public source?: string;
    public wallet?: string;
    public walletId?: number;
    public recordedAt: string;

    public constructor(init?: Partial<PriceMarkView>) { (Object as any).assign(this, init); }
}

export class Trade
{
    public id: number;
    public walletId: number;
    // @Required()
    // @StringLength(128)
    public clientId: string;

    // @Required()
    // @StringLength(64)
    public symbol: string;

    // @StringLength(200)
    public title?: string;

    public side: TradeSide;
    public kind: TradeKind;
    public qty: number;
    public price: number;
    public fee: number;
    // @StringLength(16)
    public feeCurrency?: string;

    public executedAt: string;
    // @StringLength(128)
    public orderRef?: string;

    public targetPrice?: number;
    public note?: string;
    public costing: CostingMethod;
    public lotId?: number;
    public recordedAt: string;

    public constructor(init?: Partial<Trade>) { (Object as any).assign(this, init); }
}

export class Transfer
{
    public id: number;
    public walletId: number;
    // @Required()
    // @StringLength(128)
    public clientId: string;

    public kind: TransferKind;
    public amount: number;
    // @Required()
    // @StringLength(16)
    public currency: string;

    public at: string;
    public note?: string;
    public recordedAt: string;

    public constructor(init?: Partial<Transfer>) { (Object as any).assign(this, init); }
}

export class PriceMark
{
    public id: number;
    // @StringLength(128)
    public clientId?: string;

    // @Required()
    // @StringLength(64)
    public symbol: string;

    public price: number;
    public at: string;
    // @StringLength(64)
    public source?: string;

    public walletId?: number;
    public recordedAt: string;

    public constructor(init?: Partial<PriceMark>) { (Object as any).assign(this, init); }
}

export class RecordedPriceMark
{
    public result: PriceMark;
    public alreadyRecorded: boolean;

    public constructor(init?: Partial<RecordedPriceMark>) { (Object as any).assign(this, init); }
}

export class LiveProduct
{
    public productId: string;
    public base: string;
    public quote: string;
    public name: string;
    public price?: number;

    public constructor(init?: Partial<LiveProduct>) { (Object as any).assign(this, init); }
}

export class LiveCandle
{
    public start: number;
    public open: number;
    public high: number;
    public low: number;
    public close: number;

    public constructor(init?: Partial<LiveCandle>) { (Object as any).assign(this, init); }
}

export class LiveTick
{
    public productId: string;
    public price: number;
    public at: string;

    public constructor(init?: Partial<LiveTick>) { (Object as any).assign(this, init); }
}

export class LiveLot
{
    public tradeId: number;
    public symbol: string;
    public qty: number;
    public buyPrice: number;
    public cost: number;
    public price?: number;
    public unrealizedPnl?: number;
    public unrealizedPnlPercent?: number;
    public executedAt: string;

    public constructor(init?: Partial<LiveLot>) { (Object as any).assign(this, init); }
}

export class LiveAverageQuote
{
    public openQty: number;
    public openCost: number;
    public avgCost: number;
    public sellQty: number;
    public price?: number;
    public matchedCost: number;
    public profit?: number;

    public constructor(init?: Partial<LiveAverageQuote>) { (Object as any).assign(this, init); }
}

export enum LiveOrderStatus
{
    Open = 'Open',
    Filled = 'Filled',
    Cancelled = 'Cancelled',
}

export class LiveOrder
{
    public id: number;
    public symbol: string;
    public side: TradeSide;
    public type: LiveOrderType;
    public qty: number;
    public triggerPrice?: number;
    public costing: CostingMethod;
    public lotId?: number;
    public status: LiveOrderStatus;
    public lastError?: string;
    public createdAt: string;

    public constructor(init?: Partial<LiveOrder>) { (Object as any).assign(this, init); }
}

export class PnlTotals
{
    public wallets: number;
    public baseCurrency?: string;
    public mixedCurrencies: boolean;
    /** @description Cash plus open positions. Unmarked positions are included at cost. */
    public totalValue: number;
    /** @description Change in total value across the range. Includes deposits, withdrawals, and sweeps. */
    public valueChange: number;
    public cash: number;
    public swept: number;
    /** @description Open quantity marked at the latest applicable price. Unmarked quantity is not included here. */
    public openExposure: number;
    /** @description Cost of open positions that have no price mark. This is not zero P&L. */
    public unmarkedCost: number;
    public positionsValue: number;
    public valuedAtCost: boolean;
    /** @description Realized P&L in the wallet base currency for sells in the range, after fees. */
    public realizedPnl: number;
    /** @description Unrealized P&L as of the end of the range. Null when an open position has no price mark. */
    public unrealizedPnl?: number;
    /** @description Change in unrealized P&L across the range. Null when either end is unknown. */
    public unrealizedChange?: number;
    /** @description Realized P&L plus unrealized change. Null when unrealized change is unknown. Transfers are not included. */
    public pnl?: number;
    /** @description Fees in the wallet base currency for fills in the range. */
    public fees: number;
    public deposits: number;
    public withdrawals: number;
    public sweeps: number;
    /** @description Percent of sell fills in the range with positive realized P&L. Breakeven sells are excluded. Null when no sell has a gain or a loss. */
    public winRate?: number;
    public wins: number;
    public losses: number;
    public scratches: number;
    public tradeCount: number;
    public openPositions: number;

    public constructor(init?: Partial<PnlTotals>) { (Object as any).assign(this, init); }
}

export class ModePnl
{
    /** @description all, paper, or real. */
    public mode: string;
    public totals: PnlTotals;

    public constructor(init?: Partial<ModePnl>) { (Object as any).assign(this, init); }
}

export class EquityPoint
{
    public at: string;
    public totalValue: number;
    public cash: number;
    public positionsValue: number;
    public openExposure: number;
    public unmarkedCost: number;
    public swept: number;
    public unrealizedPnl?: number;
    public valuedAtCost: boolean;

    public constructor(init?: Partial<EquityPoint>) { (Object as any).assign(this, init); }
}

export class WalletCard
{
    public id: number;
    public key: string;
    public name: string;
    public mode: WalletMode;
    public baseCurrency: string;
    public isArchived: boolean;
    public sortOrder: number;
    public totalValue: number;
    public valueChange: number;
    public cash: number;
    public swept: number;
    public openExposure: number;
    public unmarkedCost: number;
    public positionsValue: number;
    public valuedAtCost: boolean;
    public realizedPnl: number;
    public unrealizedPnl?: number;
    public unrealizedChange?: number;
    public pnl?: number;
    public fees: number;
    public deposits: number;
    public withdrawals: number;
    public sweeps: number;
    public winRate?: number;
    public wins: number;
    public losses: number;
    public scratches: number;
    public tradeCount: number;
    public openPositions: number;
    public lastActivityAt?: string;
    public sparkline: EquityPoint[] = [];

    public constructor(init?: Partial<WalletCard>) { (Object as any).assign(this, init); }
}

export class WalletEquity
{
    public wallet: string;
    public name: string;
    public mode: WalletMode;
    public points: EquityPoint[] = [];

    public constructor(init?: Partial<WalletEquity>) { (Object as any).assign(this, init); }
}

export class PnlDayResult
{
    /** @description UTC midnight of the day. */
    public date: string;
    /** @description Realized P&L in the wallet base currency for sells on this day, after fees. */
    public realizedPnl: number;
    /** @description Change in unrealized P&L during the day. Null when either end is unknown. */
    public unrealizedChange?: number;
    /** @description Realized P&L plus unrealized change. Null when unrealized change is unknown. Transfers are not included. */
    public pnl?: number;
    /** @description Fees in the wallet base currency for fills on this day. */
    public fees: number;
    public deposits: number;
    public withdrawals: number;
    public sweeps: number;
    /** @description Total value at the end of the day. Unmarked positions are included at cost. */
    public totalValue: number;
    public valueChange: number;
    public cash: number;
    public positionsValue: number;
    public openExposure: number;
    public unmarkedCost: number;
    public valuedAtCost: boolean;
    public trades: number;

    public constructor(init?: Partial<PnlDayResult>) { (Object as any).assign(this, init); }
}

export class WalletPnlDays
{
    public wallet: string;
    public name: string;
    public mode: WalletMode;
    public days: PnlDayResult[] = [];

    public constructor(init?: Partial<WalletPnlDays>) { (Object as any).assign(this, init); }
}

export class SymbolPnl
{
    public symbol: string;
    public realizedPnl: number;
    /** @description Unrealized P&L as of the end of the range. Null when an open position has no price mark. */
    public unrealizedPnl?: number;
    public unrealizedChange?: number;
    /** @description Realized P&L plus unrealized change. Null when unrealized change is unknown. */
    public pnl?: number;
    public fees: number;
    public trades: number;
    public wins: number;
    public losses: number;
    public qty: number;
    public marketValue: number;
    public valuedAtCost: boolean;

    public constructor(init?: Partial<SymbolPnl>) { (Object as any).assign(this, init); }
}

export class FeeDay
{
    public date: string;
    public fees: number;

    public constructor(init?: Partial<FeeDay>) { (Object as any).assign(this, init); }
}

export class PositionView
{
    public wallet: string;
    public walletName: string;
    public mode: WalletMode;
    public symbol: string;
    /** @description open or closed. */
    public status: string;
    public qty: number;
    /** @description Average cost per unit, including buy fees. Quote currency per unit. */
    public avgPrice: number;
    public costBasis: number;
    public lastMarkPrice?: number;
    public lastMarkAt?: string;
    public markIsStale: boolean;
    public markStaleAfterMinutes: number;
    /** @description Null when the position is open and has no price mark. */
    public unrealizedPnl?: number;
    public unrealizedPnlPercent?: number;
    public realizedPnl: number;
    public feesPaid: number;
    public marketValue: number;
    public valuedAtCost: boolean;
    public targetPrice?: number;
    /** @description Target minus the latest mark, in quote currency per unit. Null when either price is missing. */
    public distanceToTarget?: number;
    public distanceToTargetPercent?: number;

    public constructor(init?: Partial<PositionView>) { (Object as any).assign(this, init); }
}

export class TradeFillView
{
    public tradeId: number;
    public side: TradeSide;
    public symbol: string;
    public qty: number;
    /** @description Counterparty fill price in quote currency per unit. */
    public price: number;
    /** @description Allocated buy fee in the wallet base currency. */
    public buyFee: number;
    /** @description Allocated sell fee in the wallet base currency. */
    public sellFee: number;
    /** @description Matched cost in the wallet base currency, including allocated buy fees. */
    public cost: number;
    /** @description Sale proceeds in the wallet base currency, after the allocated sell fee. */
    public proceeds: number;
    public realizedPnl: number;
    public executedAt: string;

    public constructor(init?: Partial<TradeFillView>) { (Object as any).assign(this, init); }
}

export class ChartMarker
{
    public tradeId: number;
    public side: TradeSide;
    public at: string;
    /** @description Recorded fill price in quote currency per unit. */
    public price: number;
    public qty: number;

    public constructor(init?: Partial<ChartMarker>) { (Object as any).assign(this, init); }
}

// @DataContract
export class QueryResponse<T>
{
    // @DataMember(Order=1)
    public offset: number;

    // @DataMember(Order=2)
    public total: number;

    // @DataMember(Order=3)
    public results: T[] = [];

    // @DataMember(Order=4)
    public meta?: { [index:string]: string; };

    // @DataMember(Order=5)
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<QueryResponse<T>>) { (Object as any).assign(this, init); }
}

export class CreateWalletResponse
{
    public result: Wallet;
    public alreadyExists: boolean;
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<CreateWalletResponse>) { (Object as any).assign(this, init); }
}

export class UpdateWalletResponse
{
    public result: Wallet;
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<UpdateWalletResponse>) { (Object as any).assign(this, init); }
}

export class GetWalletResponse
{
    public result: Wallet;
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<GetWalletResponse>) { (Object as any).assign(this, init); }
}

export class QueryWalletsResponse
{
    public results: Wallet[] = [];
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<QueryWalletsResponse>) { (Object as any).assign(this, init); }
}

export class GetWalletBalancesResponse
{
    public result: WalletBalances;
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<GetWalletBalancesResponse>) { (Object as any).assign(this, init); }
}

export class QueryWalletHistoryResponse
{
    public total: number;
    public skip: number;
    public take: number;
    public results: WalletHistoryEntry[] = [];
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<QueryWalletHistoryResponse>) { (Object as any).assign(this, init); }
}

export class QueryTradesResponse
{
    public total: number;
    public skip: number;
    public take: number;
    public results: TradeView[] = [];
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<QueryTradesResponse>) { (Object as any).assign(this, init); }
}

export class RebuildProjectionsResponse
{
    public wallets: number;
    public positions: number;
    public snapshots: number;
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<RebuildProjectionsResponse>) { (Object as any).assign(this, init); }
}

export class QueryPriceMarksResponse
{
    public total: number;
    public skip: number;
    public take: number;
    public results: PriceMarkView[] = [];
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<QueryPriceMarksResponse>) { (Object as any).assign(this, init); }
}

export class RecordTradeResponse
{
    public result: Trade;
    public alreadyRecorded: boolean;
    public balance: WalletBalances;
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<RecordTradeResponse>) { (Object as any).assign(this, init); }
}

export class RecordTransferResponse
{
    public result: Transfer;
    public alreadyRecorded: boolean;
    public balance: WalletBalances;
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<RecordTransferResponse>) { (Object as any).assign(this, init); }
}

export class RecordPriceMarkResponse
{
    public result: PriceMark;
    public alreadyRecorded: boolean;
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<RecordPriceMarkResponse>) { (Object as any).assign(this, init); }
}

export class RecordPriceMarksResponse
{
    public results: RecordedPriceMark[] = [];
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<RecordPriceMarksResponse>) { (Object as any).assign(this, init); }
}

export class GetLiveProductsResponse
{
    public results: LiveProduct[] = [];
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<GetLiveProductsResponse>) { (Object as any).assign(this, init); }
}

export class GetLiveCandlesResponse
{
    public product: string;
    public granularity: string;
    public results: LiveCandle[] = [];
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<GetLiveCandlesResponse>) { (Object as any).assign(this, init); }
}

export class GetLiveTickerResponse
{
    public result?: LiveTick;
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<GetLiveTickerResponse>) { (Object as any).assign(this, init); }
}

export class CreatePaperWalletResponse
{
    public result: Wallet;
    public cash: number;
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<CreatePaperWalletResponse>) { (Object as any).assign(this, init); }
}

export class LiveBookResponse
{
    public wallet: string;
    public walletName: string;
    public mode: WalletMode;
    public simulated: boolean;
    public simulationId?: string;
    public symbol: string;
    public price?: number;
    public cash: number;
    public realizedPnl: number;
    public lots: LiveLot[] = [];
    public average: LiveAverageQuote;
    public orders: LiveOrder[] = [];
    public tradeId?: number;
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<LiveBookResponse>) { (Object as any).assign(this, init); }
}

export class HelloResponse
{
    public result: string;

    public constructor(init?: Partial<HelloResponse>) { (Object as any).assign(this, init); }
}

export class GetSummaryResponse
{
    public totals: PnlTotals;
    public modes: ModePnl[] = [];
    public wallets: WalletCard[] = [];
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<GetSummaryResponse>) { (Object as any).assign(this, init); }
}

export class GetEquityResponse
{
    public wallets: WalletEquity[] = [];
    public total: EquityPoint[] = [];
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<GetEquityResponse>) { (Object as any).assign(this, init); }
}

export class PnlByDayResponse
{
    /** @description One row per UTC day, summed across the selected wallets. */
    public results: PnlDayResult[] = [];
    public wallets: WalletPnlDays[] = [];
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<PnlByDayResponse>) { (Object as any).assign(this, init); }
}

export class GetPnlBySymbolResponse
{
    public results: SymbolPnl[] = [];
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<GetPnlBySymbolResponse>) { (Object as any).assign(this, init); }
}

export class GetFeesResponse
{
    /** @description Fees in the wallet base currency for fills in the range. */
    public total: number;
    public days: FeeDay[] = [];
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<GetFeesResponse>) { (Object as any).assign(this, init); }
}

export class QueryPositionsResponse
{
    public markStaleAfterMinutes: number;
    public results: PositionView[] = [];
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<QueryPositionsResponse>) { (Object as any).assign(this, init); }
}

export class GetTradeResponse
{
    public result: TradeView;
    public fills: TradeFillView[] = [];
    public markers: ChartMarker[] = [];
    public priceMarks: PriceMarkView[] = [];
    public hasPriceMarks: boolean;
    public chartFrom: string;
    public chartTo: string;
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<GetTradeResponse>) { (Object as any).assign(this, init); }
}

// @DataContract
export class RegisterResponse implements IHasSessionId, IHasBearerToken
{
    // @DataMember(Order=1)
    public userId?: string;

    // @DataMember(Order=2)
    public sessionId?: string;

    // @DataMember(Order=3)
    public userName?: string;

    // @DataMember(Order=4)
    public referrerUrl?: string;

    // @DataMember(Order=5)
    public bearerToken?: string;

    // @DataMember(Order=6)
    public refreshToken?: string;

    // @DataMember(Order=7)
    public refreshTokenExpiry?: string;

    // @DataMember(Order=8)
    public roles?: string[];

    // @DataMember(Order=9)
    public permissions?: string[];

    // @DataMember(Order=10)
    public redirectUrl?: string;

    // @DataMember(Order=11)
    public responseStatus?: ResponseStatus;

    // @DataMember(Order=12)
    public meta?: { [index:string]: string; };

    public constructor(init?: Partial<RegisterResponse>) { (Object as any).assign(this, init); }
}

// @DataContract
export class AuthenticateResponse implements IHasSessionId, IHasBearerToken
{
    // @DataMember(Order=1)
    public userId?: string;

    // @DataMember(Order=2)
    public sessionId?: string;

    // @DataMember(Order=3)
    public userName?: string;

    // @DataMember(Order=4)
    public displayName?: string;

    // @DataMember(Order=5)
    public referrerUrl?: string;

    // @DataMember(Order=6)
    public bearerToken?: string;

    // @DataMember(Order=7)
    public refreshToken?: string;

    // @DataMember(Order=8)
    public refreshTokenExpiry?: string;

    // @DataMember(Order=9)
    public profileUrl?: string;

    // @DataMember(Order=10)
    public roles?: string[];

    // @DataMember(Order=11)
    public permissions?: string[];

    // @DataMember(Order=12)
    public authProvider?: string;

    // @DataMember(Order=13)
    public responseStatus?: ResponseStatus;

    // @DataMember(Order=14)
    public meta?: { [index:string]: string; };

    public constructor(init?: Partial<AuthenticateResponse>) { (Object as any).assign(this, init); }
}

/** @description Create an agent-defined wallet */
// @Route("/wallets", "POST")
// @Api(Description="Create an agent-defined wallet")
// @ValidateRequest(Validator="IsAdmin")
export class CreateWallet implements IReturn<CreateWalletResponse>, IPost
{
    /** @description Unique slug chosen by the agent. */
    public key?: string;
    /** @description Display name. */
    public name?: string;
    /** @description Paper or Real. Immutable after create. */
    public mode?: WalletMode;
    /** @description Free-text venue, for example Coinbase. */
    public venue?: string;
    /** @description Base currency code. Defaults to USD. */
    public baseCurrency?: string;
    /** @description Optional note. */
    public note?: string;

    public constructor(init?: Partial<CreateWallet>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'CreateWallet'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new CreateWalletResponse(); }
}

/** @description Update wallet name, note, sort order, or archive flag */
// @Route("/wallets/{Key}", "PATCH")
// @Api(Description="Update wallet name, note, sort order, or archive flag")
// @ValidateRequest(Validator="IsAdmin")
export class UpdateWallet implements IReturn<UpdateWalletResponse>, IPatch
{
    /** @description Wallet slug. Key and mode cannot be changed. */
    public key?: string;
    /** @description New display name. Omit to leave it unchanged. */
    public name?: string;
    /** @description New note. An empty string clears it. Omit to leave it unchanged. */
    public note?: string;
    /** @description Sort order. Omit to leave it unchanged. */
    public sortOrder?: number;
    /** @description Archive or restore the wallet. Omit to leave it unchanged. */
    public isArchived?: boolean;

    public constructor(init?: Partial<UpdateWallet>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'UpdateWallet'; }
    public getMethod() { return 'PATCH'; }
    public createResponse() { return new UpdateWalletResponse(); }
}

/** @description Get one wallet by slug */
// @Route("/api/wallets/{Key}", "GET")
// @Api(Description="Get one wallet by slug")
// @ValidateRequest(Validator="IsAdmin")
export class GetWallet implements IReturn<GetWalletResponse>, IGet
{
    public key?: string;

    public constructor(init?: Partial<GetWallet>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'GetWallet'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new GetWalletResponse(); }
}

/** @description List agent-defined wallets */
// @Route("/api/wallets", "GET")
// @Api(Description="List agent-defined wallets")
// @ValidateRequest(Validator="IsAdmin")
export class QueryWallets implements IReturn<QueryWalletsResponse>, IGet
{
    /** @description Include archived wallets. Defaults to false. */
    public includeArchived: boolean;

    public constructor(init?: Partial<QueryWallets>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'QueryWallets'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new QueryWalletsResponse(); }
}

/** @description Cash, swept amount, fees, and open positions for one wallet */
// @Route("/api/wallets/{Wallet}/balances", "GET")
// @Api(Description="Cash, swept amount, fees, and open positions for one wallet")
// @ValidateRequest(Validator="IsAdmin")
export class GetWalletBalances implements IReturn<GetWalletBalancesResponse>, IGet
{
    /** @description Wallet slug. */
    public wallet?: string;

    public constructor(init?: Partial<GetWalletBalances>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'GetWalletBalances'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new GetWalletBalancesResponse(); }
}

/** @description Trades and transfers for one wallet, newest first */
// @Route("/api/wallets/{Wallet}/history", "GET")
// @Api(Description="Trades and transfers for one wallet, newest first")
// @ValidateRequest(Validator="IsAdmin")
export class QueryWalletHistory implements IReturn<QueryWalletHistoryResponse>, IGet
{
    public wallet?: string;
    /** @description Inclusive UTC start. */
    public from?: string;
    /** @description Inclusive UTC end. */
    public to?: string;
    /** @description buy, sell, deposit, withdrawal, or sweep. */
    public kind?: string;
    /** @description Page size. Defaults to 100. Maximum 1000. */
    public take?: number;
    public skip?: number;

    public constructor(init?: Partial<QueryWalletHistory>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'QueryWalletHistory'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new QueryWalletHistoryResponse(); }
}

/** @description Query recorded trades, newest first */
// @Route("/api/trades", "GET")
// @Api(Description="Query recorded trades, newest first")
// @ValidateRequest(Validator="IsAdmin")
export class QueryTrades implements IReturn<QueryTradesResponse>, IGet
{
    public wallet?: string;
    public symbol?: string;
    public side?: TradeSide;
    public from?: string;
    public to?: string;
    /** @description all, paper, or real. Defaults to all. */
    public mode?: string;
    /** @description won, lost, open, or closed. */
    public outcome?: string;
    /** @description Case-insensitive match on symbol, title, note, wallet, client id, or order ref. */
    public q?: string;
    /** @description time, symbol, wallet, pnl, fee, qty, or price. Defaults to time. */
    public orderBy?: string;
    /** @description Defaults to true. */
    public descending?: boolean;
    public take?: number;
    public skip?: number;

    public constructor(init?: Partial<QueryTrades>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'QueryTrades'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new QueryTradesResponse(); }
}

/** @description Recompute positions and balance snapshots from recorded trades, transfers, and price marks */
// @Route("/projections/rebuild", "POST")
// @Api(Description="Recompute positions and balance snapshots from recorded trades, transfers, and price marks")
// @ValidateRequest(Validator="IsAdmin")
export class RebuildProjections implements IReturn<RebuildProjectionsResponse>, IPost
{
    /** @description Wallet slug. Omit to rebuild every wallet. */
    public wallet?: string;

    public constructor(init?: Partial<RebuildProjections>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'RebuildProjections'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new RebuildProjectionsResponse(); }
}

/** @description Query recorded price marks, newest first */
// @Route("/api/price-marks", "GET")
// @Api(Description="Query recorded price marks, newest first")
// @ValidateRequest(Validator="IsAdmin")
export class QueryPriceMarks implements IReturn<QueryPriceMarksResponse>, IGet
{
    public symbol?: string;
    /** @description When set, returns that wallet's marks and global marks. */
    public wallet?: string;
    public from?: string;
    public to?: string;
    public take?: number;
    public skip?: number;

    public constructor(init?: Partial<QueryPriceMarks>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'QueryPriceMarks'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new QueryPriceMarksResponse(); }
}

/** @description Record a fill. The same wallet and client id returns the original fill. */
// @Route("/wallets/{Wallet}/trades", "POST")
// @Api(Description="Record a fill. The same wallet and client id returns the original fill.")
// @ValidateRequest(Validator="IsAdmin")
export class RecordTrade implements IReturn<RecordTradeResponse>, IPost
{
    /** @description Wallet slug. */
    public wallet?: string;
    /** @description Idempotency key from the agent. */
    public clientId?: string;
    /** @description Instrument symbol, for example BTC-USD. */
    public symbol?: string;
    /** @description Buy or Sell. */
    public side?: TradeSide;
    /** @description Quantity of the instrument. Must be greater than zero. */
    public qty: number;
    /** @description Quote currency per unit, in the wallet base currency. A sell may be zero. A buy must be greater than zero. */
    public price: number;
    /** @description Fee in the wallet base currency. Zero when the fill had no fee. */
    public fee: number;
    /** @description fill or settlement. Defaults to fill. */
    public kind?: TradeKind;
    /** @description Fee currency. Defaults to the wallet base currency and must match it. */
    public feeCurrency?: string;
    /** @description UTC time of the fill. Defaults to now. */
    public executedAt?: string;
    public title?: string;
    public orderRef?: string;
    /** @description Optional target price in quote currency per unit. */
    public targetPrice?: number;
    public note?: string;
    /** @description fifo, lot, or average. Omit for fifo. A buy stays fifo. */
    public costing?: CostingMethod;
    /** @description Buy trade id consumed by a lot sell. */
    public lotId?: number;

    public constructor(init?: Partial<RecordTrade>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'RecordTrade'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new RecordTradeResponse(); }
}

/** @description Record a deposit, withdrawal, or sweep */
// @Route("/wallets/{Wallet}/transfers", "POST")
// @Api(Description="Record a deposit, withdrawal, or sweep")
// @ValidateRequest(Validator="IsAdmin")
export class RecordTransfer implements IReturn<RecordTransferResponse>, IPost
{
    public wallet?: string;
    /** @description Idempotency key from the agent. */
    public clientId?: string;
    /** @description Deposit, Withdrawal, or Sweep. */
    public kind?: TransferKind;
    /** @description Amount in the wallet base currency. Must be greater than zero. */
    public amount: number;
    /** @description Currency code. Defaults to the wallet base currency and must match it. */
    public currency?: string;
    /** @description UTC time of the transfer. Defaults to now. */
    public at?: string;
    public note?: string;

    public constructor(init?: Partial<RecordTransfer>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'RecordTransfer'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new RecordTransferResponse(); }
}

/** @description Record one price mark */
// @Route("/price-marks", "POST")
// @Api(Description="Record one price mark")
// @ValidateRequest(Validator="IsAdmin")
export class RecordPriceMark implements IReturn<RecordPriceMarkResponse>, IPost
{
    /** @description Instrument symbol, for example BTC-USD. */
    public symbol?: string;
    /** @description Quote currency per unit. */
    public price: number;
    /** @description UTC time of the mark. Defaults to now. */
    public at?: string;
    /** @description Wallet slug. Omit for a mark that applies to every wallet. */
    public wallet?: string;
    /** @description Optional idempotency key. A repeat with the same values returns the original mark. */
    public clientId?: string;
    /** @description Free-text source, for example agent. */
    public source?: string;

    public constructor(init?: Partial<RecordPriceMark>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'RecordPriceMark'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new RecordPriceMarkResponse(); }
}

/** @description Record several price marks in one call */
// @Route("/price-marks/batch", "POST")
// @Api(Description="Record several price marks in one call")
// @ValidateRequest(Validator="IsAdmin")
export class RecordPriceMarks implements IReturn<RecordPriceMarksResponse>, IPost
{
    public marks?: PriceMarkWrite[];

    public constructor(init?: Partial<RecordPriceMarks>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'RecordPriceMarks'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new RecordPriceMarksResponse(); }
}

/** @description USD products from the public Coinbase catalog */
// @Route("/api/live/products", "GET")
// @Api(Description="USD products from the public Coinbase catalog")
// @ValidateRequest(Validator="IsAdmin")
export class GetLiveProducts implements IReturn<GetLiveProductsResponse>, IGet
{

    public constructor(init?: Partial<GetLiveProducts>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'GetLiveProducts'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new GetLiveProductsResponse(); }
}

/** @description Public candles for one Coinbase product */
// @Route("/api/live/candles", "GET")
// @Api(Description="Public candles for one Coinbase product")
// @ValidateRequest(Validator="IsAdmin")
export class GetLiveCandles implements IReturn<GetLiveCandlesResponse>, IGet
{
    public product?: string;
    /** @description 1m, 5m, 15m, 1h, or 1d. Defaults to 5m. */
    public granularity?: string;

    public constructor(init?: Partial<GetLiveCandles>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'GetLiveCandles'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new GetLiveCandlesResponse(); }
}

/** @description Latest public price for one product */
// @Route("/api/live/ticker", "GET")
// @Api(Description="Latest public price for one product")
// @ValidateRequest(Validator="IsAdmin")
export class GetLiveTicker implements IReturn<GetLiveTickerResponse>, IGet
{
    public product?: string;

    public constructor(init?: Partial<GetLiveTicker>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'GetLiveTicker'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new GetLiveTickerResponse(); }
}

/** @description Server-sent public prices for one product */
// @Route("/api/live/stream", "GET")
// @Api(Description="Server-sent public prices for one product")
// @ValidateRequest(Validator="IsAdmin")
export class StreamLiveTicker implements IReturnVoid, IGet
{
    public product?: string;

    public constructor(init?: Partial<StreamLiveTicker>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'StreamLiveTicker'; }
    public getMethod() { return 'GET'; }
    public createResponse() {}
}

/** @description Create a paper wallet and its starting cash */
// @Route("/api/live/paper-wallets", "POST")
// @Api(Description="Create a paper wallet and its starting cash")
// @ValidateRequest(Validator="IsAdmin")
export class CreatePaperWallet implements IReturn<CreatePaperWalletResponse>, IPost
{
    public name?: string;
    public startingCash: number;

    public constructor(init?: Partial<CreatePaperWallet>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'CreatePaperWallet'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new CreatePaperWalletResponse(); }
}

/** @description Lots, average quote, cash, and open orders for one wallet and product */
// @Route("/api/live/book", "GET")
// @Api(Description="Lots, average quote, cash, and open orders for one wallet and product")
// @ValidateRequest(Validator="IsAdmin")
export class GetLiveBook implements IReturn<LiveBookResponse>, IGet
{
    public wallet?: string;
    public symbol?: string;
    /** @description Quantity used to preview a sell at average. Defaults to the open quantity. */
    public sellQty?: number;

    public constructor(init?: Partial<GetLiveBook>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'GetLiveBook'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new LiveBookResponse(); }
}

/** @description Place a paper order. A real wallet is rejected. */
// @Route("/api/live/paper-orders", "POST")
// @Api(Description="Place a paper order. A real wallet is rejected.")
// @ValidateRequest(Validator="IsAdmin")
export class PlacePaperOrder implements IReturn<LiveBookResponse>, IPost
{
    public wallet?: string;
    public symbol?: string;
    public side?: TradeSide;
    public type?: LiveOrderType;
    public qty: number;
    public triggerPrice?: number;
    public costing?: CostingMethod;
    public lotId?: number;
    public clientId?: string;

    public constructor(init?: Partial<PlacePaperOrder>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'PlacePaperOrder'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new LiveBookResponse(); }
}

/** @description Cancel a resting paper order */
// @Route("/api/live/paper-orders/{Id}/cancel", "POST")
// @Api(Description="Cancel a resting paper order")
// @ValidateRequest(Validator="IsAdmin")
export class CancelPaperOrder implements IReturn<LiveBookResponse>, IPost
{
    public id: number;

    public constructor(init?: Partial<CancelPaperOrder>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'CancelPaperOrder'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new LiveBookResponse(); }
}

/** @description Move a resting paper order to a new trigger price */
// @Route("/api/live/paper-orders/{Id}/move", "POST")
// @Api(Description="Move a resting paper order to a new trigger price")
// @ValidateRequest(Validator="IsAdmin")
export class MovePaperOrder implements IReturn<LiveBookResponse>, IPost
{
    public id: number;
    public triggerPrice: number;

    public constructor(init?: Partial<MovePaperOrder>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'MovePaperOrder'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new LiveBookResponse(); }
}

/** @description Start an in-memory simulation seeded from a real wallet */
// @Route("/api/live/simulations", "POST")
// @Api(Description="Start an in-memory simulation seeded from a real wallet")
// @ValidateRequest(Validator="IsAdmin")
export class OpenLiveSimulation implements IReturn<LiveBookResponse>, IPost
{
    public wallet?: string;
    public symbol?: string;

    public constructor(init?: Partial<OpenLiveSimulation>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'OpenLiveSimulation'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new LiveBookResponse(); }
}

/** @description Read a simulation book */
// @Route("/api/live/simulations/{Id}", "GET")
// @Api(Description="Read a simulation book")
// @ValidateRequest(Validator="IsAdmin")
export class GetLiveSimulation implements IReturn<LiveBookResponse>, IGet
{
    public id?: string;
    public symbol?: string;
    public sellQty?: number;

    public constructor(init?: Partial<GetLiveSimulation>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'GetLiveSimulation'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new LiveBookResponse(); }
}

/** @description Place a simulated order. Nothing is written to the ledger. */
// @Route("/api/live/simulations/{Id}/orders", "POST")
// @Api(Description="Place a simulated order. Nothing is written to the ledger.")
// @ValidateRequest(Validator="IsAdmin")
export class PlaceSimulationOrder implements IReturn<LiveBookResponse>, IPost
{
    public id?: string;
    public symbol?: string;
    public side?: TradeSide;
    public type?: LiveOrderType;
    public qty: number;
    public triggerPrice?: number;
    public costing?: CostingMethod;
    public lotId?: number;

    public constructor(init?: Partial<PlaceSimulationOrder>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'PlaceSimulationOrder'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new LiveBookResponse(); }
}

/** @description Cancel a simulated order */
// @Route("/api/live/simulations/{Id}/orders/{OrderId}/cancel", "POST")
// @Api(Description="Cancel a simulated order")
// @ValidateRequest(Validator="IsAdmin")
export class CancelSimulationOrder implements IReturn<LiveBookResponse>, IPost
{
    public id?: string;
    public orderId: number;

    public constructor(init?: Partial<CancelSimulationOrder>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'CancelSimulationOrder'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new LiveBookResponse(); }
}

/** @description Move a simulated order to a new trigger price */
// @Route("/api/live/simulations/{Id}/orders/{OrderId}/move", "POST")
// @Api(Description="Move a simulated order to a new trigger price")
// @ValidateRequest(Validator="IsAdmin")
export class MoveSimulationOrder implements IReturn<LiveBookResponse>, IPost
{
    public id?: string;
    public orderId: number;
    public triggerPrice: number;

    public constructor(init?: Partial<MoveSimulationOrder>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'MoveSimulationOrder'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new LiveBookResponse(); }
}

/** @description Reset a simulation back to the wallet's saved holdings */
// @Route("/api/live/simulations/{Id}/reset", "POST")
// @Api(Description="Reset a simulation back to the wallet's saved holdings")
// @ValidateRequest(Validator="IsAdmin")
export class ResetLiveSimulation implements IReturn<LiveBookResponse>, IPost
{
    public id?: string;
    public symbol?: string;

    public constructor(init?: Partial<ResetLiveSimulation>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'ResetLiveSimulation'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new LiveBookResponse(); }
}

// @Route("/hello/{Name}")
export class Hello implements IReturn<HelloResponse>, IGet
{
    public name: string;

    public constructor(init?: Partial<Hello>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'Hello'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new HelloResponse(); }
}

/** @description Overview totals per wallet and per mode. Amounts are in each wallet's base currency. */
// @Route("/api/summary", "GET")
// @Api(Description="Overview totals per wallet and per mode. Amounts are in each wallet's base currency.")
// @ValidateRequest(Validator="IsAdmin")
export class GetSummary implements IReturn<GetSummaryResponse>, IGet
{
    /** @description all, paper, or real. Defaults to all. Totals and wallet cards use this filter. Modes always lists all three. */
    public mode?: string;
    /** @description Inclusive UTC start. Omit to start at the first recorded event. */
    public from?: string;
    /** @description Inclusive UTC end. Point-in-time totals are as of this time. Omit for the latest recorded event. */
    public to?: string;
    /** @description Wallet slug. Omit for every wallet in the mode. */
    public wallet?: string;
    /** @description Include archived wallets. A wallet slug always includes that wallet. */
    public includeArchived: boolean;

    public constructor(init?: Partial<GetSummary>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'GetSummary'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new GetSummaryResponse(); }
}

/** @description Equity curve from recorded trades, transfers, and price marks */
// @Route("/api/equity", "GET")
// @Api(Description="Equity curve from recorded trades, transfers, and price marks")
// @ValidateRequest(Validator="IsAdmin")
export class GetEquity implements IReturn<GetEquityResponse>, IGet
{
    /** @description Wallet slug. Omit for every wallet. */
    public wallet?: string;
    /** @description all, paper, or real. Defaults to all. */
    public mode?: string;
    /** @description Inclusive UTC start. */
    public from?: string;
    /** @description Inclusive UTC end. */
    public to?: string;
    public includeArchived: boolean;

    public constructor(init?: Partial<GetEquity>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'GetEquity'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new GetEquityResponse(); }
}

/** @description Daily P&L from recorded trades, fees, and price marks. The pnl_by_day tool calls this request. */
// @Route("/api/pnl/daily", "GET")
// @Api(Description="Daily P&L from recorded trades, fees, and price marks. The pnl_by_day tool calls this request.")
// @ValidateRequest(Validator="IsAdmin")
export class PnlByDay implements IReturn<PnlByDayResponse>, IGet
{
    /** @description Wallet slug. Omit for every wallet. */
    public wallet?: string;
    /** @description Inclusive UTC start. */
    public from?: string;
    /** @description Inclusive UTC end. */
    public to?: string;
    /** @description all, paper, or real. Defaults to all. */
    public mode?: string;
    /** @description Include archived wallets. A wallet slug always includes that wallet. */
    public includeArchived: boolean;

    public constructor(init?: Partial<PnlByDay>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'PnlByDay'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new PnlByDayResponse(); }
}

/** @description P&L by symbol from recorded trades, fees, and price marks */
// @Route("/api/pnl/symbols", "GET")
// @Api(Description="P&L by symbol from recorded trades, fees, and price marks")
// @ValidateRequest(Validator="IsAdmin")
export class GetPnlBySymbol implements IReturn<GetPnlBySymbolResponse>, IGet
{
    public wallet?: string;
    public mode?: string;
    public from?: string;
    public to?: string;
    public includeArchived: boolean;
    /** @description Maximum rows after sorting by P&L. Omit for every symbol. */
    public take?: number;

    public constructor(init?: Partial<GetPnlBySymbol>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'GetPnlBySymbol'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new GetPnlBySymbolResponse(); }
}

/** @description Fees over time from recorded fills */
// @Route("/api/fees", "GET")
// @Api(Description="Fees over time from recorded fills")
// @ValidateRequest(Validator="IsAdmin")
export class GetFees implements IReturn<GetFeesResponse>, IGet
{
    public wallet?: string;
    public mode?: string;
    public from?: string;
    public to?: string;
    public includeArchived: boolean;

    public constructor(init?: Partial<GetFees>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'GetFees'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new GetFeesResponse(); }
}

/** @description Open and closed positions from the ledger */
// @Route("/api/positions", "GET")
// @Api(Description="Open and closed positions from the ledger")
// @ValidateRequest(Validator="IsAdmin")
export class QueryPositions implements IReturn<QueryPositionsResponse>, IGet
{
    public wallet?: string;
    public mode?: string;
    public symbol?: string;
    /** @description open, closed, or omit for both. */
    public status?: string;
    public includeArchived: boolean;

    public constructor(init?: Partial<QueryPositions>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'QueryPositions'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new QueryPositionsResponse(); }
}

/** @description One trade with its matched fills and the price marks used to chart it */
// @Route("/api/trades/{Id}", "GET")
// @Api(Description="One trade with its matched fills and the price marks used to chart it")
// @ValidateRequest(Validator="IsAdmin")
export class GetTrade implements IReturn<GetTradeResponse>, IGet
{
    public id: number;

    public constructor(init?: Partial<GetTrade>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'GetTrade'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new GetTradeResponse(); }
}

/** @description Sign Up */
// @Api(Description="Sign Up")
// @DataContract
export class Register implements IReturn<RegisterResponse>, IPost
{
    // @DataMember(Order=1)
    public userName?: string;

    // @DataMember(Order=2)
    public firstName?: string;

    // @DataMember(Order=3)
    public lastName?: string;

    // @DataMember(Order=4)
    public displayName?: string;

    // @DataMember(Order=5)
    public email?: string;

    // @DataMember(Order=6)
    public password?: string;

    // @DataMember(Order=7)
    public confirmPassword?: string;

    // @DataMember(Order=8)
    public autoLogin?: boolean;

    // @DataMember(Order=10)
    public errorView?: string;

    // @DataMember(Order=11)
    public meta?: { [index:string]: string; };

    public constructor(init?: Partial<Register>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'Register'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new RegisterResponse(); }
}

// @Route("/confirm-email")
export class ConfirmEmail implements IReturnVoid, IGet
{
    public userId: string;
    public code: string;
    public returnUrl?: string;

    public constructor(init?: Partial<ConfirmEmail>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'ConfirmEmail'; }
    public getMethod() { return 'GET'; }
    public createResponse() {}
}

/** @description Sign In */
// @Route("/auth", "GET,POST")
// @Route("/auth/{provider}", "POST")
// @Api(Description="Sign In")
// @DataContract
export class Authenticate implements IReturn<AuthenticateResponse>, IPost
{
    /** @description AuthProvider, e.g. credentials */
    // @DataMember(Order=1)
    public provider?: string;

    // @DataMember(Order=2)
    public userName?: string;

    // @DataMember(Order=3)
    public password?: string;

    // @DataMember(Order=4)
    public rememberMe?: boolean;

    // @DataMember(Order=5)
    public accessToken?: string;

    // @DataMember(Order=6)
    public accessTokenSecret?: string;

    // @DataMember(Order=7)
    public returnUrl?: string;

    // @DataMember(Order=8)
    public errorView?: string;

    // @DataMember(Order=9)
    public meta?: { [index:string]: string; };

    public constructor(init?: Partial<Authenticate>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'Authenticate'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new AuthenticateResponse(); }
}

// @ValidateRequest(Validator="IsAdmin")
export class QueryUsers extends QueryDb<User> implements IReturn<QueryResponse<User>>
{
    public id?: string;

    public constructor(init?: Partial<QueryUsers>) { super(init); (Object as any).assign(this, init); }
    public getTypeName() { return 'QueryUsers'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new QueryResponse<User>(); }
}

