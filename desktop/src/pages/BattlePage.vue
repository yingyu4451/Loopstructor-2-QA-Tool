<script setup lang="ts">

type CheckboxInput = { checked: boolean }
import { computed } from 'vue'
import { Eye, FastForward, Gift, Keyboard, Map, Shield, Skull, Sparkles } from '../icons'
import { useAppStore } from '../stores/app'
import type { PersistentCheatOption } from '../types'
const store = useAppStore()

const persistentOptions = [
  { key: 'baseGodMode', label: '基地无敌', description: '自动阻止基地受到伤害', icon: Shield },
  { key: 'mapSkipEnabled', label: '地图自由跳转', description: '自动允许选择后续地图节点', icon: Map },
  { key: 'enemyIdsVisible', label: '显示敌人 ID', description: '自动显示怪物运行时 ID', icon: Eye },
  { key: 'enemyBuffsVisible', label: '显示 Buff', description: '自动显示 Buff 图标与持续时间', icon: Sparkles },
] as const
const persistent = computed(() => new Set<PersistentCheatOption>((store.settings?.persistentCheats ?? []) as PersistentCheatOption[]))
const persistentSavedCount = computed(() => persistentOptions.filter((option) => persistent.value.has(option.key)).length)
const state = computed(() => store.cheatState ?? {})
const hotkeysEnabled = computed(() => store.snapshot?.settings.gameHotkeysEnabled === true)
const hotkeysLive = computed(() => store.cheatState?.hotkeysEnabled === true)
const hotkeyStateLabel = computed(() => {
  if (!store.cheatEnabled) return '等待作弊连接'
  if (hotkeysLive.value) return '游戏中已生效'
  return hotkeysEnabled.value ? '正在下发到游戏' : '未启用'
})
function liveCheatActive(key: PersistentCheatOption): boolean {
  if (key === 'baseGodMode') return state.value.baseGodMode === true
  if (key === 'mapSkipEnabled') return state.value.mapSkipEnabled === true
  if (key === 'enemyIdsVisible') return state.value.enemyIdsVisible === true
  return state.value.enemyBuffsVisible === true
}
const persistentLiveCount = computed(() => persistentOptions.filter((option) => persistent.value.has(option.key) && liveCheatActive(option.key)).length)
const persistentStateLabel = computed(() => {
  if (!store.cheatEnabled) return '等待游戏连接'
  if (persistentSavedCount.value === 0) return '未启用'
  return persistentLiveCount.value === persistentSavedCount.value ? '游戏中已恢复' : '正在恢复'
})
const persistentStateClass = computed(() => {
  if (!store.cheatEnabled || persistentSavedCount.value === 0) return ''
  return persistentLiveCount.value === persistentSavedCount.value ? 'online' : 'warning'
})
async function toggle(command: string, value: boolean, input: CheckboxInput) {
  const response = await store.command(command, { enabled: value })
  if (response?.success) await store.refreshState()
  else input.checked = !value
}
async function toggleHotkeys(value: boolean, input: CheckboxInput) {
  const saved = await store.setGameHotkeys(value)
  if (!saved) input.checked = !value
}
async function togglePersistent(option: PersistentCheatOption, value: boolean, input: CheckboxInput) {
  const saved = await store.setPersistentCheat(option, value)
  if (!saved) input.checked = !value
  else if (store.connected) await store.refreshState(false)
}
</script>

