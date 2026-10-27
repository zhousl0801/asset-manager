import { mount, flushPromises } from '@vue/test-utils'
import { beforeEach, expect, test, vi } from 'vitest'
import PlanView from './PlanView.vue'

const mocks = vi.hoisted(() => ({
  overview: vi.fn(),
  calendar: vi.fn(),
  funds: vi.fn(),
  save: vi.fn(),
  pause: vi.fn(),
  resume: vi.fn(),
  run: vi.fn(),
  retry: vi.fn(),
  skip: vi.fn()
}))

vi.mock('vue-router', () => ({ useRoute: () => ({ query: {} }), useRouter: () => ({ push: vi.fn(), replace: vi.fn() }) }))
vi.mock('../api/plans', () => ({
  fetchPlanOverview: mocks.overview,
  fetchPlanCalendar: mocks.calendar,
  savePlan: mocks.save,
  pausePlan: mocks.pause,
  resumePlan: mocks.resume,
  runPlans: mocks.run,
  retryOccurrence: mocks.retry,
  skipOccurrence: mocks.skip
}))
vi.mock('../api/funds', () => ({ fetchFundOverview: mocks.funds }))

const overview = {
  plans: [{
    id: 'plan-1',
    type: 'salaryIncome',
    name: '工资到账',
    amount: 1000,
    cycle: 'monthly',
    dayOfMonth: 15,
    dayOfWeek: null,
    time: '09:00:00',
    startDate: '2026-10-01',
    generateFrom: '2026-10-01',
    isActive: true,
    fundPositionId: null,
    fundPositionName: null,
    nextDueDate: '2026-11-15',
    version: 0
  }],
  pendingOccurrences: [] as unknown[],
  recentOccurrences: [] as unknown[],
  activeCount: 1,
  nextDueDate: '2026-11-15',
  cashBalance: 10000,
  providentBalance: 0
}

beforeEach(() => {
  vi.clearAllMocks()
  mocks.overview.mockResolvedValue(structuredClone(overview))
  mocks.calendar.mockResolvedValue({
    year: 2026,
    month: 10,
    days: [{
      date: '2026-10-19',
      isTradingDay: false,
      markets: [
        { name: '中国', market: 'CN', year: 2026, isOpen: true, isHalfDay: false, usesWeekdayFallback: false, sourceUrl: null },
        { name: '美国', market: 'US', year: 2026, isOpen: true, isHalfDay: false, usesWeekdayFallback: false, sourceUrl: null },
        { name: '香港', market: 'HK', year: 2026, isOpen: false, isHalfDay: false, usesWeekdayFallback: false, sourceUrl: null }
      ]
    }]
  })
  mocks.funds.mockResolvedValue({ positions: [] })
  document.body.innerHTML = ''
})

