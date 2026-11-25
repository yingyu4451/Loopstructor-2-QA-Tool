<script setup lang="ts">
import { computed, nextTick, onMounted, reactive, ref, watch } from 'vue'
import {
  CheckCircle2, Download, Info, ListChecks, RefreshCw, RouteIcon, TriangleAlert,
} from '../icons'
import { useAppStore } from '../stores/app'
import { useUiStore } from '../stores/ui'
import { FLOW_STAGES } from '../flowtest-data'
import {
  buildFlowTestReport, createFlowTestState, effectiveCheckStatus, evaluateAutoRule,
  flowTestReportFileName, formatDateTime, loadFlowTestState, saveFlowTestState, statusLabel,
  summarizeFlowTest,
  type FlowAutoResult, type FlowCheckDefinition, type FlowCheckStatus, type FlowStageDefinition,
  type FlowStageOverall, type FlowStageSummary, type FlowTestState,
} from '../flowtest'

const store = useAppStore()
const ui = useUiStore()
const state = reactive<FlowTestState>(loadFlowTestState(window.localStorage, FLOW_STAGES))
const selectedStageId = ref(state.selectedStageId || FLOW_STAGES[0]?.id || '')
const refreshing = ref(false)
const exporting = ref(false)
const lastReadLabel = ref('')
const detailRef = ref<HTMLElement>()

const statusOptions: Array<{ value: FlowCheckStatus; label: string }> = [
  { value: 'pass', label: '通过' },
  { value: 'fail', label: '失败' },
  { value: 'blocked', label: '阻塞' },
  { value: 'skipped', label: '跳过' },
]

const selectedStage = computed(() => FLOW_STAGES.find((stage) => stage.id === selectedStageId.value) ?? FLOW_STAGES[0])
const summary = computed(() => summarizeFlowTest(FLOW_STAGES, state.checks, store.flowProgress))
const stageSummaries = computed(() => new Map(summary.value.stages.map((item) => [item.stage.id, item.summary])))
const selectedStageSummary = computed<FlowStageSummary>(() => stageSummaries.value.get(selectedStage.value.id) ?? emptySummary())
const progress = computed(() => store.flowProgress?.available ? store.flowProgress.progress : undefined)
const progressReason = computed(() => {
  if (store.flowProgress?.available) return ''
  if (store.flowProgress?.reason) return store.flowProgress.reason
  return store.connected ? '尚未读取流程存档；点击「刷新存档」后再核对。' : '连接游戏后可自动核对存档进度。'
})
const profileWriteLabel = computed(() => {
  const value = store.flowProgress?.lastWriteUtc
  if (!value) return ''
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? '' : formatDateTime(date)
})
const completionPercent = computed(() => summary.value.total > 0
  ? Math.round((summary.value.pass / summary.value.total) * 100)
  : 0)
const runStatus = computed<FlowStageOverall>(() => {
  if (summary.value.fail > 0) return 'fail'
  if (summary.value.blocked > 0) return 'blocked'
  return summary.value.pending === 0 ? 'pass' : 'pending'
})
const saveStats = computed(() => {
  const value = progress.value
  return [
    { label: '连接', value: store.connected ? '已连接' : '未连接' },
    { label: '流程段', value: value?.segmentId || '—' },
    { label: '教程死亡', value: value ? String(value.tutorialDeathCount) : '—' },
    { label: '副官', value: value ? `${value.unlockedCharacters.length} 名` : '—' },
    { label: '设施升级', value: value ? `${value.upgradeCount} 项` : '—' },
    { label: '源液', value: value ? String(value.sourceFluid) : '—' },
    { label: '最高层', value: value ? String(value.lastCompletedLayers) : '—' },
    { label: '无尽/排行榜', value: value ? (value.endlessAndLeaderboardUnlocked ? '已解锁' : '未解锁') : '—' },
  ]
})
const autoResults = computed<Record<string, FlowAutoResult>>(() => {
  const result: Record<string, FlowAutoResult> = {}
  for (const stage of FLOW_STAGES) {
    for (const check of stage.checks) {
      if (check.auto) result[check.id] = evaluateAutoRule(check.auto, store.flowProgress)
    }
  }
  return result
})
const hasStageRecords = computed(() => selectedStage.value.checks.some((check) => Boolean(state.checks[check.id])))

