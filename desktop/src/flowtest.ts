import type { FlowProgress, FlowProgressResponse } from './types'

export type FlowCheckStatus = 'pass' | 'fail' | 'blocked' | 'skipped'
export type FlowAutoState = 'met' | 'unmet' | 'unknown'
export type FlowStageOverall = FlowCheckStatus | 'pending'

export type FlowAutoRule =
  | { type: 'profileAvailable' }
  | { type: 'battleSave' }
  | { type: 'tutorialDeaths'; minimum: number }
  | { type: 'characters'; minimum: number }
  | { type: 'characterAny'; ids: string[] }
  | { type: 'characterLevelMax'; minimum: number }
  | { type: 'characterLevelKey'; keys: string[]; minimum: number }
  | { type: 'characterLevelEntries'; minimum: number }
  | { type: 'upgrades'; minimum: number }
  | { type: 'upgradeKey'; key: string; minimum: number }
  | { type: 'modeUnlocks' }
  | { type: 'endlessIntroduced' }
  | { type: 'leaderboardIntroduced' }

export interface FlowCheckDefinition {
  id: string
  label: string
  detail?: string
  auto?: FlowAutoRule
}

export interface FlowStageDefinition {
  id: string
  title: string
  run: string
  goal: string
  steps: string[]
  checks: FlowCheckDefinition[]
  note?: string
  docRef: string
}

export interface FlowCheckRecord {
  status?: FlowCheckStatus
  note?: string
  updatedAt?: string
}

export interface FlowTestState {
  version: 1
  runStartedAt: string
  updatedAt: string
  selectedStageId: string
  checks: Record<string, FlowCheckRecord>
}

export interface FlowAutoResult {
  state: FlowAutoState
  evidence: string
}

export interface FlowStageSummary {
  total: number
  pass: number
  fail: number
  blocked: number
  skipped: number
  pending: number
  autoMet: number
  overall: FlowStageOverall
}

export const FLOW_TEST_STORAGE_KEY = 'loopstructor.flowtest.v1'
const STATUSES: readonly FlowCheckStatus[] = ['pass', 'fail', 'blocked', 'skipped']

export function isFlowCheckStatus(value: unknown): value is FlowCheckStatus {
  return typeof value === 'string' && (STATUSES as readonly string[]).includes(value)
}

