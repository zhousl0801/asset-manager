import { expect, test } from 'vitest'
import { buildScopedTrend, buildTrendOption, trendAxisLabel, trendTooltip } from './trend'
import type { AssetTrend, AssetTrendPoint } from '../types/trend'

function point(overrides: Partial<AssetTrendPoint>): AssetTrendPoint {
  return {
    date: '2026-10-01',
    totalAssets: null,
    knownTotalAssets: 0,
    cash: null,
    provident: null,
    fundValue: null,
    fundPending: 0,
    fundPendingArrival: 0,
    goldValue: null,
    goldPendingArrival: 0,
    isComplete: false,
    hasCash: false,
    hasProvident: false,
    hasFunds: false,
    hasGold: false,
    ...overrides
  }
}

const trend: AssetTrend = {
  range: 'month',
  from: '2026-10-01',
  to: '2026-10-02',
  notes: [],
  points: [
    point({
      date: '2026-10-01',
      cash: 1000,
      provident: 0,
      fundValue: 0,
      goldValue: 0,
      hasCash: true,
      hasProvident: true,
      hasFunds: true,
      hasGold: true,
      isComplete: true
    }),
    point({
      date: '2026-10-02',
      cash: 1200,
      provident: null,
      fundValue: 0,
      goldValue: 0,
      hasCash: true,
      hasProvident: false,
      hasFunds: true,
      hasGold: true
    })
  ]
}

test('scoped trend keeps the baseline categories and leaves incomplete dates empty', () => {
  const scoped = buildScopedTrend(trend)
  const option = buildTrendOption(trend, scoped) as {
    series: { data: (number | null)[]; markLine: { data: unknown[] } }[]
  }

  expect(scoped.baselineTypes).toEqual(['cash', 'providentFund', 'fund', 'gold'])
  expect(option.series[0]!.data).toEqual([1000, null])
  expect(option.series[0]!.markLine.data).toEqual([])
  expect(trendAxisLabel('2026-10-02', 'month')).toBe('10-02')
  expect(trendAxisLabel('2026-10-02', 'all')).toBe('2026-10')
  expect(trendTooltip(trend.points[1]!, scoped)).toContain('同口径资产依据不足')
  expect(trendTooltip(trend.points[1]!, scoped)).toContain('公积金 依据不足')
})

test('new categories are excluded from the line and marked on their first day', () => {
  const withAddition: AssetTrend = {
    ...trend,
    points: [
      point({ date: '2026-10-01', cash: 1000, hasCash: true }),
      point({ date: '2026-10-02', cash: 1000, goldValue: 500, hasCash: true, hasGold: true })
    ]
  }
  const scoped = buildScopedTrend(withAddition)
  const option = buildTrendOption(withAddition, scoped) as {
    series: { data: (number | null)[]; markLine: { data: { xAxis: string }[] } }[]
  }

  expect(scoped.baselineTypes).toEqual(['cash'])
  expect(scoped.additions).toEqual([{ date: '2026-10-02', types: ['gold'] }])
  expect(option.series[0]!.data).toEqual([1000, 1000])
  expect(option.series[0]!.markLine.data[0]!.xAxis).toBe('2026-10-02')
  expect(trendTooltip(withAddition.points[1]!, scoped)).toContain('新增记录 积存金')
})
