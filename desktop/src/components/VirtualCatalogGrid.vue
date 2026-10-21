<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import type { CatalogItem } from '../types'
import { PackageOpen, Plug, Search } from '../icons'
import { useAppStore } from '../stores/app'
import CatalogCard from './CatalogCard.vue'
import EmptyState from './EmptyState.vue'

const props = withDefaults(defineProps<{
  items: CatalogItem[]
  counts?: Record<string, number>
  selectedIds?: string[]
  disabled?: boolean
  minItemWidth?: number
  itemHeight?: number
  searchTerm?: string
  emptyAction?: boolean
}>(), { minItemWidth: 144, itemHeight: 84, searchTerm: '', emptyAction: true })
const emit = defineEmits<{ invoke: [button: 'left' | 'right', item: CatalogItem] }>()
const store = useAppStore()
const host = ref<HTMLElement>()
const width = ref(600)
const height = ref(300)
const scrollTop = ref(0)
let observer: ResizeObserver | undefined
const columns = computed(() => Math.max(1, Math.floor(width.value / props.minItemWidth)))
const rowCount = computed(() => Math.ceil(props.items.length / columns.value))
const startRow = computed(() => Math.min(
  Math.max(0, rowCount.value - 1),
  Math.max(0, Math.floor(scrollTop.value / props.itemHeight) - 2),
))
const endRow = computed(() => Math.min(rowCount.value, Math.ceil((scrollTop.value + height.value) / props.itemHeight) + 2))
const visibleItems = computed(() => props.items.slice(startRow.value * columns.value, endRow.value * columns.value))
const selected = computed(() => new Set(props.selectedIds ?? []))
const empty = computed(() => {
  if (!store.connected) return { icon: Plug, title: '等待选择游戏', hint: '选择游戏后会自动读取可获取内容。', offline: true }
  if (props.searchTerm.trim()) return { icon: Search, title: '没有匹配的条目', hint: '换个关键词，或切换获取池。', offline: false }
  return { icon: PackageOpen, title: '这里还没有条目', hint: '当前游戏版本没有返回可获取内容。', offline: false }
})

function onScroll(event: { target: unknown }) {
  scrollTop.value = (event.target as HTMLElement).scrollTop
}
watch(() => props.items, () => {
  scrollTop.value = 0
  if (host.value) host.value.scrollTop = 0
})

onMounted(() => {
  observer = new ResizeObserver(([entry]) => {
    width.value = entry.contentRect.width
    height.value = entry.contentRect.height
  })
  if (host.value) observer.observe(host.value)
})
onBeforeUnmount(() => observer?.disconnect())
</script>

<template>
  <div ref="host" class="virtual-catalog" :class="{ 'is-empty': props.items.length === 0 }" @scroll="onScroll">
    <EmptyState v-if="props.items.length === 0" :icon="empty.icon" :title="empty.title" :hint="empty.hint">
      <button v-if="empty.offline && emptyAction" class="btn btn-outline btn-sm button secondary compact" @click="store.setRoute('game')">去选择游戏</button>
    </EmptyState>
    <div v-else class="virtual-spacer" :style="{ height: `${rowCount * itemHeight}px` }">
      <div
        class="virtual-grid"
        :style="{
          gridTemplateColumns: `repeat(${columns}, minmax(0, 1fr))`,
          transform: `translateY(${startRow * itemHeight}px)`,
        }"
      >
        <CatalogCard
          v-for="item in visibleItems"
          :key="item.id"
          :item="item"
          :count="counts?.[item.id] ?? counts?.[item.enumName ?? '']"
          :selected="selected.has(item.id)"
          :disabled="disabled"
          @invoke="emit('invoke', $event, item)"
        />
      </div>
    </div>
  </div>
</template>