export function evaluateAutoRule(rule: FlowAutoRule, response?: FlowProgressResponse): FlowAutoResult {
  const progress = response?.available ? response.progress : undefined
  if (rule.type === 'profileAvailable') {
    if (response?.available) return { state: 'met', evidence: '流程存档已生成' }
    if (response && !response.available) {
      return { state: 'unmet', evidence: response.reason || '流程存档尚未生成' }
    }
    return { state: 'unknown', evidence: '尚未连接游戏' }
  }
  if (!progress) {
    return {
      state: 'unknown',
      evidence: response && !response.available ? (response.reason || '流程存档未读取') : '尚未连接游戏',
    }
  }

  switch (rule.type) {
    case 'battleSave':
      return {
        state: progress.hasBattleSave ? 'met' : 'unmet',
        evidence: progress.hasBattleSave ? '对局存档：已生成' : '对局存档：尚未生成',
      }
    case 'tutorialDeaths': {
      const met = progress.tutorialDeathCount >= rule.minimum
      return {
        state: met ? 'met' : 'unmet',
        evidence: `教程死亡：${progress.tutorialDeathCount} / ${rule.minimum}`,
      }
    }
    case 'characters': {
      const count = progress.unlockedCharacters?.length ?? progress.unlockedCharacterCount ?? 0
      const met = count >= rule.minimum
      return {
        state: met ? 'met' : 'unmet',
        evidence: `已解锁副官：${count} / ${rule.minimum}`,
      }
    }
    case 'characterAny': {
      const unlocked = new Map(
        (progress.unlockedCharacters ?? []).map((id) => [id.toLowerCase(), id]),
      )
      const matched = rule.ids
        .map((id) => unlocked.get(id.toLowerCase()))
        .filter((id): id is string => Boolean(id))
      return {
        state: matched.length > 0 ? 'met' : 'unmet',
        evidence: matched.length > 0
          ? `已解锁：${matched.join('、')}`
          : `未解锁：${rule.ids.join(' / ')}`,
      }
    }
    case 'characterLevelMax': {
      const max = maxLevel(progress)
      return {
        state: max >= rule.minimum ? 'met' : 'unmet',
        evidence: `最高角色等级：${max} / ${rule.minimum}`,
      }
    }
    case 'characterLevelKey': {
      const levels = rule.keys.map((key) => `${key}=${progress.characterLevels?.[key] ?? 0}`)
      const met = rule.keys.some((key) => (progress.characterLevels?.[key] ?? 0) >= rule.minimum)
      return {
        state: met ? 'met' : 'unmet',
        evidence: `角色等级：${levels.join('，')}（目标 ${rule.minimum}）`,
      }
    }
    case 'characterLevelEntries': {
      const count = Object.values(progress.characterLevels ?? {}).filter((level) => level > 0).length
      return {
        state: count >= rule.minimum ? 'met' : 'unmet',
        evidence: `已升级角色：${count} / ${rule.minimum}`,
      }
    }
    case 'upgrades': {
      const count = progress.upgradeCount ?? Object.keys(progress.upgradeLevels ?? {}).length
      return {
        state: count >= rule.minimum ? 'met' : 'unmet',
        evidence: `设施升级：${count} / ${rule.minimum} 项`,
      }
    }
    case 'upgradeKey': {
      const level = progress.upgradeLevels?.[rule.key] ?? 0
      return {
        state: level >= rule.minimum ? 'met' : 'unmet',
        evidence: `${rule.key}：${level} / ${rule.minimum}`,
      }
    }
    case 'modeUnlocks':
      return {
        state: progress.endlessAndLeaderboardUnlocked ? 'met' : 'unmet',
        evidence: progress.endlessAndLeaderboardUnlocked ? '无尽/排行榜：已解锁' : '无尽/排行榜：未解锁',
      }
    case 'endlessIntroduced':
      return {
        state: progress.endlessIntroduced ? 'met' : 'unmet',
        evidence: progress.endlessIntroduced ? '无尽介绍：已看完' : '无尽介绍：未看完',
      }
    case 'leaderboardIntroduced':
      return {
        state: progress.leaderboardIntroduced ? 'met' : 'unmet',
        evidence: progress.leaderboardIntroduced ? '排行榜介绍：已看完' : '排行榜介绍：未看完',
      }
    default:
      return { state: 'unknown', evidence: '未知的自动核对规则' }
  }
}

function maxLevel(progress: FlowProgress): number {
  const levels = Object.values(progress.characterLevels ?? {})
  return levels.length > 0 ? Math.max(...levels) : 0
}

export function effectiveCheckStatus(
  record: FlowCheckRecord | undefined,
  auto: FlowAutoResult | undefined,
): FlowCheckStatus | undefined {
  if (record?.status) return record.status
  return auto?.state === 'met' ? 'pass' : undefined
}

export function summarizeStage(
  stage: FlowStageDefinition,
  records: Record<string, FlowCheckRecord>,
  response?: FlowProgressResponse,
): FlowStageSummary {
  const summary: FlowStageSummary = {
    total: stage.checks.length,
    pass: 0,
    fail: 0,
    blocked: 0,
    skipped: 0,
    pending: 0,
    autoMet: 0,
    overall: 'pending',
  }
  for (const check of stage.checks) {
    const auto = check.auto ? evaluateAutoRule(check.auto, response) : undefined
    if (auto?.state === 'met') summary.autoMet += 1
    const status = effectiveCheckStatus(records[check.id], auto)
    if (status) summary[status] += 1
    else summary.pending += 1
  }
  if (summary.fail > 0) summary.overall = 'fail'
  else if (summary.blocked > 0) summary.overall = 'blocked'
  else if (summary.pending === 0) summary.overall = summary.skipped === summary.total ? 'skipped' : 'pass'
  else summary.overall = 'pending'
  return summary
}

export interface FlowTestSummary {
  stages: Array<{ stage: FlowStageDefinition; summary: FlowStageSummary }>
  total: number
  pass: number
  fail: number
  blocked: number
  skipped: number
  pending: number
  autoMet: number
}

