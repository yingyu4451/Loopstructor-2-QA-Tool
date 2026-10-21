<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import type { CatalogItem } from '../types'
import CatalogCard from './CatalogCard.vue'

const props = withDefaults(defineProps<{
  items: CatalogItem[]
  counts?: Record<string, number>
  selectedIds?: string[]
  disabled?: boolean
  minItemWidth?: number
  itemHeight?: number
}>(), { minItemWidth: 144, itemHeight: 84 })
const emit = defineEmits<{ invoke: [button: 'left' | 'right', item: CatalogItem] }>()
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
  <div ref="host" class="virtual-catalog" @scroll="scrollTop = ($event.target as HTMLElement).scrollTop">
    <p v-if="props.items.length === 0" class="virtual-empty">没有匹配的条目，试试调整搜索或获取池。</p>
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
