<script setup lang="ts">
import { computed, ref } from 'vue'
import { ImageOff, Plus, Search, Sparkles, TrainFront, Trash2 } from '../icons'
import { useAppStore } from '../stores/app'
import { useUiStore } from '../stores/ui'
import type { CatalogItem } from '../types'
import CatalogCard from '../components/CatalogCard.vue'
import CatalogPoolSwitch from '../components/CatalogPoolSwitch.vue'
import EmptyState from '../components/EmptyState.vue'
import VirtualCatalogGrid from '../components/VirtualCatalogGrid.vue'

const store = useAppStore()
const ui = useUiStore()
const search = ref('')
const enchantmentSearch = ref('')
const activeType = ref('all')
const selectedVehicle = ref<CatalogItem>()
const count = ref(1)
const enchantments = ref<Record<string, number>>({})

const vehicles = computed(() => store.catalogItems('vehicles'))
const types = computed(() => {
  const found = new Map<string, { key: string; label: string; order: number }>()
  for (const item of vehicles.value) {
    const key = item.typeKey || String(item.enumName || item.id).split('_')[0]
    if (!found.has(key)) found.set(key, { key, label: item.typeName || translateType(key), order: item.typeOrder ?? 999 })
  }
  return [...found.values()].sort((a, b) => a.order - b.order || a.label.localeCompare(b.label, 'zh-CN'))
})
const filteredVehicles = computed(() => {
  const term = search.value.trim().toLocaleLowerCase()
  return vehicles.value.filter((item) => {
    const type = item.typeKey || String(item.enumName || item.id).split('_')[0]
    const matchesType = term ? true : activeType.value === 'all' || activeType.value === type
    const haystack = [item.name, item.enumName, item.id, item.typeKey, item.familyKey].join(' ').toLocaleLowerCase()
    return matchesType && (!term || haystack.includes(term))
  })
})
const families = computed(() => {
  const groups = new Map<string, CatalogItem[]>()
  for (const item of filteredVehicles.value) {
    const key = item.familyKey || String(item.enumName || item.id).replace(/_L\d+$/i, '')
    if (!groups.has(key)) groups.set(key, [])
    groups.get(key)!.push(item)
  }
  return [...groups.entries()].map(([key, items]) => ({
    key,
    items: items.sort((a, b) => (a.level ?? 999) - (b.level ?? 999) || (a.itemOrder ?? 999) - (b.itemOrder ?? 999)),
  }))
})
const enchantmentItems = computed(() => {
  const term = enchantmentSearch.value.trim().toLocaleLowerCase()
  return store.catalogItems('enchantments').filter((item) => !term || [item.name, item.enumName, item.id].join(' ').toLocaleLowerCase().includes(term))
})
const selectedEnchantments = computed(() => store.catalogItems('enchantments').filter((item) => (enchantments.value[item.id] ?? 0) > 0))

function translateType(type: string) {
  return ({ Shell: '炮弹', Link: '链接', Missile: '导弹', Penetrate: '穿透' } as Record<string, string>)[type] || type
}
function vehicleImage(item: CatalogItem) {
  if (item.iconDataUrl) return item.iconDataUrl
  if (!item.iconBase64) return ''
  return item.iconBase64.startsWith('data:') ? item.iconBase64 : `data:image/png;base64,${item.iconBase64}`
}
function shapeLabel(level?: number) {
  return level === 1 ? '一星' : level === 2 ? '二星' : level === 3 ? '三星' : '未知形态'
}
function adjustEnchantment(button: 'left' | 'right', item: CatalogItem) {
  const current = enchantments.value[item.id] ?? 0
  const next = button === 'left' ? Math.min(2147483647, current + 1) : Math.max(0, current - 1)
  enchantments.value = { ...enchantments.value, [item.id]: next }
}
async function grantVehicle() {
  if (!selectedVehicle.value) return
  await store.command('cheat.grantVehicle', {
    vehicleId: selectedVehicle.value.id,
    count: count.value,
    enchantments: Object.entries(enchantments.value).filter(([, level]) => level > 0).map(([enchantmentId, level]) => ({ enchantmentId, level })),
  })
}
async function clearVehicles(command: string, message: string) {
  if (!(await ui.confirm({ title: '确认删除', message, confirmText: '删除', danger: true }))) return
  if (await store.command(command)) await store.refreshState()
}
</script>

