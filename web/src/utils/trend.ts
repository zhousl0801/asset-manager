import { formatMoney } from './format'
import type { AssetTrend, AssetTrendPoint, TrendRange } from '../types/trend'

export function trendAxisLabel(date: string, range: TrendRange): string {
  return range === 'year' || range === 'all' ? date.slice(0, 7) : date.slice(5)
}

export function trendTooltip(point: AssetTrendPoint): string {
  const total = point.totalAssets === null ? '完整总资产依据不足' : `总资产 ¥${formatMoney(point.totalAssets)}`
  const lines = [
    point.date,
    total,
    `部分资产合计 ¥${formatMoney(point.knownTotalAssets)}`,
    `现金 ${point.cash === null ? '依据不足' : `¥${formatMoney(point.cash)}`}`,
    `基金 ${point.fundValue === null ? '依据不足' : `¥${formatMoney(point.fundValue + point.fundPending + point.fundPendingArrival)}`}`,
    `公积金 ${point.provident === null ? '依据不足' : `¥${formatMoney(point.provident)}`}`,
    `积存金 ${point.goldValue === null ? '依据不足' : `¥${formatMoney(point.goldValue + point.goldPendingArrival)}`}`
  ]

  return lines.join('<br/>')
}

export function buildTrendOption(trend: AssetTrend) {
  const dates = trend.points.map((point) => point.date)
  const totals = trend.points.map((point) => point.totalAssets)
  const known = trend.points.map((point) => point.isComplete ? null : point.knownTotalAssets)

  return {
    animation: false,
    grid: { left: 46, right: 14, top: 18, bottom: 26 },
    tooltip: {
      trigger: 'axis',
      confine: true,
      formatter: (params: { dataIndex?: number }[]) => {
        const index = params[0]?.dataIndex

        if (index === undefined || !trend.points[index]) {
          return ''
        }

        return trendTooltip(trend.points[index]!)
      }
    },
    xAxis: {
      type: 'category',
      boundaryGap: false,
      data: dates,
      axisLabel: {
        color: '#7A8095',
        fontSize: 9,
        formatter: (value: string) => trendAxisLabel(value, trend.range)
      },
      axisLine: { lineStyle: { color: '#E6E8F0' } },
      axisTick: { show: false }
    },
    yAxis: {
      type: 'value',
      scale: true,
      axisLabel: {
        color: '#7A8095',
        fontSize: 9,
        formatter: (value: number) => value >= 10000 ? `${(value / 10000).toFixed(1)}万` : String(value)
      },
      splitLine: { lineStyle: { color: '#EEF0F6' } }
    },
    dataZoom: [{ type: 'inside', start: 0, end: 100 }],
    series: [
      {
        name: '完整总资产',
        type: 'line',
        data: totals,
        showSymbol: false,
        connectNulls: false,
        smooth: false,
        lineStyle: { width: 2, color: '#002FA7' },
        itemStyle: { color: '#002FA7' },
        areaStyle: {
          color: {
            type: 'linear',
            x: 0,
            y: 0,
            x2: 0,
            y2: 1,
            colorStops: [
              { offset: 0, color: 'rgba(0, 47, 167, 0.18)' },
              { offset: 1, color: 'rgba(0, 47, 167, 0.01)' }
            ]
          }
        }
      },
      {
        name: '部分资产合计',
        type: 'line',
        data: known,
        showSymbol: false,
        connectNulls: false,
        smooth: false,
        lineStyle: { width: 1, type: 'dashed', color: '#9AA1B4' },
        itemStyle: { color: '#9AA1B4' }
      }
    ]
  }
}
