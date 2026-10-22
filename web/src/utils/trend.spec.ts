import { expect, test } from 'vitest'
import { buildTrendOption, trendAxisLabel, trendTooltip } from './trend'
import type { AssetTrend } from '../types/trend'

const trend: AssetTrend = {
  range: 'month',
  from: '2026-10-01',
  to: '2026-10-03',
  notes: [],
  points: [
    {
      date: '2026-10-01',
      totalAssets: 1000,
      knownTotalAssets: 1000,
      cash: 1000,
      provident: 0,
      fundValue: 0,
      fundPending: 0,
      fundPendingArrival: 0,
      goldValue: 0,
      goldPendingArrival: 0,
      isComplete: true
    },
    {
      date: '2026-10-02',
      totalAssets: null,
      knownTotalAssets: 1200,
      cash: 1200,
      provident: null,
      fundValue: 0,
      fundPending: 0,
      fundPendingArrival: 0,
      goldValue: 0,
      goldPendingArrival: 0,
      isComplete: false
    }
  ]
}

test('trend option keeps incomplete dates empty and adds a partial-total series', () => {
  const option = buildTrendOption(trend) as { series: { data: (number | null)[] }[] }

  expect(option.series[0]!.data).toEqual([1000, null])
  expect(option.series[1]!.data).toEqual([null, 1200])
  expect(trendAxisLabel('2026-10-02', 'month')).toBe('10-02')
  expect(trendAxisLabel('2026-10-02', 'all')).toBe('2026-10')
  expect(trendTooltip(trend.points[1]!)).toContain('完整总资产依据不足')
  expect(trendTooltip(trend.points[1]!)).toContain('公积金 依据不足')
})
