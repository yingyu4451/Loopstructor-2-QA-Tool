<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { ArchiveClock, BackupRestore, FolderOpen, Pencil, RefreshCw, Save, Star, StarOutline, Trash2 } from '../icons'
import type { ManagerSettings, SaveBackupEntry, StarredSaveEntry } from '../types'
import { useAppStore } from '../stores/app'
import { useUiStore } from '../stores/ui'
import { cloneSettings as cloneManagerSettings } from '../settings-clone'

const store = useAppStore()
const ui = useUiStore()
const api = window.loopstructorDesktop
const cloneSettings = (settings: ManagerSettings): ManagerSettings => ({
  ...cloneManagerSettings(settings),
  automaticSaveBackupEnabled: settings.automaticSaveBackupEnabled ?? true,
  maximumSaveBackups: settings.maximumSaveBackups ?? 20,
})
const draft = ref(store.settings ? cloneSettings(store.settings) : undefined)
const dirty = ref(false)
const backups = computed(() => store.saveBackupCatalog?.backups ?? [])
const starred = computed(() => store.saveBackupCatalog?.starred ?? [])
const status = computed(() => store.saveBackupCatalog?.status ?? store.snapshot?.saveBackups)

watch(() => store.settings, (settings) => {
  if (settings && !dirty.value) draft.value = cloneSettings(settings)
})

onMounted(() => store.refreshSaveBackups())

function markDirty() {
  dirty.value = true
}

async function saveSettings() {
  if (!draft.value) return
  draft.value.maximumSaveBackups = Math.min(100, Math.max(1, Math.round(Number(draft.value.maximumSaveBackups) || 20)))
  const saved = await store.saveSettings(cloneSettings(draft.value))
  if (!saved) return
  dirty.value = false
  draft.value = cloneSettings(saved)
  await store.refreshSaveBackups()
}

async function openBackupDirectory() {
  await ui.run(() => api.openSaveBackups())
}

function formatDate(value: string) {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? '时间未知' : new Intl.DateTimeFormat('zh-CN', {
    year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit',
  }).format(date)
}

