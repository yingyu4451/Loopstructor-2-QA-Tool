<script setup lang="ts">
import type { Component } from 'vue'

withDefaults(defineProps<{
  icon: Component
  title: string
  hint?: string
  compact?: boolean
}>(), { hint: '', compact: false })
</script>

<template>
  <div class="empty-state-panel" :class="{ compact }">
    <span class="empty-state-icon" aria-hidden="true"><component :is="icon" :size="compact ? 17 : 21" /></span>
    <strong>{{ title }}</strong>
    <p v-if="hint">{{ hint }}</p>
    <slot />
  </div>
</template>
