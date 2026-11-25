import { createPinia, setActivePinia } from 'pinia'
import { flushPromises } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mountWithTooltip } from './helpers/mount'
import EventsPage from '../src/pages/EventsPage.vue'
import { useAppStore } from '../src/stores/app'
import type { ControlResponse, DesktopApi, HostSnapshot } from '../src/types'

function snapshot(): HostSnapshot {
  return {
    protocolVersion: 1,
    version: '0.6.120',
    settings: {} as never,
    connection: { trusted: true, label: '已连接游戏', reason: '', cheatAvailable: true, autoplayActive: false },
    logs: [],
  }
}

function catalog(overrides: Record<string, unknown> = {}) {
  return {
    inRun: true,
    runEventActive: false,
    panelOpen: false,
    node: { selected: true, position: '3:2', isEventNode: true },
    direct: { available: true, reason: '' },
    libraries: [{
      id: 'a010_events',
      current: true,
      groups: [{
        id: 'a010_gray_market',
        maxTriggersPerRun: 1,
        canEnter: true,
        events: [{
          id: 'a010_gray_maket',
          title: '铸轨者灰市',
          options: [
            { index: 0, id: 'upgrade_l1', text: '上交 2 辆 Lv.1 战车，升级 1 辆 Lv.1 战车', condition: '需要至少 3 辆 Lv.1 战车。' },
            { index: 1, id: 'leave', text: '离开', condition: '' },
          ],
        }],
      }],
    }],
    ...overrides,
  }
}

function response(data: unknown): ControlResponse {
  return { success: true, message: 'ok', data: data as Record<string, any> }
}

describe('event trigger page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    window.loopstructorDesktop = {} as unknown as DesktopApi
  })

  it('lists libraries, groups, events, and option text', async () => {
    const store = useAppStore()
    store.connected = true
    store.snapshot = snapshot()
    const command = vi.spyOn(store, 'command').mockResolvedValue(response(catalog()))

    const wrapper = mountWithTooltip(EventsPage)
    await flushPromises()

    expect(command).toHaveBeenCalledWith('cheat.queryRunEvents', {}, false)
    expect(wrapper.get('[name="event-library"]').text()).toContain('a010_events')
    expect(wrapper.get('[name="event-group"]').text()).toContain('a010_gray_market')
    expect(wrapper.get('.event-card').text()).toContain('铸轨者灰市')
    expect(wrapper.get('.event-option-list').text()).toContain('上交 2 辆 Lv.1 战车')
    expect(wrapper.get('.event-option-list').text()).toContain('需要至少 3 辆 Lv.1 战车。')
    expect(wrapper.get('.event-hint').text()).toContain('已选中事件节点')
  })

  it('triggers the selected group through the cheat command', async () => {
    const store = useAppStore()
    store.connected = true
    store.snapshot = snapshot()
    const command = vi.spyOn(store, 'command').mockImplementation(async (name) =>
      name === 'cheat.queryRunEvents'
        ? response(catalog())
        : response({ libraryId: 'a010_events', groupId: 'a010_gray_market', eventCount: 1, state: 'Interactive' }))

    const wrapper = mountWithTooltip(EventsPage)
    await flushPromises()
    await wrapper.get('.trigger-manual').trigger('click')
    await flushPromises()

    expect(command).toHaveBeenCalledWith('cheat.triggerRunEvent', {
      libraryId: 'a010_events',
      groupId: 'a010_gray_market',
    })
    expect(command).toHaveBeenCalledTimes(3)
  })

  it('triggers directly without selecting any map node', async () => {
    const store = useAppStore()
    store.connected = true
    store.snapshot = snapshot()
    const directCatalog = catalog({ node: { selected: false, position: '', isEventNode: false } })
    const command = vi.spyOn(store, 'command').mockImplementation(async (name) =>
      name === 'cheat.queryRunEvents'
        ? response(directCatalog)
        : response({ libraryId: 'a010_events', groupId: 'a010_gray_market', direct: true }))

    const wrapper = mountWithTooltip(EventsPage)
    await flushPromises()

    expect(wrapper.get('.event-hint').text()).toContain('不依赖地图节点')
    expect(wrapper.get('.trigger-manual').attributes('disabled')).toBeDefined()
    const direct = wrapper.get('.trigger-direct')
    expect(direct.attributes('disabled')).toBeUndefined()
    await direct.trigger('click')
    await flushPromises()

    expect(command).toHaveBeenCalledWith('cheat.triggerRunEvent', {
      libraryId: 'a010_events',
      groupId: 'a010_gray_market',
      direct: true,
    })
  })

  it('explains why direct triggering is unavailable', async () => {
    const store = useAppStore()
    store.connected = true
    store.snapshot = snapshot()
    vi.spyOn(store, 'command').mockResolvedValue(response(catalog({
      node: { selected: false, position: '', isEventNode: false },
      direct: { available: false, reason: '当前游戏版本缺少事件沙盒需要的收尾入口。' },
    })))

    const wrapper = mountWithTooltip(EventsPage)
    await flushPromises()

    expect(wrapper.get('.event-hint').text()).toContain('事件沙盒')
    expect(wrapper.get('.trigger-direct').attributes('disabled')).toBeDefined()
    expect(wrapper.get('.trigger-manual').attributes('disabled')).toBeDefined()
  })

  it('marks an exhausted group and keeps the trigger disabled', async () => {
    const store = useAppStore()
    store.connected = true
    store.snapshot = snapshot()
    const exhausted = catalog()
    exhausted.libraries[0].groups[0].canEnter = false
    vi.spyOn(store, 'command').mockResolvedValue(response(exhausted))

    const wrapper = mountWithTooltip(EventsPage)
    await flushPromises()

    expect(wrapper.get('[name="event-group"]').text()).toContain('已达上限')
    expect(wrapper.get('.event-hint').text()).toContain('触发上限')
    expect(wrapper.get('.trigger-direct').attributes('disabled')).toBeDefined()
expect(wrapper.get('.trigger-manual').attributes('disabled')).toBeDefined()
  })
})
