import { addAmounts, formatMoney } from './format'
import { chartFontSize, chartTheme } from './chartTheme'
import type { AssetTrend, AssetTrendPoint, TrendRange } from '../types/trend'

export type ScopedAssetKey = 'cash' | 'providentFund' | 'fund' | 'gold' | 'stock'

export interface ScopedTrendPoint {
  date: string
  value: number | null
  source: AssetTrendPoint
}

export interface TrendAddition {
  date: string
  types: ScopedAssetKey[]
}

export interface ScopedTrend {
  points: ScopedTrendPoint[]
  additions: TrendAddition[]
  baselineTypes: ScopedAssetKey[]
  latest: ScopedTrendPoint | null
}

const assetOrder: ScopedAssetKey[] = ['cash', 'providentFund', 'fund', 'gold', 'stock']

export const assetLabels: Record<ScopedAssetKey, string> = {
  cash: '现金',
  providentFund: '公积金',
  fund: '基金',
  gold: '积存金',
  stock: '股票'
}

export function assetPresent(point: AssetTrendPoint, key: ScopedAssetKey): boolean {
  switch (key) {
    case 'cash':
      return point.hasCash
    case 'providentFund':
      return point.hasProvident
    case 'fund':
      return point.hasFunds
    case 'gold':
      return point.hasGold
    case 'stock':
      return point.hasStocks ?? false
  }
}

export function assetValue(point: AssetTrendPoint, key: ScopedAssetKey): number | null {
  switch (key) {
    case 'cash':
      return point.cash
    case 'providentFund':
      return point.provident
    case 'fund':
      return point.fundValue === null ? null : addAmounts(point.fundValue, point.fundPending, point.fundPendingArrival)
    case 'gold':
      return point.goldValue === null ? null : addAmounts(point.goldValue, point.goldPendingArrival)
    case 'stock':
      return point.stockValue ?? null
  }
}

export function buildScopedTrend(trend: AssetTrend): ScopedTrend {
  const firstRecorded = trend.points.find((point) =>
    assetOrder.some((key) => assetPresent(point, key)))
  const baselineTypes = firstRecorded
    ? assetOrder.filter((key) => assetPresent(firstRecorded, key))
    : []
  const additionsByDate = new Map<string, ScopedAssetKey[]>()

  for (const key of assetOrder) {
    if (baselineTypes.includes(key)) {
      continue
    }

    const first = trend.points.find((point) => assetPresent(point, key))

    if (!first) {
      continue
    }

    const types = additionsByDate.get(first.date) ?? []
    types.push(key)
    additionsByDate.set(first.date, types)
  }

  const points = trend.points.map((point) => {
    let total = 0
    let complete = baselineTypes.length > 0

    for (const key of baselineTypes) {
      const item = assetPresent(point, key) ? assetValue(point, key) : null

      if (item === null) {
        complete = false
        break
      }

      total += item
    }

    return { date: point.date, value: complete ? total : null, source: point }
  })
  const latest = [...points].reverse().find((point) => point.value !== null)
    ?? points[points.length - 1]
    ?? null

  return {
    points,
    additions: [...additionsByDate.entries()].map(([date, types]) => ({ date, types })),
    baselineTypes,
    latest
  }
}

export function trendAxisLabel(date: string, range: TrendRange): string {
  return range === 'quarter' || range === 'all' ? date.slice(0, 7) : date.slice(5)
}

export function trendTooltip(point: AssetTrendPoint, scoped: ScopedTrend): string {
  const scopedPoint = scoped.points.find((item) => item.date === point.date)
  const addition = scoped.additions.find((item) => item.date === point.date)
  const lines = [
    point.date,
    scopedPoint?.value == null
      ? '同口径资产依据不足'
      : `同口径资产 ¥${formatMoney(scopedPoint.value)}`,
    ...scoped.baselineTypes.map((key) => {
      const value = assetValue(point, key)
      return `${assetLabels[key]} ${value === null ? '依据不足' : `¥${formatMoney(value)}`}`
    })
  ]

  if (addition) {
    lines.push(`新增记录 ${addition.types.map((key) => assetLabels[key]).join('、')}`)
  }

  return lines.join('<br/>')
}

export function buildTrendOption(trend: AssetTrend, scoped: ScopedTrend) {
  const theme = chartTheme()
  const dates = scoped.points.map((point) => point.date)
  const values = scoped.points.map((point) => point.value)
  const marks = scoped.additions.map((addition) => ({
    xAxis: addition.date,
    lineStyle: { type: 'dashed', color: theme.pointer, width: 1 },
    label: {
      show: true,
      position: 'insideEndTop',
      formatter: '新增',
      color: theme.text,
      fontSize: chartFontSize(12)
    }
  }))

  return {
    animation: false,
    grid: { left: 10, right: 46, top: 28, bottom: 24 },
    tooltip: {
      trigger: 'axis',
      confine: true,
      axisPointer: { type: 'line', lineStyle: { color: theme.pointer, type: 'dashed' } },
      formatter: (params: { dataIndex?: number }[]) => {
        const index = params[0]?.dataIndex

        if (index === undefined || !scoped.points[index]) {
          return ''
        }

        return trendTooltip(scoped.points[index]!.source, scoped)
      }
    },
    xAxis: {
      type: 'category',
      boundaryGap: false,
      data: dates,
      axisLabel: {
        color: theme.text,
        fontSize: chartFontSize(12),
        formatter: (value: string) => trendAxisLabel(value, trend.range)
      },
      axisLine: { lineStyle: { color: theme.axis } },
      axisTick: { show: false }
    },
    yAxis: {
      type: 'value',
      scale: true,
      axisLabel: { show: false },
      axisLine: { show: false },
      axisTick: { show: false },
      splitLine: { show: false }
    },
    series: [
      {
        name: '同口径资产',
        type: 'line',
        data: values,
        showSymbol: false,
        connectNulls: false,
        smooth: false,
        lineStyle: { width: 2, color: theme.brand },
        itemStyle: { color: theme.brand },
        areaStyle: {
          color: {
            type: 'linear',
            x: 0,
            y: 0,
            x2: 0,
            y2: 1,
            colorStops: [
              { offset: 0, color: theme.areaStrong },
              { offset: 1, color: theme.areaSoft }
            ]
          }
        },
        markPoint: {
          symbolSize: 6,
          data: [
            { type: 'max', name: '最高' },
            { type: 'min', name: '最低' }
          ],
          label: {
            show: false
          }
        },
        markLine: {
          symbol: 'none',
          silent: true,
          data: marks
        }
      }
    ]
  }
}
