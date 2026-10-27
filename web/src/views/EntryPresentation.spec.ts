import { flushPromises, mount } from '@vue/test-utils'
import { expect, test, vi } from 'vitest'
import EntryView from './EntryView.vue'

const mocks = vi.hoisted(() => ({ create: vi.fn(), push: vi.fn() }))
vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {} }),
  useRouter: () => ({ push: mocks.push, replace: vi.fn() })
}))
vi.mock('../api/categories', () => ({ fetchCategories: vi.fn().mockResolvedValue([]) }))
vi.mock('../api/entries', () => ({
  createEntry: mocks.create, fetchEntry: vi.fn(), reverseEntry: vi.fn(), updateEntry: vi.fn()
}))

test('Enter on more options or a history link does not submit an entered amount', async () => {
  const view = mount(EntryView, { attachTo: document.body, global: { stubs: { RouterLink: { template: '<a><slot /></a>' } } } })
  await flushPromises()
  await view.get('.keypad button').trigger('click')
  expect(view.get('output[aria-label="输入金额"]').text()).toBe('7')

  const optionsEvent = new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true })
  view.get('.entry-options summary').element.dispatchEvent(optionsEvent)
  await flushPromises()
  expect(mocks.create).not.toHaveBeenCalled()
  expect(optionsEvent.defaultPrevented).toBe(false)

  const historyEvent = new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true })
  view.get('.entry-options .back-link').element.dispatchEvent(historyEvent)
  await flushPromises()
  expect(mocks.create).not.toHaveBeenCalled()
  expect(historyEvent.defaultPrevented).toBe(false)
  view.unmount()
})