<template>
  <div class="vehicle-workspace grid h-full min-h-0">
    <section class="card vehicle-selector mechanical-section min-h-0">
      <header class="section-heading compact-heading">
        <div class="heading-with-icon"><TrainFront :size="21" /><div><h2>战车目录</h2></div></div>
      </header>
      <div class="search-row">
        <label class="input input-bordered search-box"><Search :size="17" /><input v-model="search" name="vehicle-search" autocomplete="off" aria-label="搜索战车" placeholder="搜索中文名、枚举或系列…" /></label>
        <CatalogPoolSwitch />
      </div>
      <div class="tabs tabs-box type-switcher" role="group" aria-label="战车类型">
        <button class="tab" :class="{ active: activeType === 'all' }" :aria-pressed="activeType === 'all'" @click="activeType = 'all'">全部</button>
        <button v-for="type in types" :key="type.key" class="tab" :class="{ active: activeType === type.key }" :aria-pressed="activeType === type.key" @click="activeType = type.key">{{ type.label }}</button>
      </div>
      <div class="vehicle-family-grid">
        <EmptyState
          v-if="families.length === 0"
          :icon="vehicles.length ? Search : TrainFront"
          :title="!store.connected ? '等待选择游戏' : vehicles.length ? '没有匹配的战车' : '这里还没有战车'"
          :hint="!store.connected ? '选择游戏后自动读取可获取战车。' : vehicles.length ? '换个关键词或切换类型。' : '当前游戏版本没有返回战车目录。'"
        >
          <button v-if="!store.connected" class="btn btn-outline btn-sm button secondary compact" @click="store.setRoute('game')">去选择游戏</button>
        </EmptyState>
        <article v-for="family in families" :key="family.key" class="card vehicle-family" :class="{ selected: family.items.some(item => item.id === selectedVehicle?.id) }">
          <div class="card-body">
            <div class="family-identity">
              <span class="vehicle-game-icon">
                <img v-if="vehicleImage(family.items[0])" :src="vehicleImage(family.items[0])" alt="" width="46" height="46" loading="lazy" />
                <ImageOff v-else :size="22" />
              </span>
              <div><strong>{{ family.items[0].name || family.items[0].fallbackName || family.key }}</strong><small>{{ family.key }}</small></div>
            </div>
            <div class="level-buttons">
              <button
                v-for="item in family.items"
                :key="item.id"
                class="btn btn-sm"
                :class="{ active: selectedVehicle?.id === item.id }"
                :aria-pressed="selectedVehicle?.id === item.id"
                @click="selectedVehicle = item"
              >{{ shapeLabel(item.level) }}</button>
            </div>
          </div>
        </article>
      </div>
    </section>

    <section class="card vehicle-loadout mechanical-section" :class="{ mutationLocked: store.writeLocked }">
      <header class="section-heading"><div><h2>本次装配</h2><p>{{ selectedVehicle?.name || selectedVehicle?.enumName || '选择左侧任意形态' }}</p></div></header>
      <div class="selected-enchantment-wrap">
        <CatalogCard
          v-for="item in selectedEnchantments"
          :key="item.id"
          :item="item"
          :count="enchantments[item.id]"
          @invoke="adjustEnchantment"
        />
        <EmptyState
          v-if="selectedEnchantments.length === 0"
          compact
          :icon="Sparkles"
          :title="selectedVehicle ? '未选择附魔' : '尚未选择战车'"
          :hint="selectedVehicle ? '直接获取普通战车。' : '从左侧选择形态。'"
        />
      </div>
      <div v-if="store.connected" class="bulk-delete-row grant-row">
        <span>批量删除</span>
        <button class="btn btn-error btn-sm button danger compact" :disabled="store.writeLocked" @click="clearVehicles('cheat.clearFieldVehicles', '将删除场上的全部战车，且无法撤销。')"><Trash2 :size="16" />删除场上全部战车</button>
        <button class="btn btn-error btn-sm button danger compact" :disabled="store.writeLocked" @click="clearVehicles('cheat.clearBackpackVehicles', '将删除背包内的全部战车，且无法撤销。')"><Trash2 :size="16" />删除背包全部战车</button>
      </div>
      <div class="grant-row">
        <label>数量<input v-model.number="count" class="input input-bordered input-sm" name="vehicle-count" autocomplete="off" type="number" inputmode="numeric" min="1" max="999" /></label>
        <button class="btn btn-primary button primary" :disabled="!selectedVehicle || store.writeLocked" @click="grantVehicle"><Plus :size="17" />获取战车</button>
      </div>
    </section>

    <section class="card enchantment-panel mechanical-section">
      <header class="section-heading compact-heading">
        <div><h2>附魔</h2><p>左键 +1，右键或 Delete 减 1。</p></div>
        <div class="header-actions">
          <button class="btn btn-outline btn-sm button secondary compact" :disabled="selectedEnchantments.length === 0 || !store.connected || store.writeLocked" @click="enchantments = {}"><Trash2 :size="16" />清空所选</button>
        </div>
      </header>
      <div class="search-row">
        <label class="input input-bordered search-box"><Search :size="17" /><input v-model="enchantmentSearch" name="enchantment-search" autocomplete="off" aria-label="搜索附魔" placeholder="搜索附魔中文名或枚举…" /></label>
      </div>
      <VirtualCatalogGrid :items="enchantmentItems" :counts="enchantments" :search-term="enchantmentSearch" :empty-action="false" @invoke="adjustEnchantment" />
    </section>
  </div>
</template>

