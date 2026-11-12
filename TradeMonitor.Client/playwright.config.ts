import { defineConfig, devices } from '@playwright/test'

const baseURL = process.env.PLAYWRIGHT_BASE_URL ?? 'http://127.0.0.1:5050'

export default defineConfig({
    testDir: './e2e',
    timeout: 60_000,
    expect: { timeout: 20_000 },
    fullyParallel: false,
    retries: 0,
    use: {
        baseURL,
        channel: 'chrome',
        viewport: { width: 1440, height: 900 },
        trace: 'retain-on-failure',
        launchOptions: { args: ['--no-sandbox', '--disable-dev-shm-usage'] },
    },
    projects: [
        {
            name: 'chrome',
            use: { ...devices['Desktop Chrome'], channel: 'chrome', viewport: { width: 1440, height: 900 } },
        },
    ],
})
