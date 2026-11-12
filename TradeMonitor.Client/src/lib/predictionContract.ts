// Readable names and closed-contract figures for pm- wallets.
// The ticker hour is Eastern. The clock on the label is the confirmed close
// timestamp converted into the viewer's time zone.

const MONTHS = ['JAN', 'FEB', 'MAR', 'APR', 'MAY', 'JUN', 'JUL', 'AUG', 'SEP', 'OCT', 'NOV', 'DEC']
const TICKER = /(KX[A-Z0-9]+)-(\d{2}[A-Z]{3}\d{4})-([TB])(\d+(?:\.\d+)?)(-KALSHI)?/i

export const HIDE_CLOSED_CONTRACTS_KEY = 'trademonitor.hideClosedContracts'

export interface ParsedTicker {
    raw: string
    series: string
    asset: string
    year: number
    month: number
    day: number
    /** Hour encoded on the ticker. Kalshi hourly contracts use Eastern time. */
    hour: number
    strike: number
    direction: 'above' | 'below'
}

export interface ChartSample {
    at: string
    value: number
    cash?: number
}

export interface ClipOptions {
    closeAt: string | null
    ledgerClosed: boolean
    marketClosed: boolean
    realized: number
    /** Closed ledger positions end the line on realized P&L. */
    pinRealized: boolean
}

export interface PredictionFacts {
    key: string
    name: string
    symbol?: string | null
    kind?: string | null
    settlement?: string | null
    openPositions: number
    tradeCount: number
    marketValue: number
    marketChange: number
    lifetimeRealized: number
    rangeRealized: number
    closeAt: string | null
    sparkline: ChartSample[]
    now: Date
    timeZone: string
}

export interface PredictionPresentation {
    prediction: boolean
    name: string
    ticker: string | null
    asset: string | null
    closed: boolean
    ledgerClosed: boolean
    badge: 'Closed' | 'Settled' | null
    value: number
    change: number
    sparkline: number[]
    closeAt: string | null
}

type StorageLike = {
    getItem(key: string): string | null
    setItem(key: string, value: string): void
}

const memoryStore = new Map<string, string>()

export function viewerTimeZone(): string {
    try {
        return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC'
    } catch {
        return 'UTC'
    }
}

export function browserStorage(): StorageLike {
    try {
        if (typeof localStorage !== 'undefined') return localStorage
    } catch {
        // Private mode can throw on access. The in-memory store still works for this tab.
    }
    return {
        getItem: (key) => memoryStore.get(key) ?? null,
        setItem: (key, value) => {
            memoryStore.set(key, value)
        },
    }
}

export function readHideClosedContracts(storage: Pick<StorageLike, 'getItem'>): boolean {
    try {
        return storage.getItem(HIDE_CLOSED_CONTRACTS_KEY) === '1'
    } catch {
        return false
    }
}

export function writeHideClosedContracts(storage: Pick<StorageLike, 'setItem'>, hide: boolean): void {
    storage.setItem(HIDE_CLOSED_CONTRACTS_KEY, hide ? '1' : '0')
}

export function filterClosedContracts<T>(rows: T[], hide: boolean, isClosed: (row: T) => boolean): T[] {
    if (!hide) return rows.slice()
    return rows.filter((row) => !isClosed(row))
}

export function isPredictionWallet(key: string, kind?: string | null): boolean {
    if ((kind ?? '').toLowerCase() === 'predictionmarket') return true
    return key.toLowerCase().startsWith('pm-')
}

export function isLedgerClosed(settlement?: string | null): boolean {
    const code = (settlement ?? '').toLowerCase()
    return code === 'closed' || code.startsWith('settled')
}

export function isContractClosed(input: { settlement?: string | null; closeAt?: string | null; now: Date }): boolean {
    if (isLedgerClosed(input.settlement)) return true
    const end = input.closeAt ? Date.parse(input.closeAt) : Number.NaN
    return !Number.isNaN(end) && end <= input.now.getTime()
}

export function contractBadge(settlement: string | null | undefined, closed: boolean): 'Closed' | 'Settled' | null {
    if (!closed) return null
    return (settlement ?? '').toLowerCase().startsWith('settled') ? 'Settled' : 'Closed'
}

export function parseContractTicker(input: string): ParsedTicker | null {
    const match = TICKER.exec(input.toUpperCase())
    if (!match) return null
    const series = match[1] ?? ''
    const event = match[2] ?? ''
    const side = match[3] ?? ''
    const strikeText = match[4] ?? ''
    const asset = assetOf(series)
    const when = parseEvent(event)
    const strike = Number(strikeText)
    if (!asset || !when || !Number.isFinite(strike)) return null
    if (side !== 'T' && side !== 'B') return null
    return {
        raw: `${series}-${event}-${side}${strikeText}${match[5] ? '-KALSHI' : ''}`,
        series,
        asset,
        year: when.year,
        month: when.month,
        day: when.day,
        hour: when.hour,
        strike,
        direction: side === 'B' ? 'below' : 'above',
    }
}

