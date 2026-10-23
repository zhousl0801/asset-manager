import { mount, flushPromises } from '@vue/test-utils'
import { beforeEach, expect, test, vi } from 'vitest'
import QuoteStatusView from './QuoteStatusView.vue'

const mocks = vi.hoisted(() => ({
  status: vi.fn(),
  refresh: vi.fn()
}))

vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn() }) }))
vi.mock('../api/quotes', () => ({
  fetchQuoteStatus: mocks.status,
  refreshQuotes: mocks.refresh
}))

const status = {
  schedulerEnabled: true,
  intervalSeconds: 900,
  nextRunAt: '2026-10-23T03:00:00Z',
  isRunning: false,
  lastRun: {
    trigger: 'scheduled',
    scope: 'all',
    startedAt: '2026-10-23T02:00:00Z',
    completedAt: '2026-10-23T02:00:01Z',
    durationMs: 1000,
    fundAttempted: 2,
    fundSucceeded: 1,
    fundFailed: 1,
    goldAttempted: 1,
    goldSucceeded: 1,
    goldFailed: 0,
    updated: 1,
    items: [],
    errors: ['失败基金：行情服务不可用']
  },
  items: [
    {
      assetType: 'fund', positionId: 'p1', name: '纳指基金 A', code: '000001', platform: '天天基金',
      price: 12.5, priceDate: '2026-10-23', lastAttemptAt: '2026-10-23T02:00:00Z',
      lastSource: 'eastmoney', lastError: null, status: 'fresh'
    },
    {
      assetType: 'fund', positionId: 'p2', name: '债券基金', code: '000002', platform: '银行',
      price: 1.2, priceDate: '2026-10-10', lastAttemptAt: '2026-10-23T02:00:00Z',
      lastSource: null, lastError: '行情服务不可用', status: 'failed'
    },
    {
      assetType: 'gold', positionId: null, name: '积存金参考报价', code: null, platform: null,
      price: null, priceDate: null, lastAttemptAt: null,
      lastSource: null, lastError: null, status: 'notRecorded'
    }
  ],
  checkedAt: '2026-10-23T02:01:00Z'
}

beforeEach(() => {
  vi.clearAllMocks()
  mocks.status.mockResolvedValue(structuredClone(status))
  mocks.refresh.mockResolvedValue({
    trigger: 'manual', scope: 'all',
    startedAt: '2026-10-23T02:10:00Z', completedAt: '2026-10-23T02:10:01Z',
    durationMs: 900, fundAttempted: 2, fundSucceeded: 2, fundFailed: 0,
    goldAttempted: 0, goldSucceeded: 0, goldFailed: 0, updated: 0, items: [], errors: []
  })
})

test('shows scheduler, last run and quote statuses', async () => {
  const view = mount(QuoteStatusView)
  await flushPromises()

  expect(view.text()).toContain('每 15 分钟自动刷新')
  expect(view.text()).toContain('最近一次刷新')
  expect(view.text()).toContain('基金')
  expect(view.text()).toContain('1/2')
  expect(view.text()).toContain('已更新')
  expect(view.text()).toContain('刷新失败')
  expect(view.text()).toContain('未录入')
  expect(view.text()).toContain('行情服务不可用')
  view.unmount()
})

test('manual refresh calls the batch endpoint and reloads status', async () => {
  const view = mount(QuoteStatusView)
  await flushPromises()

  await view.findAll('button').find((button) => button.text() === '刷新全部')!.trigger('click')
  await flushPromises()

  expect(mocks.refresh).toHaveBeenCalledWith('all')
  expect(mocks.status).toHaveBeenCalledTimes(2)
  expect(view.text()).toContain('刷新完成')
  view.unmount()
})

test('scope buttons refresh only that asset class', async () => {
  const view = mount(QuoteStatusView)
  await flushPromises()

  await view.findAll('button').find((button) => button.text() === '刷新基金')!.trigger('click')
  await flushPromises()
  expect(mocks.refresh).toHaveBeenLastCalledWith('funds')

  await view.findAll('button').find((button) => button.text() === '刷新参考金价')!.trigger('click')
  await flushPromises()
  expect(mocks.refresh).toHaveBeenLastCalledWith('gold')
  view.unmount()
})

test('disabled scheduler is explained on the page', async () => {
  mocks.status.mockResolvedValue({
    ...structuredClone(status),
    schedulerEnabled: false,
    lastRun: null
  })
  const view = mount(QuoteStatusView)
  await flushPromises()

  expect(view.text()).toContain('定时刷新未启用')
  expect(view.text()).not.toContain('最近一次刷新')
  view.unmount()
})
