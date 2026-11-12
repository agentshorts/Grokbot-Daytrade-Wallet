import { describe, expect, it, vi, afterEach } from 'vite-plus/test'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import WalletsPage from './wallets.vue'
import WalletDetail from './wallet/[key].vue'
import { DASH } from '@/lib/walletView'

const directory = {
    results: [
        { key: 'paper', name: 'Paper', mode: 'Paper', venue: 'Coinbase', baseCurrency: 'USD', isArchived: false, sortOrder: 1 },
        { key: 'coinbase', name: 'Coinbase', mode: 'Real', venue: 'Coinbase', baseCurrency: 'USD', isArchived: false, sortOrder: 2 },
        { key: 'old-book', name: 'Old book', mode: 'Paper', venue: 'Paper', baseCurrency: 'USD', isArchived: true, sortOrder: 3 },
    ],
}

const summary = {
    wallets: [
        {
            key: 'paper',
            name: 'Paper',
            mode: 'Paper',
            baseCurrency: 'USD',
            totalValue: 214,
            valueChange: 14,
            cash: 203,
            swept: 10,
            fees: 8,
            realizedPnl: 14,
            unrealizedPnl: null,
            openPositions: 2,
            lastActivityAt: '2026-11-11T15:00:00.000Z',
            sparkline: [{ totalValue: 200 }, { totalValue: 214 }],
            unmarkedCost: 11,
            valuedAtCost: true,
        },
        {
            key: 'coinbase',
            name: 'Coinbase',
            mode: 'Real',
            baseCurrency: 'USD',
            totalValue: 52,
            valueChange: 2,
            cash: 40,
            swept: 0,
            fees: 1,
            realizedPnl: 0,
            unrealizedPnl: 2,
            openPositions: 1,
            lastActivityAt: '2026-11-12T16:20:00.000Z',
            sparkline: [{ totalValue: 50 }, { totalValue: 52 }],
            unmarkedCost: 0,
            valuedAtCost: false,
        },
        {
            key: 'old-book',
            name: 'Old book',
            mode: 'Paper',
            baseCurrency: 'USD',
            isArchived: true,
            totalValue: 5,
            valueChange: 0,
            cash: 5,
            swept: 0,
            fees: 0,
            realizedPnl: 0,
            unrealizedPnl: 0,
            openPositions: 0,
            sparkline: [],
            unmarkedCost: 0,
            valuedAtCost: false,
        },
    ],
}

function detailBody(key: string) {
    const real = key === 'coinbase'
    return {
        wallet: { result: directory.results.find((row) => row.key === key) ?? { key, name: key, mode: 'Paper' } },
        summary: { wallets: summary.wallets.filter((row) => row.key === key) },
        equity: {
            wallets: [
                {
                    wallet: key,
                    points: [
                        { at: '2026-11-10T15:00:00.000Z', totalValue: real ? 50 : 200, cash: real ? 40 : 180 },
                        { at: '2026-11-11T15:00:00.000Z', totalValue: real ? 52 : 214, cash: real ? 40 : 203 },
                    ],
                },
            ],
        },
        daily: { wallets: [{ wallet: key, days: [{ date: '2026-11-11T00:00:00.000Z', pnl: real ? 2 : null, fees: real ? 1 : 8 }] }] },
        fees: { total: real ? 1 : 8, days: [{ date: '2026-11-11T00:00:00.000Z', fees: real ? 1 : 8 }] },
        positions: {
            markStaleAfterMinutes: 15,
            results: real
                ? [
                      {
                          symbol: 'SOL-USD',
                          status: 'open',
                          qty: 2,
                          avgPrice: 150,
                          costBasis: 301,
                          lastMarkPrice: 160,
                          lastMarkAt: '2026-11-12T16:20:00.000Z',
                          markIsStale: false,
                          unrealizedPnl: 2,
                          unrealizedPnlPercent: 20,
                          realizedPnl: 0,
                          feesPaid: 1,
                          targetPrice: 180,
                          distanceToTarget: 20,
                          distanceToTargetPercent: 12.5,
                      },
                  ]
                : [
                      {
                          symbol: 'ETH-USD',
                          status: 'open',
                          qty: 0.1,
                          avgPrice: 3201,
                          costBasis: 320.1,
                          lastMarkPrice: null,
                          lastMarkAt: null,
                          markIsStale: false,
                          unrealizedPnl: null,
                          unrealizedPnlPercent: null,
                          realizedPnl: 0,
                          feesPaid: 0.1,
                          targetPrice: 3500,
                          distanceToTarget: null,
                          distanceToTargetPercent: null,
                      },
                      {
                          symbol: 'BTC-USD',
                          status: 'open',
                          qty: 0.006,
                          avgPrice: 64050,
                          costBasis: 384.3,
                          lastMarkPrice: 64100,
                          lastMarkAt: '2026-11-08T15:10:00.000Z',
                          markIsStale: true,
                          unrealizedPnl: 0.3,
                          unrealizedPnlPercent: 0.08,
                          realizedPnl: 4,
                          feesPaid: 0.75,
                          targetPrice: 66000,
                          distanceToTarget: 1900,
                          distanceToTargetPercent: 2.96,
                      },
                      {
                          symbol: 'SOL-USD',
                          status: 'closed',
                          qty: 0,
                          avgPrice: 0,
                          costBasis: 0,
                          lastMarkPrice: 15,
                          lastMarkAt: '2026-11-01T00:00:00.000Z',
                          markIsStale: true,
                          unrealizedPnl: 0,
                          realizedPnl: 14,
                          feesPaid: 7,
                          targetPrice: null,
                          distanceToTarget: null,
                      },
                  ],
        },
        trades: {
            results: [
                {
                    id: 2,
                    wallet: key,
                    walletName: real ? 'Coinbase' : 'Paper',
                    mode: real ? 'Real' : 'Paper',
                    symbol: 'ETH-USD',
                    title: 'Open clip',
                    side: 'Buy',
                    qty: 0.1,
                    price: 3200,
                    fee: 0.1,
                    executedAt: '2026-11-11T15:00:00.000Z',
                    recordedAt: '2026-11-11T15:00:01.000Z',
                    pnl: null,
                    status: 'open',
                },
            ],
        },
        history: {
            results: [{ id: 1, kind: 'Deposit', at: '2026-11-08T15:00:00.000Z', amount: 1000, note: 'Starting cash', currency: 'USD' }],
        },
        balances: { result: { key, cashIsNegative: key === 'unfunded', hasDeposits: key !== 'unfunded' } },
    }
}

