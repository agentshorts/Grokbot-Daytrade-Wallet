import { expect, test } from '@playwright/test'

const dash = '\u2014'

test('paper and real wallets read the ledger', async ({ page }) => {
    await page.goto('/signin')
    await page.getByRole('button', { name: 'admin@email.com' }).click()
    await page.locator('#login-submit').click()
    await page.waitForURL((url) => !url.pathname.startsWith('/signin'))

    await page.goto('/wallets?range=all')
    await expect(page.getByTestId('wallets')).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Paper' })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Coinbase' })).toBeVisible()
    await expect(page.getByText('Archived').first()).toBeVisible()
    await page.screenshot({ path: '/opt/cursor/artifacts/wallets-list.png', fullPage: true })

    await page.getByTestId('wallet-paper').click()
    await expect(page).toHaveURL(/\/wallet\/paper/)
    await expect(page).toHaveURL(/range=all/)
    await expect(page).toHaveURL(/wallet=paper/)
    await expect(page.getByTestId('wallet-badge')).toHaveText('Paper')
    await expect(page.getByTestId('wallet-detail')).toContainText('$993.85')
    await expect(page.getByTestId('wallet-detail')).toContainText('$289.15')
    await expect(page.getByTestId('open-positions')).toContainText('BTC-USD')
    await expect(page.getByTestId('open-positions')).toContainText('Stale')
    await expect(page.getByTestId('open-positions')).toContainText('No price mark')
    await expect(page.getByTestId('open-positions')).toContainText('+$1,900.00')
    const unrealized = page.getByTestId('unrealized')
    await expect(unrealized.filter({ hasText: dash })).toHaveCount(1)
    await page.screenshot({ path: '/opt/cursor/artifacts/wallet-paper.png', fullPage: true })

    await page.setViewportSize({ width: 390, height: 844 })
    await page.screenshot({ path: '/opt/cursor/artifacts/wallet-paper-mobile.png', fullPage: true })
    await page.setViewportSize({ width: 1440, height: 900 })

    await page.goto('/wallet/coinbase?range=all')
    await expect(page.getByTestId('wallet-badge')).toHaveText('Real')
    await expect(page).toHaveURL(/wallet=coinbase/)
    await expect(page.getByTestId('wallet-detail')).toContainText('$499.00')
    await expect(page.getByTestId('wallet-detail')).toContainText('$179.00')
    await expect(page.getByTestId('wallet-detail')).toContainText('+$19.00')
    await expect(page.getByTestId('open-positions')).toContainText('SOL-USD')
    await expect(page.getByTestId('open-positions')).not.toContainText('Stale')
    await page.screenshot({ path: '/opt/cursor/artifacts/wallet-coinbase.png', fullPage: true })

    await page.getByTestId('range-switch').getByRole('button', { name: '7D' }).click()
    await expect(page).toHaveURL(/\/wallet\/coinbase/)
    await expect(page).toHaveURL(/range=7d/)

    await page.goto('/wallet/unfunded?range=all')
    await expect(page.getByTestId('starting-balance')).toBeVisible()
    await expect(page.getByTestId('starting-balance')).toContainText('Starting balance not recorded')

    await page.getByRole('button', { name: 'Clear wallet filter' }).click()
    await expect(page).toHaveURL(/\/wallets/)
    await expect(page).toHaveURL(/range=all/)
})
