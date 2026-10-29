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

test('Enter on a focused control does not submit an entered amount', async () => {
  const view = mount(EntryView, { attachTo: document.body })
  await flushPromises()
  await view.get('.keypad button').trigger('click')
  expect(view.get('output[aria-label="输入金额"]').text()).toBe('7')

  const categoryEvent = new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true })
  view.get('.category-tile').element.dispatchEvent(categoryEvent)
  await flushPromises()
  expect(mocks.create).not.toHaveBeenCalled()
  expect(categoryEvent.defaultPrevented).toBe(false)
  view.unmount()
})

test('switching to income selects an income category and keeps four-per-row layout', async () => {
  const view = mount(EntryView, { attachTo: document.body })
  await flushPromises()

  await view.findAll('.kind-tabs button')[1]!.trigger('click')

  const tiles = view.findAll('.category-tile')
  expect(tiles).toHaveLength(3)
  expect(tiles[0]!.text()).toBe('工资')
  expect(tiles[0]!.classes()).toContain('active')
  view.unmount()
})
