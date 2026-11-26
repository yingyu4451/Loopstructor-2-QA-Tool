import { createPinia, setActivePinia } from 'pinia'
import { flushPromises } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import FlowTestPage from '../src/pages/FlowTestPage.vue'
import { FLOW_STAGES } from '../src/flowtest-data'
import {
  buildFlowTestReport, createFlowTestState, evaluateAutoRule, flowTestReportFileName,
  loadFlowTestState, parseFlowTestState, saveFlowTestState, summarizeFlowTest,
} from '../src/flowtest'
import { useAppStore } from '../src/stores/app'
import type { DesktopApi, FlowProgressResponse, HostSnapshot } from '../src/types'
import { mountWithTooltip } from './helpers/mount'

function snapshot(): HostSnapshot {
  return {
    protocolVersion: 1,
    version: '0.6.121',
    settings: {} as never,
    connection: { trusted: true, label: '已连接游戏', reason: '', cheatAvailable: true, autoplayActive: false },
    logs: [],
  }
}

function flowResponse(overrides: Partial<FlowProgressResponse> = {}): FlowProgressResponse {
  return {
    available: true,
    saveRoot: 'C:\\saves\\PlayerFlow\\slot_demo-v1',
    profilePath: 'C:\\saves\\PlayerFlow\\slot_demo-v1\\profile.json',
    lastWriteUtc: '2026-11-25T08:00:00.000Z',
    reason: '',
    progress: {
      revision: 12,
      profileId: 'demo',
      segmentId: 'demo_free',
      phase: 2,
      tutorialDeathCount: 1,
      hasBattleSave: true,
      sourceFluid: 46,
      lastCompletedLayers: 23,
      unlockedVehicleCount: 3,
      unlockedCharacters: ['Character_Seraphina', 'Character_Vera', 'Character_Greta'],
      unlockedCharacterCount: 3,
      characterLevels: { Character_Seraphina: 2 },
      upgradeLevels: { character_1_node_choices: 1 },
      upgradeCount: 1,
      upgradeLevelSum: 1,
      modeUnlockRecordPresent: false,
      modeUnlockRecordValid: false,
      endlessAndLeaderboardUnlocked: false,
      endlessIntroduced: false,
      leaderboardIntroduced: false,
      modeUnlockCharacterId: '',
    },
    ...overrides,
  }
}

