import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, expect, test, vi } from 'vitest'
import HistoryView from './HistoryView.vue'
import CashAdjustmentLink from '../components/CashAdjustmentLink.vue'
import { ApiError } from '../api/client'

const mocks = vi.hoisted(() => ({ history: vi.fn(), preview: vi.fn(), confirm: vi.fn() }))
vi.mock('../api/history', () => ({ fetchHistory: mocks.history, previewHistory: mocks.preview, confirmHistory: mocks.confirm }))
vi.mock('vue-router', () => ({ useRoute: () => ({ query: {} }), useRouter: () => ({ push: vi.fn(), replace: vi.fn() }) }))
const snapshot = { asOfDate: '2026-10-21', cashBalance: 1000, providentBalance: null, funds: [], stocks: [], gold: null, adjustments: [], issues: ['公积金尚无依据'], knownTotalAssets: 1000, totalAssets: null }
const cash = { id: 'cash-id', source: 'cashOpening', date: '2026-10-01', amount: 1000, sequence: 0, version: 1, quantity: null, price: null, priceDate: null, isReversed: false, needsDateConfirmation: false }
const history = { ledgerVersion: 1, cashOpening: cash, facts: [], prices: [], audit: [], snapshot }
beforeEach(() => {
  vi.clearAllMocks()
  document.body.innerHTML = ''
  mocks.history.mockResolvedValue(structuredClone(history))
  mocks.preview.mockResolvedValue({ ledgerVersion: 1, before: snapshot, after: { ...snapshot, cashBalance: 1200 }, previewToken: 'signed-preview' })
  mocks.confirm.mockResolvedValue({})
})
function button(text: string): HTMLButtonElement {
  return Array.from(document.querySelectorAll('button')).find(item => item.textContent?.trim() === text)!
}
async function input(label: string, value: string): Promise<void> {
  const element = Array.from(document.querySelectorAll('label')).find(item => item.querySelector('span')?.textContent === label)!.querySelector('input')!
  element.value = value
  element.dispatchEvent(new Event('input', { bubbles: true }))
  await flushPromises()
}

test('history shows incomplete coverage explicitly and requires a fresh preview after editing', async () => {
  const view = mount(HistoryView, { attachTo: document.body, global: { stubs: { RouterLink: true } } })
  await flushPromises()
  expect(view.text()).toContain('不代表完整总资产')
  expect(button('确认保存历史变更')).toBeUndefined()
  button('更正').click()
  await flushPromises()
  await input('变更原因', '现金起点录错')
  await input('金额 / 持仓成本（元）', '1200')
  button('预览影响').click()
  await flushPromises()
  expect(mocks.preview).toHaveBeenCalledTimes(1)
  expect(mocks.confirm).not.toHaveBeenCalled()
  expect(button('确认保存历史变更')).toBeDefined()
  await input('金额 / 持仓成本（元）', '1300')
  expect(button('确认保存历史变更')).toBeUndefined()
  view.unmount()
})

test('history confirms the exact previewed request and retains draft on a conflict', async () => {
  mocks.confirm.mockRejectedValue(new ApiError(409, '账本已变化，请重新预览'))
  const view = mount(HistoryView, { attachTo: document.body, global: { stubs: { RouterLink: true } } })
  await flushPromises()
  button('更正').click()
  await flushPromises()
  await input('变更原因', '现金起点录错')
  await input('金额 / 持仓成本（元）', '1200')
  button('预览影响').click()
  await flushPromises()
  button('确认保存历史变更').click()
  await flushPromises()
  expect(mocks.confirm.mock.calls[0]![0]).toEqual(mocks.preview.mock.calls[0]![0])
  expect(mocks.confirm.mock.calls[0]![1]).toBe('signed-preview')
  expect(document.body.textContent).toContain('账本已变化，请重新预览')
  expect((Array.from(document.querySelectorAll('input')).find(item => item.value === '1200'))).toBeDefined()
  view.unmount()
})

test('cash links load on demand and show only matching direction and dates', async () => {
  mocks.history.mockResolvedValue({ ...history, facts: [
    { id: 'match', source: 'adjustment', date: '2026-10-21', recordedDifference: -50, note: '漏记' },
    { id: 'old', source: 'adjustment', date: '2026-10-01', recordedDifference: -50 },
    { id: 'income', source: 'adjustment', date: '2026-10-21', recordedDifference: 50 }
  ] })
  const view = mount(CashAdjustmentLink, { props: { businessDate: '2026-10-20', cashEffect: -50 } })
  expect(mocks.history).not.toHaveBeenCalled()
  await view.find('button').trigger('click')
  await flushPromises()
  expect(view.findAll('option')).toHaveLength(2)
  await view.find('select').setValue('match')
  expect(view.emitted('update:modelValue')![0]).toEqual(['match'])
  view.unmount()
})


test('a preview response arriving after a draft edit cannot restore confirmation', async () => {
  let resolvePreview!: (value: unknown) => void
  mocks.preview.mockImplementation(() => new Promise(resolve => { resolvePreview = resolve }))
  const view = mount(HistoryView, { attachTo: document.body, global: { stubs: { RouterLink: true } } })
  await flushPromises()
  button('更正').click()
  await flushPromises()
  await input('变更原因', '更正现金')
  await input('金额 / 持仓成本（元）', '1200')
  button('预览影响').click()
  await flushPromises()
  await input('金额 / 持仓成本（元）', '1300')
  resolvePreview({ ledgerVersion: 1, before: snapshot, after: { ...snapshot, cashBalance: 1200 }, previewToken: 'old-preview' })
  await flushPromises()
  expect(button('确认保存历史变更')).toBeUndefined()
  expect(mocks.confirm).not.toHaveBeenCalled()
  view.unmount()
})

test('quote changes preview the effective price, date, valuation and known total', async () => {
  const gold = { positionId: null, quantity: 10, cost: 8000, realizedProfit: 0, price: 900, priceDate: '2026-10-21', value: 9000, canRebuild: true }
  const before = { ...snapshot, gold, knownTotalAssets: 10000 }
  const after = { ...before, gold: { ...gold, price: 1000, priceDate: '2026-10-20', value: 10000 }, knownTotalAssets: 11000 }
  mocks.preview.mockResolvedValue({ ledgerVersion: 1, before, after, previewToken: 'quote-preview' })
  const view = mount(HistoryView, { attachTo: document.body, global: { stubs: { RouterLink: true } } })
  await flushPromises()
  button('更正').click()
  await flushPromises()
  await input('变更原因', '更正报价')
  button('预览影响').click()
  await flushPromises()
  const preview = document.querySelector('.history-preview')!
  expect(preview.textContent).toContain('积存金报价')
  expect(preview.textContent).toContain('1,000.00')
  expect(preview.textContent).toContain('2026-10-20')
  expect(preview.textContent).toContain('10,000.00')
  expect(preview.textContent).toContain('11,000.00')
  view.unmount()
})
