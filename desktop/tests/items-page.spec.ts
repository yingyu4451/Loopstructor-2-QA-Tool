import { createPinia, setActivePinia } from 'pinia'
import { mountWithTooltip } from './helpers/mount'
import { beforeEach, describe, expect, it } from 'vitest'
import ItemsPage from '../src/pages/ItemsPage.vue'
import { useAppStore } from '../src/stores/app'

describe('item catalog presentation', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('keeps the consumable heading free and places the catalog pool switch beside the search box', () => {
    const store = useAppStore()
    store.catalog = {
      disposables: [item('Potion_Heal', '修复凝胶')],
      catapultPoints: [],
    }

    const wrapper = mountWithTooltip(ItemsPage, {
      global: { stubs: { VirtualCatalogGrid: true } },
    })

    const heading = wrapper.get('.inventory-column .section-heading')
    expect(heading.text()).toContain('消耗品')
    expect(heading.find('.pool-switch').exists()).toBe(false)

    const searchRow = wrapper.get('.search-row')
    expect(searchRow.find('.search-box').exists()).toBe(true)
    expect(searchRow.find('.pool-switch').exists()).toBe(true)
    expect(searchRow.findAll('.pool-option')).toHaveLength(2)
  })
})

function item(id: string, name: string) {
  return {
    id,
    enumName: id,
    name,
    groupKey: 'consumable',
    groupName: '消耗品',
    order: 0,
  }
}