export function summarizeFlowTest(
  stages: FlowStageDefinition[],
  records: Record<string, FlowCheckRecord>,
  response?: FlowProgressResponse,
): FlowTestSummary {
  const result: FlowTestSummary = {
    stages: [],
    total: 0,
    pass: 0,
    fail: 0,
    blocked: 0,
    skipped: 0,
    pending: 0,
    autoMet: 0,
  }
  for (const stage of stages) {
    const summary = summarizeStage(stage, records, response)
    result.stages.push({ stage, summary })
    result.total += summary.total
    result.pass += summary.pass
    result.fail += summary.fail
    result.blocked += summary.blocked
    result.skipped += summary.skipped
    result.pending += summary.pending
    result.autoMet += summary.autoMet
  }
  return result
}

export function statusLabel(status: FlowStageOverall | undefined): string {
  switch (status) {
    case 'pass': return '通过'
    case 'fail': return '失败'
    case 'blocked': return '阻塞'
    case 'skipped': return '跳过'
    default: return '待测'
  }
}

export function createFlowTestState(stages: FlowStageDefinition[], now = new Date()): FlowTestState {
  const timestamp = now.toISOString()
  return {
    version: 1,
    runStartedAt: timestamp,
    updatedAt: timestamp,
    selectedStageId: stages[0]?.id ?? '',
    checks: {},
  }
}

export function parseFlowTestState(raw: string | null, stages: FlowStageDefinition[]): FlowTestState {
  const fallback = createFlowTestState(stages)
  if (!raw) return fallback
  let value: unknown
  try {
    value = JSON.parse(raw)
  } catch {
    return fallback
  }
  if (!value || typeof value !== 'object') return fallback
  const candidate = value as Partial<FlowTestState>
  if (candidate.version !== 1) return fallback
  const validIds = new Set(stages.flatMap((stage) => stage.checks.map((check) => check.id)))
  const checks: Record<string, FlowCheckRecord> = {}
  if (candidate.checks && typeof candidate.checks === 'object') {
    for (const [id, record] of Object.entries(candidate.checks)) {
      if (!validIds.has(id) || !record || typeof record !== 'object') continue
      const source = record as FlowCheckRecord
      const next: FlowCheckRecord = {}
      if (isFlowCheckStatus(source.status)) next.status = source.status
      if (typeof source.note === 'string' && source.note.trim()) next.note = source.note.trim().slice(0, 2000)
      if (typeof source.updatedAt === 'string') next.updatedAt = source.updatedAt
      if (next.status || next.note) checks[id] = next
    }
  }
  const selected = stages.some((stage) => stage.id === candidate.selectedStageId)
    ? candidate.selectedStageId as string
    : fallback.selectedStageId
  return {
    version: 1,
    runStartedAt: typeof candidate.runStartedAt === 'string' ? candidate.runStartedAt : fallback.runStartedAt,
    updatedAt: typeof candidate.updatedAt === 'string' ? candidate.updatedAt : fallback.updatedAt,
    selectedStageId: selected,
    checks,
  }
}

export function loadFlowTestState(storage: Pick<Storage, 'getItem'>, stages: FlowStageDefinition[]): FlowTestState {
  try {
    return parseFlowTestState(storage.getItem(FLOW_TEST_STORAGE_KEY), stages)
  } catch {
    return createFlowTestState(stages)
  }
}

export function saveFlowTestState(storage: Pick<Storage, 'setItem'>, state: FlowTestState): boolean {
  try {
    storage.setItem(FLOW_TEST_STORAGE_KEY, JSON.stringify(state))
    return true
  } catch {
    return false
  }
}