test('plan list shows the active salary plan and can save a new salary plan', async () => {
  mocks.save.mockResolvedValue({})
  const view = mount(PlanView, { attachTo: document.body })
  await flushPromises()

  expect(view.text()).toContain('工资到账')
  expect(view.text()).toContain('每月 15 日 09:00')

  const newButton = view.findAll('button').find((button) => button.text() === '新建计划')!
  await newButton.trigger('click')
  await flushPromises()

  const inputs = Array.from(document.querySelectorAll<HTMLInputElement>('.sheet form input'))
  inputs[1]!.value = '1000'
  inputs[1]!.dispatchEvent(new Event('input', { bubbles: true }))
  await flushPromises()

  document.querySelector('.sheet form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
  await flushPromises()

  expect(mocks.save).toHaveBeenCalledTimes(1)
  expect(mocks.save.mock.calls[0]![0]).toMatchObject({
    type: 'salaryIncome',
    amount: 1000,
    cycle: 'monthly',
    dayOfMonth: 15
  })
  expect(mocks.save.mock.calls[0]![0].requestId).toBeTruthy()
  view.unmount()
})

test('run-now reports results and the preview section is gone', async () => {
  mocks.run.mockResolvedValue({ createdOccurrences: 1, completed: 1, waiting: 0, failed: 0 })
  const view = mount(PlanView, { attachTo: document.body })
  await flushPromises()

  expect(view.text()).not.toContain('执行预览')
  expect(view.text()).not.toContain('试算本期')

  const runButton = view.findAll('button').find((button) => button.text() === '立即执行')!
  await runButton.trigger('click')
  await flushPromises()

  expect(mocks.run).toHaveBeenCalledTimes(1)
  expect(view.find('.notice').text()).toContain('已记账 1')
  view.unmount()
})

test('plans are grouped by type and share classes of the same fund merge into one card', async () => {
  const salary = overview.plans[0]!
  const fund = {
    ...salary,
    id: 'fund-a',
    type: 'fundSip',
    name: '大成纳斯达克100ETF联接 A · 交易日定投',
    amount: 100,
    cycle: 'tradingDay',
    dayOfMonth: null,
    dayOfWeek: null,
    fundPositionId: 'position-a',
    fundPositionName: '大成纳斯达克100ETF联接 A',
    fundTarget: { name: '大成纳斯达克100ETF联接', code: '000834', shareClass: 'A' },
    nextDueDate: '2026-10-28'
  }
  const fundC = {
    ...fund,
    id: 'fund-c',
    name: '大成纳斯达克100ETF联接 C · 交易日定投',
    fundPositionId: 'position-c',
    fundPositionName: '大成纳斯达克100ETF联接 C',
    fundTarget: { name: '大成纳斯达克100ETF联接', code: '008971', shareClass: 'C' }
  }
  const provident = {
    ...salary,
    id: 'provident-1',
    type: 'providentDeposit',
    name: '公积金缴存',
    amount: 3444
  }
  mocks.overview.mockResolvedValue({
    ...structuredClone(overview),
    plans: [salary, fund, fundC, provident],
    activeCount: 4
  })

  const view = mount(PlanView, { attachTo: document.body })
  await flushPromises()

  expect(view.text()).toContain('基金定投')
  expect(view.text()).toContain('公积金')
  expect(view.text()).toContain('定期收入')
  expect(view.findAll('.fund-plan-card')).toHaveLength(1)
  expect(view.findAll('.fund-plan-row')).toHaveLength(2)
  expect(view.find('.fund-plan-card').text()).toContain('A 类 / C 类')
  expect(view.find('.fund-plan-card').text()).toContain('¥200.00')
  expect(view.find('.group-provident').text()).toContain('公积金缴存')
  expect(view.find('.group-income').text()).toContain('工资到账')
  view.unmount()
})


test('trading-day SIP selects one share class across channels and sends a business target', async () => {
  const position = { id: 'source-1', name: '测试基金', code: '000001', shareClass: 'C', platform: '渠道甲' }
  mocks.funds.mockResolvedValue({ positions: [position, { ...position, id: 'source-2', platform: '渠道乙' }] })
  mocks.save.mockResolvedValue({})
  const view = mount(PlanView, { attachTo: document.body })
  await flushPromises()
  await view.findAll('button').find(button => button.text() === '新建计划')!.trigger('click')
  const select = (label: string) => [...document.querySelectorAll('label')].find(item => item.querySelector('span')?.textContent === label)!.querySelector('select')!
  const type = select('计划类型')
  type.value = 'fundSip'
  type.dispatchEvent(new Event('change', { bubbles: true }))
  await flushPromises()
  const target = select('基金及份额类别')
  expect(target.options).toHaveLength(2)
  expect(target.textContent).not.toContain('渠道甲')
  target.value = 'source-1'
  target.dispatchEvent(new Event('change', { bubbles: true }))
  const cycle = select('周期')
  cycle.value = 'tradingDay'
  cycle.dispatchEvent(new Event('change', { bubbles: true }))
  const amount = [...document.querySelectorAll('label')].find(item => item.querySelector('span')?.textContent === '每期金额（元）')!.querySelector('input')!
  amount.value = '100'
  amount.dispatchEvent(new Event('input', { bubbles: true }))
  await flushPromises()
  expect(select('周期').value).toBe('tradingDay')
  expect(document.querySelector('.sheet')!.textContent).not.toContain('每月日期')
  expect(document.querySelector('.sheet')!.textContent).not.toContain('星期')
  document.querySelector('.sheet form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
  await flushPromises()
  expect(mocks.save).toHaveBeenCalledWith(expect.objectContaining({
    cycle: 'tradingDay', dayOfMonth: null, dayOfWeek: null, fundPositionId: null,
    fundTarget: { name: '测试基金', code: '000001', shareClass: 'C' }
  }))
  view.unmount()
})

test('calendar shows official coverage, missing-year fallback and overseas closures', async () => {
  const markets = [
    { name: '中国', market: 'CN', year: 2027, isOpen: true, isHalfDay: false, usesWeekdayFallback: true, sourceUrl: null },
    { name: '美国', market: 'US', year: 2027, isOpen: false, isHalfDay: false, usesWeekdayFallback: false, sourceUrl: 'https://www.nasdaq.com/market-activity/stock-market-holiday-schedule' },
    { name: '香港', market: 'HK', year: 2027, isOpen: true, isHalfDay: false, usesWeekdayFallback: true, sourceUrl: null }
  ]
  const day = { date: '2027-01-04', isTradingDay: false, markets }
  mocks.calendar.mockResolvedValue({ year: 2026, month: 10, days: [day] })
  const view = mount(PlanView, { attachTo: document.body })
  await flushPromises()

  expect(view.text()).toContain('交易日历')
  expect(view.text()).toContain('中国 2027 · 工作日回退')
  expect(view.text()).toContain('美国 2027 · 官方日历')
  expect(view.text()).toContain('本月有 1 天因海外休市')
  expect(view.find('.closure-tag').text()).toBe('美')
  view.unmount()
})


test('editing a SIP with unavailable fund choices shows retry instead of submitting', async () => {
  mocks.overview.mockResolvedValue({ ...structuredClone(overview), plans: [{ ...overview.plans[0], type: 'fundSip', fundPositionId: 'source-1' }] })
  mocks.funds.mockRejectedValue(new Error('offline'))
  const view = mount(PlanView, { attachTo: document.body })
  await flushPromises()
  await view.findAll('button').find(button => button.text() === '编辑')!.trigger('click')
  await flushPromises()
  expect(document.querySelector('.sheet')!.textContent).toContain('基金类别加载失败，请重试。')
  document.querySelector('.sheet form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
  await flushPromises()
  expect(mocks.save).not.toHaveBeenCalled()
  expect(document.querySelector('button[type="submit"]')!.hasAttribute('disabled')).toBe(true)
  view.unmount()
})
