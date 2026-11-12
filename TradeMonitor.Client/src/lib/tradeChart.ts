import type { ChartMarkerPoint, PriceMarkRow } from '@/api'
import { toChartPoints, toTradeMarkers, type ChartMarker, type PricePoint } from '@/lib/priceSeries'

export interface TradeChartModel {
    points: PricePoint[]
    markers: ChartMarker[]
    empty: boolean
}

/**
 * Price marks are the line. Each marker is drawn on the series value at its time,
 * so the point at a fill is the recorded fill price. That keeps the arrow on the fill.
 */
export function tradeChartModel(detail: { hasPriceMarks: boolean; priceMarks: PriceMarkRow[]; markers: ChartMarkerPoint[] }): TradeChartModel {
    if (!detail.hasPriceMarks || detail.priceMarks.length === 0) {
        return { points: [], markers: [], empty: true }
    }

    const points = toChartPoints(detail.priceMarks.map((mark) => ({ at: mark.at, price: mark.price })))
    const byTime = new Map(points.map((point) => [point.time, point.value]))
    for (const marker of detail.markers) {
        const time = Math.floor(new Date(marker.at).getTime() / 1000)
        if (Number.isNaN(time)) continue
        byTime.set(time, marker.price)
    }

    const markers = toTradeMarkers(detail.markers.map((marker) => ({ executedAt: marker.at, side: marker.side, qty: marker.qty })))
    return {
        points: [...byTime.entries()].sort((a, b) => a[0] - b[0]).map(([time, value]) => ({ time, value })),
        markers,
        empty: false,
    }
}

export function chartPointsAttr(points: PricePoint[]): string {
    return points.map((point) => `${point.time}=${point.value}`).join('|')
}

export function chartMarkersAttr(markers: ChartMarker[]): string {
    return markers.map((marker) => `${marker.time}=${marker.text}`).join('|')
}