<template>
  <div class="battle-workspace">
    <section class="card battle-section mechanical-section" :class="{ mutationLocked: store.writeLocked }">
      <header class="section-heading"><div><h2>战场状态</h2><p>持久开关会标记当前游戏使用过作弊。</p></div></header>
      <div class="battle-action-grid">
        <label class="action-toggle"><Shield :size="19" /><span><strong>基地无敌</strong><small>阻止基地受到伤害</small></span><input class="checkbox checkbox-success" name="base-god-mode" type="checkbox" :checked="state.baseGodMode" :disabled="store.writeLocked" @change="toggle('cheat.setBaseGodMode', ($event.target as HTMLInputElement).checked, $event.target as HTMLInputElement)" /></label>
        <label class="action-toggle"><Map :size="19" /><span><strong>地图自由跳转</strong><small>允许选择后续地图节点</small></span><input class="checkbox checkbox-success" name="map-skip-enabled" type="checkbox" :checked="state.mapSkipEnabled" :disabled="store.writeLocked" @change="toggle('cheat.setMapSkipEnabled', ($event.target as HTMLInputElement).checked, $event.target as HTMLInputElement)" /></label>
      </div>
    </section>
    <section class="card battle-section mechanical-section">
      <header class="section-heading"><div><h2>敌人信息</h2><p>只读覆盖在自动游玩期间仍可使用。</p></div></header>
      <div class="battle-action-grid">
        <label class="action-toggle"><Eye :size="19" /><span><strong>显示敌人 ID</strong><small>在怪物旁显示运行时 ID</small></span><input class="checkbox checkbox-success" name="enemy-id-overlay" type="checkbox" :checked="state.enemyIdsVisible" @change="toggle('cheat.setEnemyIdOverlay', ($event.target as HTMLInputElement).checked, $event.target as HTMLInputElement)" /></label>
        <label class="action-toggle"><Sparkles :size="19" /><span><strong>显示 Buff</strong><small>显示 Buff 图标与持续时间</small></span><input class="checkbox checkbox-success" name="enemy-buff-overlay" type="checkbox" :checked="state.enemyBuffsVisible" @change="toggle('cheat.setEnemyBuffOverlay', ($event.target as HTMLInputElement).checked, $event.target as HTMLInputElement)" /></label>
      </div>
    </section>
    <section class="card battle-section mechanical-section" :class="{ mutationLocked: store.writeLocked }">
      <header class="section-heading"><div><h2>波次与阻断恢复</h2><p>这些操作会立即改变当前战斗或奖励流程。</p></div></header>
      <div class="battle-action-grid command-grid">
        <button class="btn command-button" :disabled="store.writeLocked" @click="store.command('cheat.endWave')"><FastForward :size="20" /><span><strong>结束当前波次</strong><small>推进到本波结算</small></span></button>
        <button class="btn btn-error command-button danger-command" :disabled="store.writeLocked" @click="store.command('cheat.clearEnemies')"><Skull :size="20" /><span><strong>清除所有敌人</strong><small>立即清理当前场景</small></span></button>
        <button class="btn command-button" :disabled="store.writeLocked" @click="store.command('cheat.skipRewardPopup')"><Gift :size="20" /><span><strong>跳过当前奖励</strong><small>放弃奖励并解除弹窗阻断</small></span></button>
      </div>
    </section>
    <section class="card battle-section mechanical-section" :class="{ mutationLocked: store.writeLocked }">
      <header class="section-heading"><div><h2>游戏内快捷键</h2><p>不用切回工具，在游戏窗口内直接按键；行为与开发者模式的 F / F1 一致。</p></div></header>
      <div class="battle-action-grid">
        <label class="action-toggle"><Keyboard :size="19" /><span><strong>启用 F / F1</strong><small>F 清除当前所有怪物；F1 结束当前波次</small></span><input class="checkbox checkbox-success" name="game-hotkeys" type="checkbox" :checked="hotkeysEnabled" :disabled="store.writeLocked" @change="toggleHotkeys(($event.target as HTMLInputElement).checked, $event.target as HTMLInputElement)" /></label>
        <div class="hotkey-state">
          <span class="status-badge" :class="hotkeysLive ? 'online' : (hotkeysEnabled ? 'warning' : '')">{{ hotkeyStateLabel }}</span>
          <small>快捷键只在工具与游戏保持连接时生效。</small>
        </div>
      </div>
    </section>
    <section class="card battle-section mechanical-section" :class="{ mutationLocked: store.writeLocked }">
      <header class="section-heading"><div><h2>常驻启动</h2><p>连接游戏并启用作弊后自动打开这些功能；改动会立刻保存。</p></div></header>
      <div class="battle-action-grid">
        <label v-for="option in persistentOptions" :key="option.key" class="action-toggle">
          <component :is="option.icon" :size="19" />
          <span><strong>{{ option.label }}</strong><small>{{ option.description }}</small></span>
          <input class="checkbox checkbox-success" :name="`persistent-${option.key}`" type="checkbox" :checked="persistent.has(option.key)" :disabled="store.writeLocked" @change="togglePersistent(option.key, ($event.target as HTMLInputElement).checked, $event.target as HTMLInputElement)" />
        </label>
        <div class="hotkey-state">
          <span class="status-badge" :class="persistentStateClass">{{ persistentStateLabel }}</span>
          <small>已保存 {{ persistentSavedCount }} / {{ persistentOptions.length }} 项；进入对局后由工具自动恢复。</small>
        </div>
      </div>
    </section>
  </div>
</template>

