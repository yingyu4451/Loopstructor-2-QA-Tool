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

  it('bulk-deletes field and backpack vehicles in one click', async () => {
    const store = useAppStore()
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
