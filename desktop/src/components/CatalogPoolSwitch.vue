<script setup lang="ts">
import { computed } from 'vue'
import { useAppStore } from '../stores/app'
import type { CatalogPoolMode } from '../types'

const store = useAppStore()
const selected = computed<CatalogPoolMode>(() => (store.settings?.catalogPoolMode === 'configured' ? 'configured' : 'all'))
const applied = computed<CatalogPoolMode | undefined>(() => {
  const value = store.catalog?.poolMode
  return value === 'configured' || value === 'all' ? value : undefined
})
const warnings = computed<string[]>(() => {
  const value = store.catalog?.poolWarnings
  return Array.isArray(value) ? value.filter((item): item is string => typeof item === 'string' && item.length > 0) : []
})
const live = computed(() => store.cheatEnabled && applied.value !== undefined)
const stateLabel = computed(() => {
  if (!store.cheatEnabled) return '等待选择游戏'
  if (!applied.value) return '等待生效'
  return applied.value === selected.value ? '已生效' : '正在切换'
})
const stateTitle = computed(() => warnings.value.length > 0
  ? warnings.value.join('；')
  : '游戏配置池：战车来自奖励配置的 Tag 映射；遗物来自奖励主池、备用池与 BOSS 奖励；消耗品来自奖励配置。')
function choose(mode: CatalogPoolMode) {
  if (mode !== selected.value) void store.setCatalogPoolMode(mode)
}
</script>

<template>
  <div class="pool-switch-wrap">
    <div class="pool-switch" role="group" aria-label="获取池">
      <button type="button" class="pool-option" :class="{ active: selected === 'all' }" :aria-pressed="selected === 'all'" title="全部池：目录里的所有项目均可获取" @click="choose('all')">全部池</button>
      <button type="button" class="pool-option" :class="{ active: selected === 'configured' }" :aria-pressed="selected === 'configured'" title="游戏配置池：只显示当前版本奖励配置里可获取的项目" @click="choose('configured')">游戏配置池</button>
    </div>
    <span class="pool-state" :class="{ live }" :title="stateTitle">{{ stateLabel }}</span>
  </div>
</template>