function formatSize(bytes: number) {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`
}

function restartHint(entry: StarredSaveEntry | SaveBackupEntry): string {
  return entry.chapter > 0 && entry.level > 0
    ? `第 ${entry.chapter} 章 · 第 ${entry.level} 关`
    : '当前存档'
}

async function restore(backup: SaveBackupEntry) {
  await store.restoreSaveBackup(backup)
}

async function starBackup(backup: SaveBackupEntry) {
  await store.starSaveBackup(backup)
}

async function starActive() {
  await store.starActiveSave()
}

async function renameStarred(entry: StarredSaveEntry) {
  await store.renameStarredSave(entry)
}

async function removeStarred(entry: StarredSaveEntry) {
  await store.deleteStarredSave(entry)
}

async function restoreStarred(entry: StarredSaveEntry) {
  await store.restoreStarredSave(entry)
}
</script>

<template>
  <div class="saves-workspace">
    <section class="card mechanical-section save-vault-controls">
      <header class="section-heading">
        <div class="heading-with-icon"><Save :size="22" /><div><h2>存档保险库</h2><p>章节或关卡变化后，等待游戏写盘稳定再保存快照。</p></div></div>
        <span class="backup-counter">{{ status?.backupCount ?? backups.length }} / {{ draft?.maximumSaveBackups ?? 20 }}</span>
      </header>
      <div v-if="draft" class="backup-controls">
        <label class="switch-row">
          <span><strong>自动备份存档</strong><small>只处理正式玩家存档</small></span>
          <input v-model="draft.automaticSaveBackupEnabled" class="checkbox checkbox-success" name="automatic-save-backup" type="checkbox" @change="markDirty" />
        </label>
        <label class="backup-limit" :class="{ disabled: !draft.automaticSaveBackupEnabled }">
          <span>最多保留</span>
          <input v-model.number="draft.maximumSaveBackups" class="input input-bordered" name="maximum-save-backups" autocomplete="off" type="number" inputmode="numeric" min="1" max="100" step="1" :disabled="!draft.automaticSaveBackupEnabled" @input="markDirty" />
          <span>个步骤存档</span>
        </label>
        <div class="backup-summary">
          <div class="backup-status">
            <span :class="{ active: draft.automaticSaveBackupEnabled }">{{ status?.busy ? '正在处理存档' : status?.pending ? '等待写入稳定' : draft.automaticSaveBackupEnabled ? '自动备份中' : '自动备份已关闭' }}</span>
            <p>{{ status?.lastMessage || '备份按章节、关卡和时间命名。' }}</p>
          </div>
          <div class="backup-actions">
            <button class="btn btn-primary btn-sm button primary compact" :disabled="!dirty" @click="saveSettings">保存设置</button>
            <button class="btn btn-outline btn-sm button secondary compact" :disabled="ui.busy || status?.busy" @click="starActive"><Star :size="16" />立即保存当前存档为星标</button>
            <button class="btn btn-outline btn-sm button secondary compact" @click="openBackupDirectory"><FolderOpen :size="16" />打开目录</button>
          </div>
        </div>
      </div>
    </section>

    <section class="card mechanical-section save-history-section">
      <header class="section-heading save-history-heading">
        <div class="heading-with-icon"><ArchiveClock :size="22" /><div><h2>保存好的存档</h2><p>选择一个快照后，Manager 会关闭游戏、恢复存档并重新启动游戏。</p></div></div>
        <button v-tooltip="'刷新存档列表'" class="btn btn-square btn-ghost btn-sm icon-button" aria-label="刷新存档列表" @click="store.refreshSaveBackups(true)"><RefreshCw :size="17" /></button>
      </header>
      <div v-if="backups.length" class="save-history-list">
        <article v-for="backup in backups" :key="backup.id" class="save-history-row">
          <div class="save-history-copy">
            <div class="save-history-title">
              <strong>第 {{ backup.chapter }} 章 · 第 {{ backup.level }} 关</strong>
              <span v-if="backup.isLatest">最新</span>
            </div>
            <p>{{ formatDate(backup.createdAt) }}</p>
            <small>{{ backup.fileCount }} 个文件 · {{ formatSize(backup.totalBytes) }}</small>
          </div>
          <div class="save-row-actions">
            <button v-tooltip="'保存为星标存档'" class="btn btn-square btn-ghost btn-sm icon-button" :aria-label="`把 ${restartHint(backup)} 的备份保存为星标存档`" :disabled="ui.busy || status?.busy" @click="starBackup(backup)"><StarOutline :size="18" /></button>
            <button class="btn btn-outline button secondary restore-save-button" :disabled="ui.busy || status?.busy" @click="restore(backup)"><BackupRestore :size="18" />读档</button>
          </div>
        </article>
      </div>
      <div v-else class="save-history-empty">
        <ArchiveClock :size="34" />
        <strong>还没有保存好的存档</strong>
        <span>进入游戏并推进到章节关卡后，这里会显示自动备份。</span>
      </div>
    </section>

    <section class="card mechanical-section starred-save-section">
      <header class="section-heading save-history-heading">
        <div class="heading-with-icon"><Star :size="22" /><div><h2>星标存档</h2><p>所有游戏版本共用、不会被自动清理，可以随时命名和读档。</p></div></div>
        <div class="backup-actions">
          <span class="starred-counter">{{ starred.length }} 个星标</span>
          <button v-tooltip="'打开星标存档目录'" class="btn btn-square btn-ghost btn-sm icon-button" aria-label="打开星标存档目录" @click="store.openStarredSaves()"><FolderOpen :size="17" /></button>
        </div>
      </header>
      <div v-if="starred.length" class="save-history-list">
        <article v-for="entry in starred" :key="entry.id" class="save-history-row starred-save-row">
          <div class="save-history-copy">
            <div class="save-history-title">
              <strong>{{ entry.name }}</strong>
              <span>{{ restartHint(entry) }}</span>
            </div>
            <p>{{ formatDate(entry.createdAt) }}</p>
            <small>{{ entry.fileCount }} 个文件 · {{ formatSize(entry.totalBytes) }} · 来源：{{ entry.sourceBackupId || '当前存档' }}</small>
          </div>
          <div class="save-row-actions">
            <button class="btn btn-outline btn-sm button secondary compact" :disabled="ui.busy || status?.busy" @click="renameStarred(entry)"><Pencil :size="16" />重命名</button>
            <button class="btn btn-outline btn-sm button danger compact" :disabled="ui.busy" @click="removeStarred(entry)"><Trash2 :size="16" />删除</button>
            <button class="btn btn-outline button secondary restore-save-button" :disabled="ui.busy || status?.busy" @click="restoreStarred(entry)"><BackupRestore :size="18" />读档</button>
          </div>
        </article>
      </div>
      <div v-else class="save-history-empty">
        <StarOutline :size="34" />
        <strong>还没有星标存档</strong>
        <span>在自动备份旁点星标，或点「立即保存当前存档为星标」，就能把存档固定保存下来。</span>
      </div>
    </section>
  </div>
</template>
