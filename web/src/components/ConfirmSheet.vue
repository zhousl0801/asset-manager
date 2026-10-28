<script setup lang="ts">
import BottomSheet from './BottomSheet.vue'

withDefaults(defineProps<{
  open: boolean
  title: string
  message: string
  confirmLabel?: string
  cancelLabel?: string
  danger?: boolean
  busy?: boolean
}>(), {
  confirmLabel: '确认',
  cancelLabel: '取消',
  danger: false,
  busy: false
})

defineEmits<{ confirm: []; cancel: [] }>()
</script>

<template>
  <BottomSheet :open="open" :title="title" :close-disabled="busy" @close="$emit('cancel')">
    <p class="confirm-message">{{ message }}</p>
    <div class="confirm-actions">
      <button type="button" class="secondary-button" :disabled="busy" @click="$emit('cancel')">
        {{ cancelLabel }}
      </button>
      <button
        type="button"
        class="confirm-button"
        :class="{ danger }"
        :disabled="busy"
        @click="$emit('confirm')"
      >
        {{ busy ? '处理中…' : confirmLabel }}
      </button>
    </div>
  </BottomSheet>
</template>

<style scoped>
.confirm-message {
  margin: 0;
  color: var(--color-text-secondary);
  font-size: var(--text-subhead);
  line-height: 1.65;
}

.confirm-actions {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
  margin-top: 4px;
}

.confirm-button {
  min-height: 48px;
  border: 0;
  border-radius: var(--radius-card);
  background: var(--color-brand);
  color: var(--color-on-brand);
  font-size: var(--text-body);
  font-weight: 600;
}

.confirm-button.danger {
  background: var(--color-danger);
  color: var(--color-on-danger);
}

.confirm-button:hover:not(:disabled) {
  background: var(--color-brand-hover);
}

.confirm-button.danger:hover:not(:disabled) {
  background: var(--color-danger-hover);
}

.confirm-button:disabled {
  background: var(--color-bg-disabled);
  color: var(--color-text-disabled);
}
</style>