function emptySummary(): FlowStageSummary {
  return {
    total: 0, pass: 0, fail: 0, blocked: 0, skipped: 0, pending: 0, autoMet: 0, overall: 'pending',
  }
}

function stageSummary(stage: FlowStageDefinition): FlowStageSummary {
  return stageSummaries.value.get(stage.id) ?? emptySummary()
}

function autoFor(check: FlowCheckDefinition): FlowAutoResult | undefined {
  return check.auto ? autoResults.value[check.id] : undefined
}

function checkStatus(check: FlowCheckDefinition): FlowCheckStatus | undefined {
  return effectiveCheckStatus(state.checks[check.id], autoFor(check))
}

function checkStatusClass(check: FlowCheckDefinition): string {
  return checkStatus(check) ?? 'pending'
}

function autoLabel(value: FlowAutoResult['state']): string {
  return value === 'met' ? '已核对' : value === 'unmet' ? '未满足' : '未读取'
}

function autoShortLabel(check: FlowCheckDefinition): string {
  const auto = autoFor(check)
  return auto ? autoLabel(auto.state) : ''
}

function markSymbol(status: FlowCheckStatus | undefined): string {
  switch (status) {
    case 'pass': return '✓'
    case 'fail': return '×'
    case 'blocked': return '!'
    case 'skipped': return '–'
    default: return '·'
  }
}

function markTitle(status: FlowCheckStatus | undefined): string {
  return status ? statusLabel(status) : '待测'
}

function statusClass(status: FlowStageOverall | undefined): string {
  return status ?? 'pending'
}

function persist(): void {
  state.updatedAt = new Date().toISOString()
  state.selectedStageId = selectedStageId.value
  saveFlowTestState(window.localStorage, state)
}

function setStatus(check: FlowCheckDefinition, status: FlowCheckStatus): void {
  const current = state.checks[check.id]
  const updatedAt = new Date().toISOString()
  if (current?.status === status) {
    if (current.note) state.checks[check.id] = { note: current.note, updatedAt }
    else delete state.checks[check.id]
  } else {
    state.checks[check.id] = { ...(current ?? {}), status, updatedAt }
  }
  persist()
}

function setNote(check: FlowCheckDefinition, value: string): void {
  const note = value.slice(0, 2000)
  const current = state.checks[check.id] ?? {}
  const updatedAt = new Date().toISOString()
  if (note.trim()) state.checks[check.id] = { ...current, note, updatedAt }
  else if (current.status) state.checks[check.id] = { status: current.status, updatedAt }
  else delete state.checks[check.id]
  persist()
}

function selectStageByValue(stage: FlowStageDefinition): void {
  selectedStageId.value = stage.id
  state.selectedStageId = stage.id
  persist()
  if (window.innerWidth < 1180) {
    void nextTick(() => detailRef.value?.scrollIntoView({
      behavior: prefersReducedMotion() ? 'auto' : 'smooth',
      block: 'start',
    }))
  }
}

async function refreshProgress(announce = true): Promise<void> {
  if (!store.connected) {
    if (announce) ui.toast('先连接游戏，再读取流程存档。', 'warning')
    return
  }
  refreshing.value = true
  try {
    await store.refreshFlowProgress(announce)
    lastReadLabel.value = formatDateTime(new Date())
  } finally {
    refreshing.value = false
  }
}

async function startNewRun(): Promise<void> {
  const confirmed = await ui.confirm({
    title: '开始新一轮流程测试',
    message: '清空当前所有验收状态与备注，从第一段重新开始。存档进度本身不会被修改。',
    confirmText: '开始新一轮',
    danger: true,
  })
  if (!confirmed) return
  const next = createFlowTestState(FLOW_STAGES)
  state.runStartedAt = next.runStartedAt
  state.updatedAt = next.updatedAt
  state.checks = {}
  selectedStageId.value = next.selectedStageId
  state.selectedStageId = next.selectedStageId
  persist()
  ui.toast('已开始新一轮流程测试。', 'success')
}

async function clearStage(): Promise<void> {
  const stage = selectedStage.value
  const ids = stage.checks.map((check) => check.id).filter((id) => Boolean(state.checks[id]))
  if (ids.length === 0) return
  const confirmed = await ui.confirm({
    title: '清除本阶段记录',
    message: `清除「${stage.title}」的验收状态和备注？其他阶段不受影响。`,
    confirmText: '清除',
    danger: true,
  })
  if (!confirmed) return
  for (const id of ids) delete state.checks[id]
  persist()
  ui.toast('已清除本阶段记录。', 'info')
}

