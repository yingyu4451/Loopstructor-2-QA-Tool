import { createPinia, setActivePinia } from 'pinia'
import { flushPromises } from '@vue/test-utils'
import { nextTick } from 'vue'
import { mountWithTooltip } from './helpers/mount'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import VehiclesPage from '../src/pages/VehiclesPage.vue'
import { useAppStore } from '../src/stores/app'
import { useUiStore } from '../src/stores/ui'

describe('vehicle catalog presentation', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('shows all three star shapes and renders the game catalog image', () => {
    const store = useAppStore()
    store.catalog = {
      vehicles: [
        vehicle('Shell_ShadowRift_L1', 1, 'data:image/png;base64,AA=='),
        vehicle('Shell_ShadowRift_L2', 2, 'data:image/png;base64,BB=='),
        vehicle('Shell_ShadowRift_L3', 3, 'data:image/png;base64,CC=='),
      ],
      enchantments: [],
    }

    const wrapper = mountWithTooltip(VehiclesPage, {
      global: {
        stubs: { CatalogCard: true, VirtualCatalogGrid: true },
      },
    })

    expect(wrapper.text()).toContain('一星')
    expect(wrapper.text()).toContain('二星')
    expect(wrapper.text()).toContain('三星')
    expect(wrapper.findAll('.level-buttons button')).toHaveLength(3)
    expect(wrapper.get('.vehicle-game-icon img').attributes('src')).toBe('data:image/png;base64,AA==')
    expect(wrapper.get('.vehicle-family').classes()).toContain('card')
    expect(wrapper.find('.vehicle-family > .card-body').exists()).toBe(true)
    for (const button of wrapper.findAll('.level-buttons button')) {
      expect(button.classes()).toEqual(expect.arrayContaining(['btn', 'btn-sm']))
    }
  })

  it('finds vehicles by their built-in initial fetter names', async () => {
    const store = useAppStore()
    store.catalog = {
      vehicles: [
        { ...vehicle('Shell_HighExplosive_L1', 1, ''), name: '高爆弹', familyKey: 'Shell_HighExplosive', initialFetters: [{ id: 'Fetter_Explode', name: '高爆' }] },
        { ...vehicle('Shell_ShadowRift_L1', 1, ''), name: '暗影裂隙', familyKey: 'Shell_ShadowRift', initialFetters: [{ id: 'Fetter_Critical', name: '暴击' }] },
      ],
      enchantments: [],
    }

    const wrapper = mountWithTooltip(VehiclesPage, {
      global: { stubs: { CatalogCard: true, VirtualCatalogGrid: true } },
    })
    await wrapper.get('input[name="vehicle-search"]').setValue('高爆')
    await flushPromises()

    expect(wrapper.findAll('.vehicle-family')).toHaveLength(1)
    expect(wrapper.text()).toContain('高爆弹')
    expect(wrapper.text()).not.toContain('暗影裂隙')
    expect(wrapper.get('.fetter-chip').attributes('title')).toBe('高爆')
  })

  it('shows each family built-in fetters as icons on the right edge of its card', () => {
    const store = useAppStore()
    const explode = { id: 'Explode', name: '高爆', iconDataUrl: 'data:image/png;base64,AA==' }
    const critical = { id: 'Critical', name: '暴击', iconDataUrl: 'data:image/png;base64,BB==' }
    store.catalog = {
      vehicles: [
        { ...vehicle('Shell_HighExplosive_L1', 1, ''), name: '高爆弹', familyKey: 'Shell_HighExplosive', initialFetters: [explode] },
        { ...vehicle('Shell_HighExplosive_L2', 2, ''), name: '高爆弹', familyKey: 'Shell_HighExplosive', initialFetters: [explode, critical] },
        { ...vehicle('Shell_ShadowRift_L1', 1, ''), name: '暗影裂隙', familyKey: 'Shell_ShadowRift', initialFetters: [] },
      ],
      enchantments: [],
    }

    const wrapper = mountWithTooltip(VehiclesPage, {
      global: { stubs: { CatalogCard: true, VirtualCatalogGrid: true } },
    })
    const cards = wrapper.findAll('.vehicle-family')

    expect(cards).toHaveLength(2)
    const chips = cards[0].findAll('.family-fetters .fetter-chip')
    expect(chips.map((chip) => chip.attributes('title'))).toEqual(['高爆', '暴击'])
    expect(chips.map((chip) => chip.find('img').attributes('src'))).toEqual(['data:image/png;base64,AA==', 'data:image/png;base64,BB=='])
    expect(cards[0].get('.family-fetters').attributes('title')).toBe('一星：高爆；二星：高爆、暴击')
    expect(cards[0].get('.family-fetters').text()).toBe('')
    expect(cards[1].find('.family-fetters').exists()).toBe(false)
  })

  it('falls back to the fetter name when an icon is missing', () => {
    const store = useAppStore()
    store.catalog = {
      vehicles: [
        { ...vehicle('Link_ThunderChain_L1', 1, ''), name: '雷链', familyKey: 'Link_ThunderChain', initialFetters: [{ id: 'Link', name: '连锁' }] },
      ],
      enchantments: [],
    }

    const wrapper = mountWithTooltip(VehiclesPage, {
      global: { stubs: { CatalogCard: true, VirtualCatalogGrid: true } },
    })

    const chip = wrapper.get('.fetter-chip')
    expect(chip.classes()).toContain('fetter-chip-text')
    expect(chip.find('img').exists()).toBe(false)
    expect(chip.text()).toBe('连锁')
  })

  it('bulk-deletes field and backpack vehicles in one click', async () => {
    const store = useAppStore()
    store.connected = true
    store.snapshot = {
      protocolVersion: 1,
      version: '0.6.79',
      settings: {} as never,
      connection: { trusted: true, label: '已连接', reason: '', cheatAvailable: true, autoplayActive: true },
      logs: [],
    }
    vi.spyOn(useUiStore(), 'confirm').mockResolvedValue(true)
    const command = vi.spyOn(store, 'command').mockResolvedValue({ success: true, message: '' })
    const refreshState = vi.spyOn(store, 'refreshState').mockResolvedValue()

    const wrapper = mountWithTooltip(VehiclesPage, {
      global: { stubs: { CatalogCard: true, VirtualCatalogGrid: true } },
    })
    const buttons = wrapper.findAll('.bulk-delete-row button')

    expect(buttons).toHaveLength(2)
    expect(buttons[0].text()).toBe('删除场上全部战车')
    expect(buttons[1].text()).toBe('删除背包全部战车')
    expect(buttons[0].attributes('disabled')).toBeDefined()
    expect(buttons[1].attributes('disabled')).toBeDefined()

    store.snapshot.connection.autoplayActive = false
    await nextTick()
    expect(buttons[0].attributes('disabled')).toBeUndefined()
    expect(buttons[1].attributes('disabled')).toBeUndefined()

    await buttons[0].trigger('click')
    await flushPromises()
    expect(command).toHaveBeenCalledWith('cheat.clearFieldVehicles')
    expect(refreshState).toHaveBeenCalledTimes(1)

    await buttons[1].trigger('click')
    await flushPromises()
    expect(command).toHaveBeenCalledWith('cheat.clearBackpackVehicles')
    expect(refreshState).toHaveBeenCalledTimes(2)
  })
})

function vehicle(id: string, level: number, iconDataUrl: string) {
  return {
    id,
    enumName: id,
    name: '暗影裂隙',
    typeKey: 'Shell',
    typeName: '炮弹',
    typeOrder: 0,
    familyKey: 'Shell_ShadowRift',
    familyOrder: 0,
    itemOrder: level,
    level,
    iconDataUrl,
  }
}
