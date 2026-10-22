import { mount, flushPromises } from '@vue/test-utils'
import { beforeEach, expect, test, vi } from 'vitest'
import PlanView from './PlanView.vue'

const mocks = vi.hoisted(() => ({
  overview: vi.fn(),
  funds: vi.fn(),
  save: vi.fn(),
  pause: vi.fn(),
  resume: vi.fn(),
  preview: vi.fn(),
  run: vi.fn(),
  retry: vi.fn(),
  skip: vi.fn()
}))

vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn(), replace: vi.fn() }) }))
vi.mock('../api/plans', () => ({
  fetchPlanOverview: mocks.overview,
  savePlan: mocks.save,
  pausePlan: mocks.pause,
  resumePlan: mocks.resume,
  previewPlans: mocks.preview,
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

test('preview shows the projected rows and run-now result', async () => {
  mocks.preview.mockResolvedValue({
    date: '2026-10-21',
    trialCashStart: 100,
    trialCashEnd: 1100,
    providentStart: 0,
    providentEnd: 0,
    rows: [{ planId: 'plan-1', planName: '工资到账', type: 'salaryIncome', amount: 1000, status: '预计记账', cashAfter: 1100, providentAfter: 0, note: null }],
    notes: ['仅试算所选日']
  })
  mocks.run.mockResolvedValue({ createdOccurrences: 1, completed: 1, waiting: 0, failed: 0 })
  const view = mount(PlanView, { attachTo: document.body })
  await flushPromises()

  const previewButton = view.findAll('button').find((button) => button.text() === '试算本期')!
  await previewButton.trigger('click')
  await flushPromises()

  expect(view.text()).toContain('工资到账')
  expect(view.text()).toContain('预计记账')

  const runButton = view.findAll('button').find((button) => button.text() === '立即执行')!
  await runButton.trigger('click')
  await flushPromises()

  expect(mocks.run).toHaveBeenCalledTimes(1)
  expect(view.find('.notice').text()).toContain('已记账 1')
  view.unmount()
})