function exportReport(): void {
  exporting.value = true
  try {
    const report = buildFlowTestReport(FLOW_STAGES, state, store.flowProgress, {
      appVersion: __APP_VERSION__,
      connected: store.connected,
    })
    const blob = new window.Blob([report], { type: 'text/markdown;charset=utf-8' })
    const url = window.URL.createObjectURL(blob)
    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = flowTestReportFileName()
    anchor.click()
    window.setTimeout(() => window.URL.revokeObjectURL(url), 1000)
    ui.toast('流程测试报告已导出。', 'success')
  } finally {
    exporting.value = false
  }
}

function prefersReducedMotion(): boolean {
  return window.matchMedia?.('(prefers-reduced-motion: reduce)').matches === true
}

watch(() => state.checks, () => persist(), { deep: true })
watch(selectedStageId, () => persist())
watch(() => store.connected, (connected) => {
  if (connected) void refreshProgress(false)
})

onMounted(() => {
  if (store.connected) void refreshProgress(false)
})
</script>

<template>
  <div class="flowtest-page">
    <section class="card mechanical-section flowtest-overview">
      <header class="flowtest-overview-head">
        <div class="heading-with-icon">
          <ListChecks :size="22" />
          <div>
            <h1 class="flowtest-title">主线流程</h1>
            <p class="flowtest-subtitle">按文档逐段验收；能自动核对的项直接读当前流程存档，其余人工确认。</p>
          </div>
        </div>
        <div class="flowtest-overview-actions">
          <button class="btn btn-outline btn-sm button secondary compact" :disabled="refreshing || !store.connected" @click="refreshProgress(true)">
            <RefreshCw :size="15" />{{ refreshing ? '读取中…' : '刷新存档' }}
          </button>
          <button class="btn btn-outline btn-sm button secondary compact" :disabled="exporting" @click="exportReport">
            <Download :size="15" />导出报告
          </button>
          <button class="btn btn-outline btn-sm button danger compact" @click="startNewRun">开始新一轮</button>
        </div>
      </header>

      <div class="flowtest-progress">
        <div class="flowtest-progress-meta">
          <strong>已完成 {{ summary.pass }} / {{ summary.total }} 个验收点</strong>
          <span>
            通过 {{ summary.pass }} · 失败 {{ summary.fail }} · 阻塞 {{ summary.blocked }} · 跳过 {{ summary.skipped }} · 待测 {{ summary.pending }}
            · 自动核对命中 {{ summary.autoMet }}
          </span>
        </div>
        <div
          class="flowtest-progress-track"
          role="progressbar"
          :aria-valuenow="completionPercent"
          aria-valuemin="0"
          aria-valuemax="100"
          :aria-label="`主线流程完成 ${completionPercent}%`"
        >
          <span class="flowtest-progress-fill" :class="runStatus" :style="{ width: `${completionPercent}%` }" />
        </div>
      </div>

      <div class="flowtest-save-strip">
        <div v-for="stat in saveStats" :key="stat.label">
          <span>{{ stat.label }}</span>
          <strong>{{ stat.value }}</strong>
        </div>
      </div>
      <p v-if="progressReason" class="flowtest-save-note">
        <TriangleAlert :size="13" />{{ progressReason }}
      </p>
      <p v-else class="flowtest-save-note ok">
        <CheckCircle2 :size="13" />
        存档已读取{{ profileWriteLabel ? `（存档写入 ${profileWriteLabel}）` : '' }}{{ lastReadLabel ? ` · 上次刷新 ${lastReadLabel}` : '' }}
      </p>
    </section>

    <div class="flowtest-workspace">
      <nav class="card mechanical-section flowtest-stages" aria-label="流程阶段">
        <header class="flowtest-stages-head">
          <div class="heading-with-icon"><RouteIcon :size="18" /><h2>阶段</h2></div>
          <span>{{ FLOW_STAGES.length }} 段</span>
        </header>
        <div class="flowtest-stage-scroll">
          <button
            v-for="(stage, index) in FLOW_STAGES"
            :key="stage.id"
            class="flowtest-stage-item"
            :class="[stageSummary(stage).overall, { active: stage.id === selectedStage.id }]"
            :aria-current="stage.id === selectedStage.id ? 'step' : undefined"
            :title="`${stage.title} · ${statusLabel(stageSummary(stage).overall)} · ${stageSummary(stage).pass}/${stageSummary(stage).total}`"
            @click="selectStageByValue(stage)"
          >
            <span class="flowtest-stage-index">{{ String(index + 1).padStart(2, '0') }}</span>
            <span class="flowtest-stage-copy">
              <strong>{{ stage.title }}</strong>
              <small>{{ stage.run }} · {{ stageSummary(stage).pass }}/{{ stageSummary(stage).total }}</small>
            </span>
            <span class="flowtest-stage-state">{{ statusLabel(stageSummary(stage).overall) }}</span>
          </button>
        </div>
      </nav>

      <div ref="detailRef" class="flowtest-detail-scroll">
        <section class="card mechanical-section flowtest-detail">
          <header class="flowtest-detail-head">
            <div>
              <span class="flowtest-stage-tag">{{ selectedStage.run }}</span>
              <h2>{{ selectedStage.title }}</h2>
              <p>{{ selectedStage.goal }}</p>
            </div>
            <span class="flowtest-stage-score" :class="statusClass(selectedStageSummary.overall)">
              {{ statusLabel(selectedStageSummary.overall) }} · {{ selectedStageSummary.pass }}/{{ selectedStageSummary.total }}
            </span>
          </header>
          <p class="flowtest-doc-ref"><Info :size="13" />{{ selectedStage.docRef }}</p>

          <section class="flowtest-section">
            <h3>操作步骤</h3>
            <ol class="flowtest-steps">
              <li v-for="(step, index) in selectedStage.steps" :key="index">{{ step }}</li>
            </ol>
          </section>

          <section class="flowtest-section">
            <h3>验收点 <small>自动核对命中 {{ selectedStageSummary.autoMet }} 项</small></h3>
            <article
              v-for="check in selectedStage.checks"
              :key="check.id"
              class="flowtest-check"
              :class="checkStatusClass(check)"
              :data-check-id="check.id"
            >
              <div class="flowtest-check-head">
                <span class="flowtest-check-mark" :class="statusClass(checkStatus(check))" :title="markTitle(checkStatus(check))" :aria-label="markTitle(checkStatus(check))">
                  {{ markSymbol(checkStatus(check)) }}
                </span>
                <div class="flowtest-check-copy">
                  <strong>{{ check.label }}</strong>
                  <small v-if="check.detail">{{ check.detail }}</small>
                  <span
                    class="flowtest-auto"
                    :class="autoFor(check)?.state ?? 'manual'"
                    :title="autoFor(check)?.evidence ?? '人工确认'"
                  >{{ check.auto ? `存档核对：${autoShortLabel(check)}` : '人工确认' }}</span>
                  <small v-if="autoFor(check)" class="flowtest-auto-detail">{{ autoFor(check)?.evidence }}</small>
                </div>
                <div class="flowtest-check-actions">
                  <button
                    v-for="option in statusOptions"
                    :key="option.value"
                    class="flowtest-status-button"
                    :class="[option.value, { active: state.checks[check.id]?.status === option.value }]"
                    :aria-pressed="state.checks[check.id]?.status === option.value"
                    :title="state.checks[check.id]?.status === option.value ? '再次点击清除状态' : `标记为${option.label}`"
                    @click="setStatus(check, option.value)"
                  >{{ option.label }}</button>
                </div>
              </div>
              <input
                class="flowtest-note"
                :name="`flow-note-${check.id}`"
                :value="state.checks[check.id]?.note ?? ''"
                :aria-label="`${check.label}的备注`"
                maxlength="2000"
                placeholder="备注（失败或阻塞时写清现象、日志位置）"
                @input="setNote(check, ($event.target as HTMLInputElement).value)"
              />
            </article>
          </section>

          <p v-if="selectedStage.note" class="flowtest-stage-note">{{ selectedStage.note }}</p>

          <div class="flowtest-detail-actions">
            <button class="btn btn-outline btn-sm button secondary compact" :disabled="!hasStageRecords" @click="clearStage">清除本阶段记录</button>
          </div>
        </section>
      </div>
    </div>
  </div>
</template>
