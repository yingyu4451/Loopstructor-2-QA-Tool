<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { Play, RefreshCw, ScriptText } from '../icons'
import EmptyState from '../components/EmptyState.vue'
import { useAppStore } from '../stores/app'

type RunEventOption = { index: number; id: string; text: string; condition: string }
type RunEventItem = { id: string; title: string; options: RunEventOption[] }
type RunEventGroup = { id: string; maxTriggersPerRun: number; canEnter: boolean; events: RunEventItem[] }
type RunEventLibrary = { id: string; current: boolean; groups: RunEventGroup[] }
type RunEventCatalog = {
  inRun: boolean
  runEventActive: boolean
  panelOpen: boolean
  node: { selected: boolean; position: string; isEventNode: boolean }
  libraries: RunEventLibrary[]
  hint?: string
  unavailable?: string
}

const store = useAppStore()
const catalog = ref<RunEventCatalog>()
const libraryId = ref('')
const groupId = ref('')
const loading = ref(false)
const triggering = ref(false)

const libraries = computed(() => catalog.value?.libraries ?? [])
const library = computed(() => libraries.value.find((item) => item.id === libraryId.value))
const groups = computed(() => library.value?.groups ?? [])
const group = computed(() => groups.value.find((item) => item.id === groupId.value))
const node = computed(() => catalog.value?.node)
const blockedReason = computed(() => {
  if (!store.connected) return '先连接游戏。'
  if (store.writeLocked) return '自动游玩运行中，停止后才能触发事件。'
  const value = catalog.value
  if (!value) return '先刷新事件目录。'
  if (!value.inRun) return '当前不在对局中，先进入一局游戏并回到地图。'
  if (value.runEventActive) return '已有事件流程进行中，先走完或结束当前事件。'
  if (value.panelOpen) return '普通事件面板正被占用，先关闭当前事件面板。'
  if (!value.node.selected) return '请先在地图上选中一个新版事件节点（灰色节点）。'
  if (!value.node.isEventNode) return '选中的节点不是新版事件节点，请换一个。'
  if (!group.value) return '先选择事件组。'
  if (!group.value.canEnter) return '这个事件组本局已达到触发上限，请重开一局或换一个。'
  return ''
})
const canTrigger = computed(() => blockedReason.value === '')
const groupSummary = computed(() => {
  const value = group.value
  if (!value) return ''
  const limit = value.maxTriggersPerRun === 0 ? '不限' : value.maxTriggersPerRun
  return `${value.events.length} 个事件 · 本局上限 ${limit}`
})

function applySelection() {
  const value = catalog.value
  if (!value) return
  if (!value.libraries.some((item) => item.id === libraryId.value)) {
    libraryId.value = value.libraries.find((item) => item.current)?.id ?? value.libraries[0]?.id ?? ''
  }
  const nextGroups = value.libraries.find((item) => item.id === libraryId.value)?.groups ?? []
  if (!nextGroups.some((item) => item.id === groupId.value)) {
    groupId.value = nextGroups.find((item) => item.canEnter)?.id ?? nextGroups[0]?.id ?? ''
  }
}

async function refresh() {
  if (!store.connected) return
  loading.value = true
  try {
    const response = await store.command('cheat.queryRunEvents', {}, false)
    if (response?.success) {
      catalog.value = response.data as RunEventCatalog
      applySelection()
    }
  } finally {
    loading.value = false
  }
}

async function trigger() {
  if (!canTrigger.value || !library.value || !group.value) return
  triggering.value = true
  try {
    const response = await store.command('cheat.triggerRunEvent', {
      libraryId: library.value.id,
      groupId: group.value.id,
    })
    if (response?.success) await refresh()
  } finally {
    triggering.value = false
  }
}

watch(libraryId, () => {
  if (!groups.value.some((item) => item.id === groupId.value)) {
    groupId.value = groups.value.find((item) => item.canEnter)?.id ?? groups.value[0]?.id ?? ''
  }
})
watch(() => store.connected, (connected) => {
  if (connected) void refresh()
})
onMounted(() => {
  void refresh()
})
</script>

<template>
  <div class="page-grid">
    <section class="card mechanical-section">
      <header class="section-heading">
        <div class="heading-with-icon">
          <ScriptText :size="21" />
          <div><h2>事件</h2><p>直接开启新版白盒事件组，省去走地图。只列出当前对局已加载的事件库。</p></div>
        </div>
        <div class="action-bar">
          <button class="btn btn-outline btn-sm button secondary compact" :disabled="!store.connected || loading" @click="refresh"><RefreshCw :size="16" />刷新</button>
        </div>
      </header>
      <div class="event-status-row">
        <span class="status-badge" :class="catalog?.inRun ? 'online' : ''">对局：{{ catalog?.inRun ? '进行中' : '未进入' }}</span>
        <span class="status-badge" :class="node?.selected ? 'online' : ''">节点：{{ node?.selected ? node.position : '未选中' }}</span>
        <span class="status-badge" :class="catalog?.runEventActive ? 'warning' : ''">事件流程：{{ catalog?.runEventActive ? '进行中' : '空闲' }}</span>
        <span class="status-badge" :class="catalog?.panelOpen ? 'warning' : ''">事件面板：{{ catalog?.panelOpen ? '占用' : '空闲' }}</span>
      </div>
      <p v-if="blockedReason" class="event-hint blocked">{{ blockedReason }}</p>
      <p v-else class="event-hint ready">条件满足，可以触发。</p>
    </section>

    <section class="card mechanical-section" :class="{ mutationLocked: store.writeLocked }">
      <header class="section-heading"><div><h2>选择事件组</h2><p>触发后地图上出现该组角色，点击角色进入对话。</p></div></header>
      <div class="form-grid two-columns">
        <label>事件库
          <select v-model="libraryId" class="select select-bordered" name="event-library" :disabled="libraries.length === 0">
            <option v-for="item in libraries" :key="item.id" :value="item.id">{{ item.id }}{{ item.current ? '（当前层）' : '' }}</option>
          </select>
        </label>
        <label>事件组
          <select v-model="groupId" class="select select-bordered" name="event-group" :disabled="groups.length === 0">
            <option v-for="item in groups" :key="item.id" :value="item.id">{{ item.id }}{{ item.canEnter ? '' : '（已达上限）' }}</option>
          </select>
        </label>
      </div>
      <div v-if="group" class="event-group-meta">{{ groupSummary }}</div>
      <div class="action-bar right">
        <button class="btn btn-primary button primary" :disabled="!canTrigger || triggering" @click="trigger"><Play :size="18" />触发事件组</button>
      </div>
    </section>

    <section v-if="group" class="card mechanical-section">
      <header class="section-heading"><div><h2>事件与选项</h2><p>只读预览：触发后按这里的选项在游戏里操作。</p></div></header>
      <article v-for="runEvent in group.events" :key="runEvent.id" class="event-card">
        <div class="event-card-heading">
          <h3>{{ runEvent.title || runEvent.id }}</h3>
          <small>{{ runEvent.id }}</small>
        </div>
        <ul class="event-option-list">
          <li v-for="option in runEvent.options" :key="option.index">
            <span class="event-option-index">选项 {{ option.index }}</span>
            <span class="event-option-text">{{ option.text || option.id }}</span>
            <small v-if="option.condition">{{ option.condition }}</small>
          </li>
        </ul>
      </article>
    </section>

    <section v-if="!store.connected" class="card mechanical-section">
      <EmptyState :icon="ScriptText" title="还没有连接游戏" hint="先在「游戏与插件」页连接 Player 或 Editor，再回到这里。" />
    </section>
  </div>
</template>
