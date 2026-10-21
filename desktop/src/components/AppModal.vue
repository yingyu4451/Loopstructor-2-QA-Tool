<script setup lang="ts">
import { nextTick, onBeforeUnmount, ref, watch } from 'vue'
import { X } from '../icons'
import { useUiStore } from '../stores/ui'
import managerLogo from '../../../assets/branding/manager-logo-256.png'
const ui = useUiStore()
const dialogElement = ref<HTMLElement | null>(null)
const confirmButton = ref<{ focus: () => void } | null>(null)
let restoreFocus: HTMLElement | null = null

watch(() => ui.confirmDialog, async (dialog) => {
  if (!dialog) {
    restoreFocus?.focus()
    restoreFocus = null
    return
  }
  restoreFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null
  await nextTick()
  confirmButton.value?.focus()
})

function onKeydown(event: { key: string; shiftKey: boolean; preventDefault: () => void }) {
  if (!ui.confirmDialog) return
  if (event.key === 'Escape') {
    event.preventDefault()
    ui.resolveConfirm(false)
    return
  }
  if (event.key !== 'Tab' || !dialogElement.value) return
  const focusable = Array.from(dialogElement.value.querySelectorAll<HTMLElement>('button:not([disabled]), a[href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'))
  if (focusable.length === 0) return
  const first = focusable[0]
  const last = focusable[focusable.length - 1]
  if (event.shiftKey && document.activeElement === first) {
    event.preventDefault()
    last.focus()
  } else if (!event.shiftKey && document.activeElement === last) {
    event.preventDefault()
    first.focus()
  }
}

window.addEventListener('keydown', onKeydown)
onBeforeUnmount(() => window.removeEventListener('keydown', onKeydown))
</script>

<template>
  <div v-if="ui.confirmDialog" class="modal modal-open modal-backdrop" @click.self="ui.resolveConfirm(false)">
    <section ref="dialogElement" class="modal-box mechanical-modal" role="dialog" aria-modal="true" :aria-label="ui.confirmDialog.title">
      <header>
        <div class="modal-title"><img :src="managerLogo" alt="" width="34" height="34" /><h2>{{ ui.confirmDialog.title }}</h2></div>
        <button class="btn btn-square btn-ghost btn-sm icon-button" aria-label="关闭" @click="ui.resolveConfirm(false)"><X :size="18" /></button>
      </header>
      <p>{{ ui.confirmDialog.message }}</p>
      <footer>
        <button class="btn btn-outline button secondary" @click="ui.resolveConfirm(false)">{{ ui.confirmDialog.cancelText }}</button>
        <button ref="confirmButton" class="btn button" :class="ui.confirmDialog.danger ? 'btn-error danger' : 'btn-primary primary'" @click="ui.resolveConfirm(true)">{{ ui.confirmDialog.confirmText }}</button>
      </footer>
    </section>
  </div>
</template>
