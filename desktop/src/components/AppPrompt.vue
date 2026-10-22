<script setup lang="ts">
import { nextTick, onBeforeUnmount, ref, watch } from 'vue'
import { Pencil, X } from '../icons'
import { useUiStore } from '../stores/ui'
import managerLogo from '../../../assets/branding/manager-logo-256.png'
const ui = useUiStore()
const dialogElement = ref<HTMLElement | null>(null)
const inputElement = ref<{ focus: () => void; select?: () => void } | null>(null)
const value = ref('')
let restoreFocus: HTMLElement | null = null

watch(() => ui.promptDialog, async (dialog) => {
  if (!dialog) {
    restoreFocus?.focus()
    restoreFocus = null
    return
  }
  value.value = dialog.value
  restoreFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null
  await nextTick()
  inputElement.value?.focus()
  inputElement.value?.select?.()
})

function submit() {
  ui.resolvePrompt(value.value.trim())
}

function onKeydown(event: { key: string; shiftKey: boolean; preventDefault: () => void }) {
  if (!ui.promptDialog) return
  if (event.key === 'Escape') {
    event.preventDefault()
    ui.resolvePrompt(null)
    return
  }
  if (event.key !== 'Tab' || !dialogElement.value) return
  const focusable = Array.from(dialogElement.value.querySelectorAll<HTMLElement>('button:not([disabled]), input:not([disabled]), [tabindex]:not([tabindex="-1"])'))
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
  <div v-if="ui.promptDialog" class="modal modal-open modal-backdrop" @click.self="ui.resolvePrompt(null)">
    <section ref="dialogElement" class="modal-box mechanical-modal" role="dialog" aria-modal="true" :aria-label="ui.promptDialog.title">
      <header>
        <div class="modal-title"><img :src="managerLogo" alt="" width="34" height="34" /><h2>{{ ui.promptDialog.title }}</h2></div>
        <button class="btn btn-square btn-ghost btn-sm icon-button" aria-label="关闭" @click="ui.resolvePrompt(null)"><X :size="18" /></button>
      </header>
      <p>{{ ui.promptDialog.message }}</p>
      <label class="modal-input-row">
        <Pencil :size="16" />
        <input
          ref="inputElement"
          v-model="value"
          class="input input-bordered"
          :placeholder="ui.promptDialog.placeholder"
          name="prompt-value"
          autocomplete="off"
          maxlength="40"
          @keydown.enter.prevent="submit"
        />
      </label>
      <footer>
        <button class="btn btn-outline button secondary" @click="ui.resolvePrompt(null)">{{ ui.promptDialog.cancelText }}</button>
        <button class="btn btn-primary button primary" @click="submit">{{ ui.promptDialog.confirmText }}</button>
      </footer>
    </section>
  </div>
</template>