function requestUrl(input: RequestInfo | URL): string {
    if (typeof input === 'string') return input
    if (input instanceof URL) return input.href
    return input.url
}

/** Ledger reads are under `/api`. Returns the resource path after that prefix. */
function ledgerPath(url: string): string {
    const bare = url.split('?')[0] ?? url
    if (!bare.startsWith('/api/')) throw new Error(`ledger read left /api: ${url}`)
    return bare.slice('/api'.length)
}

function installFetch(status = 200) {
    const urls: string[] = []
    vi.stubGlobal('fetch', (input: RequestInfo | URL) => {
        const url = requestUrl(input)
        urls.push(url)
        if (status !== 200) return Promise.resolve(new Response(JSON.stringify({ responseStatus: { message: 'denied' } }), { status }))
        const path = ledgerPath(url)
        const pathKey = /\/wallets\/([^/?]+)/.exec(path)?.[1]
        const queryKey = /[?&]wallet=([^&]+)/.exec(url)?.[1]
        const key = decodeURIComponent(pathKey || queryKey || 'paper')
        const body = detailBody(key)
        let payload: unknown = {}
        if (path === '/wallets') payload = directory
        else if (path.endsWith('/balances')) payload = body.balances
        else if (path.endsWith('/history')) payload = url.includes('kind=deposit') ? body.history : { results: [] }
        else if (path.startsWith('/wallets/')) payload = body.wallet
        else if (path === '/summary') payload = url.includes('wallet=') ? body.summary : summary
        else if (path === '/equity') payload = body.equity
        else if (path === '/pnl/daily') payload = body.daily
        else if (path === '/fees') payload = body.fees
        else if (path === '/positions') payload = body.positions
        else if (path === '/trades') payload = body.trades
        return Promise.resolve(new Response(JSON.stringify(payload), { status: 200, headers: { 'content-type': 'application/json' } }))
    })
    return urls
}

async function mountAt(path: string, page: object) {
    const router = createRouter({
        history: createMemoryHistory(),
        routes: [
            { path: '/', component: { template: '<div />' } },
            { path: '/wallets', component: WalletsPage },
            { path: '/wallet/:key', component: WalletDetail },
            { path: '/trades', component: { template: '<div />' } },
            { path: '/signin', component: { template: '<div>Sign in page</div>' } },
        ],
    })
    await router.push(path)
    await router.isReady()
    const wrapper = mount(page, {
        global: {
            plugins: [router, createPinia()],
            stubs: { EChart: { template: '<div data-testid="chart" />' } },
        },
    })
    await flushPromises()
    return { router, wrapper }
}

afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
})

