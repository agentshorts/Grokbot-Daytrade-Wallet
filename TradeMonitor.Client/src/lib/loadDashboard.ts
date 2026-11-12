import type { DashboardSnapshot } from '@/lib/book'
import { buildSample } from '@/lib/sampleData'

/** Read only. Returns the sample book until a GET read API is connected. */
export async function loadDashboard(now = new Date()): Promise<DashboardSnapshot> {
    return buildSample(now)
}
