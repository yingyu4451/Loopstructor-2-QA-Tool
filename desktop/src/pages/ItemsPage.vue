<script setup lang="ts">

type CheckboxInput = { checked: boolean }
import { computed, ref } from 'vue'
import { MapPin, PackageOpen, Search, Trash2 } from '../icons'
import { useAppStore } from '../stores/app'
import { useUiStore } from '../stores/ui'
import type { CatalogItem } from '../types'
import CatalogPoolSwitch from '../components/CatalogPoolSwitch.vue'
import VirtualCatalogGrid from '../components/VirtualCatalogGrid.vue'

const store = useAppStore()
const ui = useUiStore()
const consumableSearch = ref('')
const catapultSearch = ref('')

function filtered(key: string, search: string) {
  const term = search.trim().toLocaleLowerCase()
  return store.catalogItems(key).filter((item) => !term || [item.name, item.enumName, item.id].join(' ').toLocaleLowerCase().includes(term))
}
const consumables = computed(() => filtered('disposables', consumableSearch.value))
const catapults = computed(() => filtered('catapultPoints', catapultSearch.value))
const consumableCounts = computed(() => ownedCounts(store.cheatState?.ownedConsumables, 'disposableId'))
const catapultCounts = computed(() => ownedCounts(store.cheatState?.ownedCatapultPoints, 'disposableId'))
const fieldDeleteMode = computed(() => store.cheatState?.fieldCatapultDeleteMode === true)

function ownedCounts(items: any[] | undefined, key: string) {
  const result: Record<string, number> = {}
  for (const item of items ?? []) result[item[key] || item.enumName || item.id] = Number(item.count || 0)
  return result
}
async function grantConsumable(_button: 'left' | 'right', item: CatalogItem) {
  if ((consumableCounts.value[item.id] ?? 0) >= 5) {
    ui.toast('该消耗品已经达到游戏持有上限 5。', 'warning')
    return
  }
  if (await store.command('cheat.grantDisposable', { disposableId: item.id, count: 1 })) await store.refreshState()
}
async function grantCatapult(_button: 'left' | 'right', item: CatalogItem) {
  if (await store.command('cheat.grantCatapultPoint', { disposableId: item.id, count: 1 })) await store.refreshState()
}
async function clearAll(command: string, message: string) {
  if (!(await ui.confirm({ title: '确认删除', message, confirmText: '删除', danger: true }))) return
  if (await store.command(command)) await store.refreshState()
}
async function setFieldDeleteMode(enabled: boolean, input: CheckboxInput) {
  const response = await store.command('cheat.setFieldCatapultDeleteMode', { enabled })
  if (response?.success) await store.refreshState()
  else input.checked = !enabled
}
</script>

<template>
  <div class="items-workspace grid h-full min-h-0">
    <section class="card inventory-column mechanical-section min-h-0">
      <header class="section-heading compact-heading">
        <div class="heading-with-icon"><PackageOpen :size="20" /><div><h2>消耗品</h2><p>左键直接获取，单种最多持有 5 个。</p></div></div>
        <div class="header-actions">
          <button class="btn btn-error btn-sm button danger compact" :disabled="store.writeLocked || !store.connected" @click="clearAll('cheat.clearConsumables', '将删除背包与场上的全部消耗品，且无法撤销。')"><Trash2 :size="16" />全部删除</button>
        </div>
      </header>
      <div class="search-row">
        <label class="input input-bordered search-box"><Search :size="17" /><input v-model="consumableSearch" name="consumable-search" autocomplete="off" aria-label="搜索消耗品" placeholder="搜索消耗品…" /></label>
        <CatalogPoolSwitch />
      </div>
      <VirtualCatalogGrid :items="consumables" :counts="consumableCounts" :disabled="store.writeLocked || !store.connected" :search-term="consumableSearch" @invoke="grantConsumable" />
      <footer class="fixed-operation-note">{{ consumables.length }} 项 · 点击卡片立即写入背包</footer>
    </section>
    <section class="card inventory-column mechanical-section min-h-0">
      <header class="section-heading compact-heading">
        <div class="heading-with-icon"><MapPin :size="20" /><div><h2>弹射点</h2><p>包含普通、能量和当前游戏配置的特殊站点。</p></div></div>
      </header>
      <label class="input input-bordered search-box"><Search :size="17" /><input v-model="catapultSearch" name="catapult-search" autocomplete="off" aria-label="搜索弹射点" placeholder="搜索弹射点…" /></label>
      <VirtualCatalogGrid :items="catapults" :counts="catapultCounts" :disabled="store.writeLocked || !store.connected" :search-term="catapultSearch" :empty-action="false" @invoke="grantCatapult" />
      <label class="field-delete-toggle" :class="{ disabled: store.writeLocked || !store.connected }">
        <input class="checkbox checkbox-success" name="field-catapult-delete-mode" type="checkbox" :checked="fieldDeleteMode" :disabled="store.writeLocked || !store.connected" @change="setFieldDeleteMode(($event.target as HTMLInputElement).checked, $event.target as HTMLInputElement)" />
        <span class="switch-track"><span /></span>
        <span>点击场上弹射点直接删除</span>
      </label>
      <footer class="inventory-action-footer">
        <button class="btn btn-error btn-sm button danger compact" :disabled="store.writeLocked || !store.connected" @click="clearAll('cheat.clearBackpackCatapultPoints', '将删除背包内的全部弹射点，且无法撤销。')">清空背包</button>
        <button class="btn btn-error btn-sm button danger compact" :disabled="store.writeLocked || !store.connected" @click="clearAll('cheat.clearFieldCatapultPoints', '将删除场上的全部弹射点，且无法撤销。')">清空场上</button>
      </footer>
    </section>
  </div>
</template>

