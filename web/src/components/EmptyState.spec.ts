import { mount } from '@vue/test-utils'
import { expect, test } from 'vitest'
import EmptyState from './EmptyState.vue'

test('empty state keeps a single action and sends it to the owning page', async () => {
  const view = mount(EmptyState, { props: { title: '没有找到基金', description: '清除搜索条件后再试。', actionLabel: '查看全部基金' } })
  expect(view.findAll('button')).toHaveLength(1)
  await view.get('button').trigger('click')
  expect(view.emitted('action')).toHaveLength(1)
  await view.setProps({ actionLabel: undefined })
  expect(view.findAll('button')).toHaveLength(0)
  view.unmount()
})