describe('wallets list', () => {
    it('renders a real wallet, a paper wallet, and an archived badge from the read API', async () => {
        const urls = installFetch()
        const { router, wrapper } = await mountAt('/wallets?range=all', WalletsPage)
        expect(wrapper.get('h1').text()).toBe('Wallets')
        expect(wrapper.text()).toContain('Paper')
        expect(wrapper.text()).toContain('Coinbase')
        expect(wrapper.text()).toContain('Real')
        expect(wrapper.text()).toContain('Archived')
        expect(wrapper.text()).toContain('$214.00')
        expect(wrapper.text()).toContain('$52.00')
        expect(urls.some((url) => ledgerPath(url) === '/summary' && url.includes('includeArchived=true'))).toBe(true)
        expect(urls.some((url) => ledgerPath(url) === '/wallets' && url.includes('includeArchived=true'))).toBe(true)

        const paper = wrapper.findAll('button').find((button) => button.text().includes('Paper'))
        expect(paper).toBeTruthy()
        await paper?.trigger('click')
        await flushPromises()
        expect(router.currentRoute.value.path).toBe('/wallet/paper')
        expect(router.currentRoute.value.query.wallet).toBe('paper')
        expect(router.currentRoute.value.query.range).toBe('all')
        wrapper.unmount()
    })

    it('shows the empty book and the sign-in failure', async () => {
        installFetch()
        vi.stubGlobal('fetch', () => Promise.resolve(new Response(JSON.stringify({ results: [], wallets: [] }), { status: 200 })))
        const empty = await mountAt('/wallets?range=all', WalletsPage)
        expect(empty.wrapper.text()).toContain('No wallets yet')
        empty.wrapper.unmount()

        installFetch(401)
        const denied = await mountAt('/wallets?range=all', WalletsPage)
        expect(denied.wrapper.text()).toContain('Sign in as an admin to read the ledger.')
        denied.wrapper.unmount()
    })
})

describe('wallet detail', () => {
    async function open(path: string) {
        installFetch()
        const mounted = await mountAt(path, WalletDetail)
        return mounted
    }

    function unmount(wrapper: VueWrapper) {
        wrapper.unmount()
    }

    it('shows the paper wallet with a dash for unknown unrealized P&L and a stale mark', async () => {
        const { router, wrapper } = await open('/wallet/paper?range=all&wallet=paper')
        expect(wrapper.get('[data-testid="wallet-badge"]').text()).toBe('Paper')
        expect(wrapper.text()).toContain('$214.00')
        expect(wrapper.text()).toContain('$203.00')
        expect(wrapper.text()).toContain('$10.00')
        expect(wrapper.text()).toContain('$8.00')
        expect(wrapper.text()).toContain(DASH)
        expect(wrapper.get('[data-testid="unrealized"]').text()).toContain(DASH)
        expect(wrapper.text()).toContain('Stale')
        expect(wrapper.text()).toContain('No price mark')
        expect(wrapper.text()).toContain('Deposit')
        expect(wrapper.text()).toContain('Starting cash')
        expect(wrapper.text()).toContain('SOL-USD')
        expect(wrapper.text()).toContain('+$14.00')
        expect(router.currentRoute.value.query.wallet).toBe('paper')
        expect(router.currentRoute.value.query.range).toBe('all')
        unmount(wrapper)
    })

    it('shows the real wallet, target distance, and keeps a new range in the URL', async () => {
        const urls = installFetch()
        const { router, wrapper } = await mountAt('/wallet/coinbase?range=all&wallet=coinbase', WalletDetail)
        expect(wrapper.get('[data-testid="wallet-badge"]').text()).toBe('Real')
        expect(wrapper.text()).toContain('$52.00')
        expect(wrapper.text()).toContain('+$2.00')
        expect(wrapper.get('[data-testid="distance"]').text()).toContain('+$20.00')
        expect(wrapper.get('[data-testid="distance"]').text()).toContain('+12.50%')
        expect(wrapper.text()).not.toContain('Starting balance not recorded')
        expect(urls.some((url) => ledgerPath(url) === '/summary' && url.includes('wallet=coinbase') && !url.includes('from='))).toBe(true)

        await router.push('/wallet/coinbase?range=7d&wallet=coinbase')
        await flushPromises()
        expect(router.currentRoute.value.query.range).toBe('7d')
        expect(router.currentRoute.value.query.wallet).toBe('coinbase')
        expect(urls.some((url) => ledgerPath(url) === '/summary' && url.includes('from='))).toBe(true)
        wrapper.unmount()
    })

    it('banners a wallet whose cash went negative with no deposit', async () => {
        installFetch()
        const { wrapper } = await mountAt('/wallet/unfunded?range=all&wallet=unfunded', WalletDetail)
        expect(wrapper.get('[data-testid="starting-balance"]').text().toLowerCase()).toContain('starting balance not recorded')
        wrapper.unmount()
    })
})
