<script lang="ts">
// 多个 sheet 叠加时，只让最上层处理 Tab / Escape，避免下层同时抢焦点。
const openSheets: symbol[] = []
</script>

<script setup lang="ts">
import { nextTick, onBeforeUnmount, onMounted, ref, useId, watch } from 'vue'

const props = defineProps<{
  open: boolean
  title: string
  subtitle?: string
  closeDisabled?: boolean
}>()

const emit = defineEmits<{
  close: []
}>()

const sheetId = useId()
const instanceId = Symbol('sheet')
const sheet = ref<HTMLElement | null>(null)
const dragY = ref(0)
const dragging = ref(false)
let previousOverflow = ''
let previousFocus: HTMLElement | null = null
let dragStartY: number | null = null
let wasOpen = false

watch(
  () => props.open,
  async (open) => {
    if (open) {
      wasOpen = true

      if (!openSheets.includes(instanceId)) {
        openSheets.push(instanceId)
      }
      previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null
      previousOverflow = document.body.style.overflow
      document.body.style.overflow = 'hidden'
      dragY.value = 0
      await nextTick()
      // 不自动聚焦输入框，避免打开 sheet 就弹出键盘；先把焦点放在对话框本身。
      sheet.value?.focus()
    } else if (wasOpen) {
      wasOpen = false
      unregisterSheet()
      document.body.style.overflow = previousOverflow

      if (previousFocus?.isConnected) {
        previousFocus.focus()
      }

      previousFocus = null
    }
  },
  { immediate: true }
)

onMounted(() => {
  document.addEventListener('keydown', handleKeydown)
})

onBeforeUnmount(() => {
  document.removeEventListener('keydown', handleKeydown)
  unregisterSheet()
  document.body.style.overflow = previousOverflow
})

function unregisterSheet(): void {
  const index = openSheets.indexOf(instanceId)

  if (index >= 0) {
    openSheets.splice(index, 1)
  }
}

function close(): void {
  if (!props.closeDisabled) {
    emit('close')
  }
}

function focusables(): HTMLElement[] {
  if (!sheet.value) {
    return []
  }

  return [...sheet.value.querySelectorAll<HTMLElement>(
    'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'
  )].filter((element) =>
    element.getAttribute('aria-hidden') !== 'true'
    && (element.offsetWidth > 0 || element.offsetHeight > 0 || element.getClientRects().length > 0)
  )
}

function handleKeydown(event: KeyboardEvent): void {
  if (!props.open || openSheets[openSheets.length - 1] !== instanceId) {
    return
  }

  if (event.key === 'Escape') {
    event.preventDefault()
    close()
    return
  }

  if (event.key !== 'Tab') {
    return
  }

  const nodes = focusables()

  if (nodes.length === 0) {
    event.preventDefault()
    sheet.value?.focus()
    return
  }

  const first = nodes[0]!
  const last = nodes[nodes.length - 1]!

  if (event.shiftKey && (document.activeElement === first || document.activeElement === sheet.value)) {
    event.preventDefault()
    last.focus()
  } else if (!event.shiftKey && document.activeElement === last) {
    event.preventDefault()
    first.focus()
  }
}

function startDrag(event: PointerEvent): void {
  if (props.closeDisabled) {
    return
  }

  dragStartY = event.clientY
  dragging.value = true
  dragY.value = 0
  ;(event.currentTarget as HTMLElement).setPointerCapture(event.pointerId)
}

function moveDrag(event: PointerEvent): void {
  if (!dragging.value || dragStartY === null) {
    return
  }

  dragY.value = Math.max(0, event.clientY - dragStartY)
}

function endDrag(): void {
  if (!dragging.value) {
    return
  }

  dragging.value = false
  dragStartY = null

  if (dragY.value > 96) {
    dragY.value = 0
    close()
    return
  }

  dragY.value = 0
}
</script>

<template>
  <Teleport to="body">
    <Transition name="sheet">
      <div v-if="open" class="sheet-backdrop" @click.self="close">
        <section
          ref="sheet"
          class="sheet"
          :class="{ dragging }"
          :style="dragY > 0 ? { transform: `translateY(${dragY}px)` } : undefined"
          role="dialog"
          aria-modal="true"
          :aria-labelledby="sheetId"
          tabindex="-1"
        >
          <div
            class="sheet-grabber"
            aria-hidden="true"
            @pointerdown="startDrag"
            @pointermove="moveDrag"
            @pointerup="endDrag"
            @pointercancel="endDrag"
          >
            <span />
          </div>

          <header class="sheet-head">
            <div>
              <h2 :id="sheetId">{{ title }}</h2>
              <p v-if="subtitle">{{ subtitle }}</p>
            </div>
            <button type="button" class="sheet-close" :disabled="closeDisabled" @click="close">
              关闭
            </button>
          </header>
          <div class="sheet-body">
            <slot />
          </div>
        </section>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.sheet-backdrop {
  position: fixed;
  inset: 0;
  z-index: 50;
  display: flex;
  justify-content: center;
  align-items: flex-end;
  background: var(--color-overlay);
}

.sheet {
  box-sizing: border-box;
  width: min(100%, 430px);
  max-height: 88dvh;
  overflow-y: auto;
  padding: 0 var(--page-gutter) calc(24px + env(safe-area-inset-bottom));
  border-radius: var(--radius-sheet) var(--radius-sheet) 0 0;
  background: var(--color-bg-surface);
  box-shadow: 0 -8px 30px var(--color-shadow);
  outline: none;
  transition: transform var(--motion-sheet) var(--ease-sheet);
}

.sheet.dragging {
  transition: none;
}

.sheet-enter-active,
.sheet-leave-active {
  transition: opacity var(--motion-base) ease;
}

.sheet-enter-from,
.sheet-leave-to {
  opacity: 0;
}

.sheet-enter-active .sheet,
.sheet-leave-active .sheet {
  transition: transform var(--motion-sheet) var(--ease-sheet);
}

.sheet-enter-from .sheet,
.sheet-leave-to .sheet {
  transform: translateY(100%);
}

.sheet-grabber {
  display: grid;
  place-items: center;
  height: 28px;
  margin: 0 calc(var(--page-gutter) * -1);
  touch-action: none;
  cursor: grab;
}

.sheet-grabber span {
  width: 36px;
  height: 5px;
  border-radius: var(--radius-pill);
  background: var(--color-border-default);
}

.sheet-head {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 12px;
  margin-bottom: 18px;
}

.sheet-head h2 {
  margin: 0;
  font-size: 1.25rem;
  font-weight: 600;
  letter-spacing: -0.3px;
}

.sheet-head p {
  margin: 5px 0 0;
  color: var(--color-text-secondary);
  font-size: var(--text-footnote);
  line-height: 1.5;
}

.sheet-close {
  min-width: var(--touch-target);
  min-height: var(--touch-target);
  margin: -8px -4px 0 0;
  padding: 0 8px;
  border: 0;
  border-radius: var(--radius-control);
  background: transparent;
  color: var(--color-brand);
  font-size: 0.875rem;
}

.sheet-close:hover:not(:disabled) {
  background: var(--color-brand-soft);
}

.sheet-body {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

@media (min-width: 600px) {
  .sheet-backdrop {
    align-items: center;
  }

  .sheet {
    border-radius: var(--radius-sheet);
  }
}

@media (prefers-reduced-motion: reduce) {
  .sheet,
  .sheet-enter-active .sheet,
  .sheet-leave-active .sheet {
    transition: none;
  }
}
</style>
