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
    version: '0.6.123',
    settings: {
      persistentCheats: [],
      gameHotkeysEnabled: false,
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

  it('opens next-level selection without claiming an unclaimed reward chest', async () => {
    const store = useAppStore()
    store.snapshot = snapshot()
    store.connected = true
    store.cheatState = { enabled: true }
    const command = vi.spyOn(store, 'command').mockResolvedValue(undefined)

    const wrapper = mountWithTooltip(BattlePage)
    await flushPromises()

    expect(wrapper.get('button[title*="临时解除奖励箱"]').text()).toContain('不领奖励，开放选关')
    expect(wrapper.text()).toContain('跳过当前奖励')

    await wrapper.get('button[title*="临时解除奖励箱"]').trigger('click')

    expect(command).toHaveBeenCalledWith('cheat.skipRewardChests')
  })

  it('disables the bypass while automation is running', () => {
    const store = useAppStore()
    store.snapshot = snapshot(true)
    store.connected = true

    const wrapper = mountWithTooltip(BattlePage)

    expect(wrapper.get('button[title*="临时解除奖励箱"]').attributes('disabled')).toBeDefined()
    expect(wrapper.get('button[title*="临时解除奖励箱"]').text()).toContain('不领奖励，开放选关')
  })
})