export function buildFlowTestReport(
  stages: FlowStageDefinition[],
  state: FlowTestState,
  response: FlowProgressResponse | undefined,
  options: { generatedAt?: Date; appVersion?: string; connected?: boolean } = {},
): string {
  const now = options.generatedAt ?? new Date()
  const summary = summarizeFlowTest(stages, state.checks, response)
  const lines: string[] = [
    '# 主线流程测试报告',
    '',
    `- 生成时间：${formatDateTime(now)}`,
    `- 工具版本：${options.appVersion || '—'}`,
    `- 游戏连接：${options.connected ? '已连接' : '未连接'}`,
    `- 存档读取：${response?.available ? '成功' : (response?.reason || '未读取')}`,
    `- 测试开始：${formatDateTime(new Date(state.runStartedAt))}`,
    '',
  ]

  const progress = response?.available ? response.progress : undefined
  if (progress) {
    lines.push('## 当前存档')
    lines.push('')
    lines.push(`- 流程段：${progress.segmentId || '—'}（阶段 ${progress.phase}）`)
    lines.push(`- 教程死亡：${progress.tutorialDeathCount}`)
    lines.push(`- 已解锁副官：${progress.unlockedCharacters.length}（${progress.unlockedCharacters.join('、') || '—'}）`)
    lines.push(`- 角色等级：${formatLevels(progress.characterLevels)}`)
    lines.push(`- 设施升级：${progress.upgradeCount} 项（${formatLevels(progress.upgradeLevels)}）`)
    lines.push(`- 源液：${progress.sourceFluid}；最高完成层：${progress.lastCompletedLayers}`)
    lines.push(`- 无尽/排行榜：${progress.endlessAndLeaderboardUnlocked ? '已解锁' : '未解锁'}` +
      `；无尽介绍：${progress.endlessIntroduced ? '已看完' : '未看完'}` +
      `；排行榜介绍：${progress.leaderboardIntroduced ? '已看完' : '未看完'}`)
    lines.push('')
  }

  lines.push('## 总览')
  lines.push('')
  lines.push(`- 验收点：${summary.total}；通过 ${summary.pass}；失败 ${summary.fail}；阻塞 ${summary.blocked}；跳过 ${summary.skipped}；待测 ${summary.pending}`)
  lines.push(`- 自动核对命中：${summary.autoMet}`)
  lines.push('')

  lines.push('## 阶段结果')
  lines.push('')
  stages.forEach((stage, index) => {
    const stageSummary = summary.stages.find((item) => item.stage.id === stage.id)?.summary
    const counts = stageSummary
      ? `通过 ${stageSummary.pass}/${stageSummary.total}；失败 ${stageSummary.fail}；阻塞 ${stageSummary.blocked}；跳过 ${stageSummary.skipped}；待测 ${stageSummary.pending}`
      : '未统计'
    lines.push(`### ${index + 1}. ${stage.title}（${statusLabel(stageSummary?.overall)}）`)
    lines.push('')
    lines.push(`- 局次：${stage.run}`)
    lines.push(`- 目标：${stage.goal}`)
    lines.push(`- 统计：${counts}`)
    if (stage.note) lines.push(`- 说明：${stage.note}`)
    lines.push('')
    for (const check of stage.checks) {
      const record = state.checks[check.id]
      const auto = check.auto ? evaluateAutoRule(check.auto, response) : undefined
      const status = effectiveCheckStatus(record, auto)
      const mark = status === 'pass' ? '通过' : status === 'fail' ? '失败' : status === 'blocked' ? '阻塞' : status === 'skipped' ? '跳过' : '待测'
      const evidence = auto ? `；${auto.evidence}` : ''
      const manual = record?.status ? '（人工）' : ''
      lines.push(`- ${mark}${manual} — ${check.label}${evidence}`)
      if (record?.note) lines.push(`  - 备注：${record.note.replace(/\s+/g, ' ')}`)
    }
    lines.push('')
  })
  lines.push('## 说明')
  lines.push('')
  lines.push('- 自动核对来自当前流程存档（PlayerFlow/slot_*/profile.json），只反映存档里已落盘的状态；文案、动画和交互仍需人工确认。')
  lines.push('- 未填写备注的失败/阻塞项，建议在复测前补充现象、日志位置或截图路径。')
  lines.push('')
  return lines.join('\n')
}

export function flowTestReportFileName(now = new Date()): string {
  const stamp = `${now.getFullYear()}${String(now.getMonth() + 1).padStart(2, '0')}${String(now.getDate()).padStart(2, '0')}` +
    `-${String(now.getHours()).padStart(2, '0')}${String(now.getMinutes()).padStart(2, '0')}`
  return `主线流程测试报告-${stamp}.md`
}

export function formatDateTime(value: Date): string {
  if (Number.isNaN(value.getTime())) return '—'
  return new Intl.DateTimeFormat('zh-CN', {
    year: 'numeric', month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false,
  }).format(value)
}

export function formatLevels(levels: Record<string, number> | undefined): string {
  const entries = Object.entries(levels ?? {}).filter(([, level]) => level > 0)
  return entries.length > 0 ? entries.map(([key, level]) => `${key}=${level}`).join('、') : '—'
}
