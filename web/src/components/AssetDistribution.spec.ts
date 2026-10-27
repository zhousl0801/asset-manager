import { mount } from '@vue/test-utils'
import { expect, test } from 'vitest'
import AssetDistribution from './AssetDistribution.vue'
import type { LedgerOverview } from '../types/ledger'

const overview: LedgerOverview = {
  id: 'ledger',
  currency: 'CNY',
  timeZone: 'Asia/Shanghai',
  startDate: '2026-10-01',
  asOfDate: '2026-10-23',
  isComplete: false,
  totalAssets: 3500,
  cash: { isConfigured: true, amount: 2000, note: '统一余额', notHeld: false },
  fund: { isConfigured: false, amount: null, note: '尚未录入', notHeld: false },
  providentFund: { isConfigured: true, amount: 1000, note: '本月暂无缴存', notHeld: false },
  gold: { isConfigured: true, amount: 500, note: '持有 1 克', notHeld: false },
  month: { income: 0, expense: 0, surplus: 0, hasRecords: false },
  fundNotHeld: false,
  providentFundNotHeld: false,
  goldNotHeld: false,
  coverageComplete: false
}

const changes = [
  { assetType: 'cash' as const, change: 0, isNew: false },
  { assetType: 'providentFund' as const, change: null, isNew: true },
  { assetType: 'fund' as const, change: null, isNew: false },
  { assetType: 'gold' as const, change: null, isNew: true }
]

const stubs = {
  RouterLink: { props: ['to'], template: '<a><slot /></a>' }
}

test('shows recorded categories, percentages, new records and missing assets', () => {
  const view = mount(AssetDistribution, {
    props: { overview, changes, comparable: false },
    global: { stubs }
  })
  const text = view.text()

  expect(text).toContain('已记录 3 类')
  expect(text).toContain('现金')
  expect(text).toContain('57.1%')
  expect(text).toContain('公积金')
  expect(text).toContain('本月新增')
  expect(text).toContain('暂未录入')
  expect(text).toContain('较上月 —')
  expect(text).toContain('添加')
  view.unmount()
})

test('editable mode offers add and not-held actions for missing assets', async () => {
  const view = mount(AssetDistribution, {
    props: { overview, changes, comparable: false, editable: true },
    global: { stubs }
  })
  const buttons = view.findAll('button')

  expect(buttons.some((button) => button.text() === '不持有')).toBe(true)
  expect(view.text()).toContain('添加')

  const notHeld = buttons.find((button) => button.text() === '不持有')!
  await notHeld.trigger('click')
  expect(view.emitted('toggleNotHeld')?.[0]).toEqual(['fund', true])
  view.unmount()
})
