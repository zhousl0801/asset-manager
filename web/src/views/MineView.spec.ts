import { mount, flushPromises } from '@vue/test-utils'
import { beforeEach, expect, test, vi } from 'vitest'
import MineView from './MineView.vue'
import { initializeTheme, setThemeMode } from '../utils/theme'

const mocks = vi.hoisted(() => ({ plans: vi.fn() }))

vi.mock('../api/plans', () => ({ fetchPlanOverview: mocks.plans }))

beforeEach(() => {
  vi.clearAllMocks()
  localStorage.clear()
  mocks.plans.mockResolvedValue({ activeCount: 2 })
  initializeTheme()
  setThemeMode('system')
})

test('theme setting switches between system, light and dark', async () => {
  const view = mount(MineView, {
    global: {
      stubs: {
        RouterLink: { template: '<a><slot /></a>' }
      }
    }
  })
  await flushPromises()

  const themeRow = view.findAll('button').find((button) => button.text().startsWith('主题'))!
  await themeRow.trigger('click')
  await flushPromises()

  const options = [...document.querySelectorAll<HTMLButtonElement>('.sheet .theme-option')]
  expect(options).toHaveLength(3)
  expect(options[0]!.getAttribute('aria-checked')).toBe('true')

  options[2]!.click()
  await flushPromises()

  expect(document.documentElement.dataset.colorScheme).toBe('dark')
  expect(document.querySelectorAll<HTMLButtonElement>('.sheet .theme-option')[2]!.getAttribute('aria-checked'))
    .toBe('true')

  view.unmount()
})