describe('flow test model', () => {
  it('evaluates save-backed checks and leaves disconnected checks unknown', () => {
    const response = flowResponse()

    expect(evaluateAutoRule({ type: 'characters', minimum: 3 }, response)).toMatchObject({ state: 'met' })
    expect(evaluateAutoRule({ type: 'characters', minimum: 4 }, response)).toMatchObject({ state: 'unmet' })
    expect(evaluateAutoRule({ type: 'tutorialDeaths', minimum: 1 }, response)).toMatchObject({ state: 'met' })
    expect(evaluateAutoRule({ type: 'endlessIntroduced' }, response)).toMatchObject({ state: 'unmet' })
    expect(evaluateAutoRule({ type: 'characters', minimum: 1 }, flowResponse({ available: false, reason: '未连接' })))
      .toMatchObject({ state: 'unknown' })
  })

  it('matches lieutenants by stable character ids across save layouts', () => {
    const response = flowResponse()
    const withVera = flowResponse({
      progress: {
        ...flowResponse().progress!,
        unlockedCharacters: ['Character_Seraphina', 'Character_Vera'],
      },
    })

    expect(evaluateAutoRule({ type: 'characterAny', ids: ['character_2', 'Character_Vera'] }, withVera))
      .toMatchObject({ state: 'met' })
    expect(evaluateAutoRule({ type: 'characterAny', ids: ['character_3', 'Character_Greta', 'Character_Elaina'] }, withVera))
      .toMatchObject({ state: 'unmet' })
    expect(evaluateAutoRule({ type: 'characterAny', ids: ['character_3', 'Character_Greta', 'Character_Elaina'] }, flowResponse({
      progress: {
        ...flowResponse().progress!,
        unlockedCharacters: ['Character_Seraphina', 'Character_Elaina'],
      },
    }))).toMatchObject({ state: 'met' })
    expect(evaluateAutoRule({ type: 'characterAny', ids: ['charACTER_seraphina'] }, response))
      .toMatchObject({ state: 'met' })
  })

  it('counts manual and automatic statuses separately', () => {
    const stage = FLOW_STAGES.find((item) => item.id === 'base-initial')!
    const records = {
      [stage.checks[0].id]: { status: 'fail' as const, note: '副官没有感叹号' },
      [stage.checks[1].id]: { status: 'pass' as const },
    }
    const summary = summarizeFlowTest(FLOW_STAGES, records, flowResponse())

    const baseSummary = summary.stages.find((item) => item.stage.id === stage.id)!.summary
    expect(baseSummary.fail).toBe(1)
    expect(baseSummary.pass).toBe(2)
    expect(baseSummary.autoMet).toBe(1)
    expect(baseSummary.overall).toBe('fail')
    expect(summary.fail).toBe(1)
  })

  it('round-trips state through storage and drops unknown checks', () => {
    const storage = new Map<string, string>()
    const fake = {
      getItem: (key: string) => storage.get(key) ?? null,
      setItem: (key: string, value: string) => { storage.set(key, value) },
    }
    const state = createFlowTestState(FLOW_STAGES)
    state.checks['s01-start-menu'] = { status: 'blocked', note: '登录界面卡住' }
    state.checks['unknown-check'] = { status: 'pass' }
    expect(saveFlowTestState(fake, state)).toBe(true)

    const loaded = loadFlowTestState(fake, FLOW_STAGES)
    expect(loaded.checks['s01-start-menu']).toMatchObject({ status: 'blocked', note: '登录界面卡住' })
    expect(loaded.checks['unknown-check']).toBeUndefined()
    expect(parseFlowTestState('{bad json', FLOW_STAGES).checks).toEqual({})
  })

  it('writes a markdown report with statuses, notes, and the save snapshot', () => {
    const state = createFlowTestState(FLOW_STAGES)
    state.checks['s01-start-menu'] = { status: 'fail', note: 'Steam 名称没有带出来' }
    const report = buildFlowTestReport(FLOW_STAGES, state, flowResponse(), {
      appVersion: '0.6.121',
      connected: true,
      generatedAt: new Date('2026-11-25T09:30:00+08:00'),
    })

    expect(report).toContain('# 主线流程测试报告')
    expect(report).toContain('工具版本：0.6.121')
    expect(report).toContain('流程段：demo_free')
    expect(report).toContain('已解锁副官：3')
    expect(report).toContain('失败（人工） — 登录界面与开始游戏入口正常')
    expect(report).toContain('备注：Steam 名称没有带出来')
    expect(flowTestReportFileName(new Date('2026-11-25T09:30:00+08:00')))
      .toMatch(/^主线流程测试报告-\d{8}-\d{4}\.md$/)
  })
})

describe('flow test page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    window.localStorage.clear()
  })

  it('shows auto-verified checks and persists a manual failure', async () => {
    const readFlowProgress = vi.fn(async () => flowResponse())
    window.loopstructorDesktop = { readFlowProgress } as unknown as DesktopApi
    const store = useAppStore()
    store.connected = true
    store.snapshot = snapshot()

    const wrapper = mountWithTooltip(FlowTestPage)
    await flushPromises()

    expect(readFlowProgress).toHaveBeenCalledOnce()
    expect(wrapper.get('.flowtest-progress-meta').text()).toContain('已完成')
    expect(wrapper.get('[data-check-id="s01-save"]').text()).toContain('存档核对：已核对')

    await wrapper.get('[data-check-id="s01-start-menu"] .flowtest-status-button.fail').trigger('click')
    const saved = JSON.parse(window.localStorage.getItem('loopstructor.flowtest.v1') ?? '{}')
    expect(saved.checks['s01-start-menu'].status).toBe('fail')
  })

  it('keeps the checklist usable while disconnected', async () => {
    window.loopstructorDesktop = {} as unknown as DesktopApi
    const store = useAppStore()
    store.connected = false

    const wrapper = mountWithTooltip(FlowTestPage)
    await flushPromises()

    expect(wrapper.get('.flowtest-save-note').text()).toContain('连接游戏后可自动核对存档进度')
    expect(wrapper.get('.flowtest-stage-item').text()).toContain('启动与创建流程档')
    expect(wrapper.findAll('.flowtest-check').length).toBeGreaterThan(0)
  })
})