export function readContractTicker(...sources: (string | null | undefined)[]): ParsedTicker | null {
    for (const source of sources) {
        if (!source) continue
        const parsed = parseContractTicker(source)
        if (parsed) return parsed
    }
    return null
}

export function formatStrike(value: number): string {
    const rounded = Math.round(value)
    return rounded.toLocaleString('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 0 })
}

/** Local clock from a confirmed close timestamp. Returns null when the time or zone cannot be formatted. */
export function formatContractName(parsed: ParsedTicker, closeUtc: string, timeZone: string): string | null {
    const date = new Date(closeUtc)
    if (Number.isNaN(date.getTime())) return null
    const when = formatWhen(date, timeZone)
    if (!when) return null
    return `${parsed.asset} ${parsed.direction} ${formatStrike(parsed.strike)} at ${when.clock} ${when.zone}, ${when.day}`
}

export function contractName(sources: (string | null | undefined)[], closeAt: string | null, timeZone: string, fallback: string): string {
    if (!closeAt) return fallback
    const parsed = readContractTicker(...sources)
    if (!parsed) return fallback
    return formatContractName(parsed, closeAt, timeZone) ?? fallback
}

function nearZero(value: number): boolean {
    return Math.abs(value) < 0.005
}

export function clipContractChart(points: ChartSample[], options: ClipOptions): ChartSample[] {
    const sorted = points
        .map((point) => ({ point, at: Date.parse(point.at) }))
        .filter((row) => !Number.isNaN(row.at))
        .sort((a, b) => a.at - b.at)
    const endMs = options.closeAt ? Date.parse(options.closeAt) : Number.NaN
    const hasEnd = !Number.isNaN(endMs)
    let kept = (hasEnd ? sorted.filter((row) => row.at <= endMs) : sorted).map((row) => ({
        at: row.point.at,
        value: row.point.value,
        cash: row.point.cash ?? 0,
    }))
    const done = options.ledgerClosed || options.marketClosed
    if (!done) return kept

    const keepZero = options.ledgerClosed && options.pinRealized && nearZero(options.realized)
    while (kept.length > 1 && nearZero(kept[kept.length - 1]?.value ?? 0) && !keepZero) kept = kept.slice(0, -1)

    let endValue = kept[kept.length - 1]?.value ?? 0
    if (options.ledgerClosed && options.pinRealized) endValue = options.realized
    const endAt = hasEnd ? new Date(endMs).toISOString() : (kept[kept.length - 1]?.at ?? new Date(0).toISOString())
    if (kept.length === 0) return [{ at: endAt, value: endValue, cash: 0 }]

    const last = kept[kept.length - 1]!
    if (!hasEnd || Date.parse(last.at) === endMs) return [...kept.slice(0, -1), { ...last, value: endValue }]
    return [...kept, { at: endAt, value: endValue, cash: last.cash }]
}

export function presentWalletCard(
    card: {
        key: string
        name: string
        kind?: string | null
        settlement?: string | null
        openPositions?: number
        tradeCount?: number
        totalValue: number
        valueChange: number
        realizedPnl: number
        lifetimeRealizedPnl?: number | null
        closeAt?: string | null
        sparkline: { at: string; totalValue: number; cash?: number }[]
    },
    options: { symbol?: string | null; now: Date; timeZone?: string },
): PredictionPresentation {
    return presentPrediction({
        key: card.key,
        name: card.name,
        symbol: options.symbol,
        kind: card.kind,
        settlement: card.settlement,
        openPositions: card.openPositions ?? 0,
        tradeCount: card.tradeCount ?? 0,
        marketValue: card.totalValue,
        marketChange: card.valueChange,
        lifetimeRealized: card.lifetimeRealizedPnl ?? card.realizedPnl,
        rangeRealized: card.realizedPnl,
        closeAt: card.closeAt ?? null,
        sparkline: card.sparkline.filter((point) => point.at).map((point) => ({ at: point.at, value: point.totalValue, cash: point.cash })),
        now: options.now,
        timeZone: options.timeZone ?? viewerTimeZone(),
    })
}

export function presentPrediction(facts: PredictionFacts): PredictionPresentation {
    const prediction = isPredictionWallet(facts.key, facts.kind)
    if (!prediction) {
        return {
            prediction: false,
            name: facts.name,
            ticker: null,
            asset: null,
            closed: false,
            ledgerClosed: false,
            badge: null,
            value: facts.marketValue,
            change: facts.marketChange,
            sparkline: facts.sparkline.map((point) => point.value),
            closeAt: null,
        }
    }

    const parsed = readContractTicker(facts.key, facts.symbol, facts.name)
    const ledgerClosed = isLedgerClosed(facts.settlement)
    const marketClosed = isContractClosed({ settlement: null, closeAt: facts.closeAt, now: facts.now })
    const closed = ledgerClosed || marketClosed
    const name = contractName([facts.key, facts.symbol, facts.name], facts.closeAt, facts.timeZone, facts.name)
    const line = clipContractChart(facts.sparkline, {
        closeAt: facts.closeAt,
        ledgerClosed,
        marketClosed,
        realized: facts.lifetimeRealized,
        pinRealized: true,
    })
    let sparkline = line.map((point) => point.value)
    if (closed && sparkline.length === 1) sparkline = [0, sparkline[0]!]
    if (closed && sparkline.length === 0) sparkline = [0, facts.lifetimeRealized]

    const ended = line[line.length - 1]?.value ?? facts.marketValue
    const opened = line[0]?.value ?? ended
    const value = ledgerClosed ? facts.lifetimeRealized : closed ? ended : facts.marketValue
    const change = ledgerClosed ? facts.rangeRealized : closed ? ended - opened : facts.marketChange

    return {
        prediction: true,
        name,
        ticker: parsed?.raw ?? null,
        asset: parsed?.asset ?? null,
        closed,
        ledgerClosed,
        badge: contractBadge(facts.settlement, closed),
        value,
        change,
        sparkline,
        closeAt: facts.closeAt,
    }
}

export function groupPredictionWallets<T extends { prediction?: boolean; asset?: string | null; closeAt?: string | null; name: string }>(
    wallets: T[],
): { asset: string | null; wallets: T[] }[] {
    const standard = wallets.filter((wallet) => !wallet.prediction)
    const contracts = wallets.filter((wallet) => wallet.prediction)
    const sections: { asset: string | null; wallets: T[] }[] = []
    if (standard.length > 0) sections.push({ asset: null, wallets: standard })
    const assets = [...new Set(contracts.map((wallet) => wallet.asset || 'Contracts'))].sort((a, b) => a.localeCompare(b))
    for (const asset of assets) {
        const rows = contracts.filter((wallet) => (wallet.asset || 'Contracts') === asset)
        rows.sort((a, b) => (a.closeAt ?? '').localeCompare(b.closeAt ?? '') || a.name.localeCompare(b.name))
        sections.push({ asset, wallets: rows })
    }
    return sections
}

function assetOf(series: string): string | null {
    const known: Record<string, string> = {
        KXBTCD: 'BTC',
        KXETHD: 'ETH',
        KXSOLD: 'SOL',
        KXDOGED: 'DOGE',
        KXXRPD: 'XRP',
        KXBNBD: 'BNB',
        KXAVAXD: 'AVAX',
        KXLINKD: 'LINK',
        KXADAD: 'ADA',
        KXLTCD: 'LTC',
    }
    if (known[series]) return known[series]
    const daily = /^KX([A-Z]{2,5})D$/.exec(series)
    if (daily?.[1]) return daily[1]
    const bare = /^KX([A-Z]{2,5})$/.exec(series)
    return bare?.[1] ?? null
}

function parseEvent(event: string): { year: number; month: number; day: number; hour: number } | null {
    const match = /^(\d{2})([A-Z]{3})(\d{2})(\d{2})$/.exec(event)
    if (!match) return null
    const month = MONTHS.indexOf(match[2] ?? '') + 1
    const day = Number(match[3])
    const hour = Number(match[4])
    if (month < 1 || day < 1 || day > 31 || hour > 23) return null
    return { year: 2000 + Number(match[1]), month, day, hour }
}

function formatWhen(date: Date, timeZone: string): { clock: string; zone: string; day: string } | null {
    try {
        const parts = new Intl.DateTimeFormat('en-US', {
            timeZone,
            hour: 'numeric',
            minute: '2-digit',
            hour12: true,
            timeZoneName: 'short',
            month: 'short',
            day: 'numeric',
        }).formatToParts(date)
        const get = (type: Intl.DateTimeFormatPartTypes) => parts.find((part) => part.type === type)?.value ?? ''
        const period = get('dayPeriod').replace(/\./g, '').toUpperCase()
        const minute = get('minute')
        const hour = get('hour')
        const zone = get('timeZoneName')
        const day = `${get('month')} ${get('day')}`.trim()
        if (!hour || !period || !zone || day === '') return null
        const clock = minute === '00' ? `${hour} ${period}` : `${hour}:${minute} ${period}`
        return { clock, zone, day }
    } catch {
        return null
    }
}
