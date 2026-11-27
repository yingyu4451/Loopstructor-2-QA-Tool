import { createPinia, setActivePinia } from 'pinia'
import { flushPromises } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mountWithTooltip } from './helpers/mount'
import BattlePage from '../src/pages/BattlePage.vue'
import { useAppStore } from '../src/stores/app'
import type { DesktopApi, HostSnapshot } from '../src/types'

function snapshot(autoplayActive = false): HostSnapshot {
  return {
    protocolVersion: 1,
    version: '0.6.124',
    settings: {
      persistentCheats: [],
      gameHotkeysEnabled: false,
      rewardChestBypassEnabled: false,
      catalogPoolMode: 'all',
    } as never,
    connection: { trusted: true, label: '已连接游戏', reason: '', cheatAvailable: true, autoplayActive },
    logs: [],
  }
}

describe('battle page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    window.loopstructorDesktop = {} as unknown as DesktopApi
  })

  it('persists the reward chest selection switch', async () => {
    const store = useAppStore()
    store.snapshot = snapshot()
    store.connected = true
    const save = vi.spyOn(store, 'setRewardChestBypass').mockResolvedValue(true as never)
    const wrapper = mountWithTooltip(BattlePage)
    await flushPromises()

    const toggle = wrapper.get('input[name="reward-chest-bypass"]')
    expect(toggle.attributes('checked')).toBeUndefined()
    expect((toggle.element as HTMLInputElement).checked).toBe(false)

    await toggle.setValue(true)

    expect(save).toHaveBeenCalledWith(true)
  })

  it('disables the reward chest switch while automation is running', () => {
    const store = useAppStore()
    store.snapshot = snapshot(true)
    store.connected = true

    const wrapper = mountWithTooltip(BattlePage)

    expect(wrapper.get('input[name="reward-chest-bypass"]').attributes('disabled')).toBeDefined()
  })
})
