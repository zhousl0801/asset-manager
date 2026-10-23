import { mount } from '@vue/test-utils'
import { expect, test, vi } from 'vitest'
import AssetTrendChart from './AssetTrendChart.vue'
import type { AssetTrend } from '../types/trend'

const chart = vi.hoisted(() => ({
  on: vi.fn(), setOption: vi.fn(), resize: vi.fn(), dispose: vi.fn()
}))
vi.mock('echarts/core', () => ({ use: vi.fn(), init: () => chart }))

test('selecting a trend date reports the current point without changing its financial values', async () => {
  const point = {
    date: '2026-10-01', totalAssets: null, knownTotalAssets: 1200, cash: 1200,
    provident: null, fundValue: 0, fundPending: 0, fundPendingArrival: 0,
    goldValue: 0, goldPendingArrival: 0, isComplete: false,
    hasCash: true, hasProvident: false, hasFunds: false, hasGold: false
  }
  const trend: AssetTrend = { range: 'month', from: point.date, to: point.date, points: [point], notes: [] }
  const view = mount(AssetTrendChart, { props: { trend } })
  const select = chart.on.mock.calls[0]![1] as (event: unknown) => void

  select({ axesInfo: [{ axisDim: 'x', axisIndex: 0, value: 0 }] })
  expect(view.emitted('select')?.[0]).toEqual([point])

  const nextPoint = { ...point, date: '2026-10-02', totalAssets: 1500, knownTotalAssets: 1500, isComplete: true }
  await view.setProps({ trend: { ...trend, points: [nextPoint] } })
  select({ axesInfo: [{ axisDim: 'x', axisIndex: 0, value: 0 }] })
  expect(view.emitted('select')?.[1]).toEqual([nextPoint])
  select({ axesInfo: [] })
  expect(view.emitted('select')).toHaveLength(2)
  view.unmount()
  expect(chart.dispose).toHaveBeenCalledOnce()
})
