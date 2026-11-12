import { describe, expect, it } from 'vite-plus/test'

/** Account sign-in, registration, and the session helper those pages call. */
const signInFlow = new Set(['pages/signin.vue', 'pages/signup.vue', 'lib/auth.ts'])

const patterns: { name: string; re: RegExp }[] = [
    { name: '.post(', re: /\.post\s*\(/ },
    { name: '.put(', re: /\.put\s*\(/ },
    { name: '.patch(', re: /\.patch\s*\(/ },
    { name: '.delete(', re: /\.delete\s*\(/ },
    { name: '.apiForm(', re: /\.apiForm\s*\(/ },
    { name: '.api(', re: /\.api\s*\(/ },
    { name: 'mutating method', re: /method\s*:\s*['"](?:POST|PUT|PATCH|DELETE)['"]/i },
]

function sourcePath(key: string) {
    if (key.startsWith('./')) return `lib/${key.slice(2)}`
    if (key.startsWith('../')) return key.slice(3)
    return key
}

const sources = import.meta.glob(['../*.{vue,ts}', '../**/*.{vue,ts}'], {
    query: '?raw',
    import: 'default',
    eager: true,
})

describe('read-only client', () => {
    it('does not send a data-changing request outside the sign-in flow', () => {
        const keys = Object.keys(sources).map(sourcePath)
        expect(keys.some((key) => key.endsWith('pages/signin.vue'))).toBe(true)
        expect(keys.some((key) => key.endsWith('pages/index.vue'))).toBe(true)
        expect(keys).toContain('lib/auth.ts')

        const hits: string[] = []
        for (const [key, text] of Object.entries(sources)) {
            const rel = sourcePath(key)
            if (rel.endsWith('.test.ts')) continue
            if (signInFlow.has(rel)) continue
            if (typeof text !== 'string') continue
            for (const pattern of patterns) {
                if (pattern.re.test(text)) hits.push(`${rel} matches ${pattern.name}`)
            }
        }
        expect(hits).toEqual([])
    })
})
