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
const sheet = ref<HTMLElement | null>(null)
let previousOverflow = ''

watch(
  () => props.open,
  async (open) => {
    if (open) {
      previousOverflow = document.body.style.overflow
      document.body.style.overflow = 'hidden'
      await nextTick()
      const input = sheet.value?.querySelector<HTMLElement>('input, select, textarea')

      if (input) {
        input.focus()
      } else {
        sheet.value?.focus()
      }
    } else {
      document.body.style.overflow = previousOverflow
    }
  },
  { immediate: true }
)

onMounted(() => {
  document.addEventListener('keydown', handleKeydown)
})

onBeforeUnmount(() => {
  document.removeEventListener('keydown', handleKeydown)
  document.body.style.overflow = previousOverflow
})

function close(): void {
  if (!props.closeDisabled) {
    emit('close')
  }
}

function handleKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape' && props.open) {
    event.preventDefault()
    close()
  }
}
</script>

<template>
  <Teleport to="body">
    <div v-if="open" class="sheet-backdrop" @click.self="close">
      <section
        ref="sheet"
        class="sheet"
        role="dialog"
        aria-modal="true"
        :aria-labelledby="sheetId"
        tabindex="-1"
      >
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
  padding: 20px 19px calc(24px + env(safe-area-inset-bottom));
  border-radius: 18px 18px 0 0;
  background: var(--color-bg-surface);
  box-shadow: 0 -8px 30px var(--color-shadow);
  outline: none;
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
  font-size: 17px;
  font-weight: 600;
  letter-spacing: -0.3px;
}

.sheet-head p {
  margin: 5px 0 0;
  color: var(--color-text-secondary);
  font-size: 11px;
}

.sheet-close {
  min-height: 32px;
  padding: 0 6px;
  border: 0;
  background: transparent;
  color: var(--color-brand);
  font-size: 12px;
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
    border-radius: 16px;
  }
}
</style>
