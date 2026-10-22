<script setup lang="ts">
import { applyAmountKey, type AmountKey } from '../utils/amount'

const props = defineProps<{
  modelValue: string
  canSubmit: boolean
  disabled: boolean
  dateLabel: string
}>()

const emit = defineEmits<{
  'update:modelValue': [value: string]
  submit: []
  pickDate: []
}>()

function press(key: AmountKey): void {
  if (props.disabled) {
    return
  }

  emit('update:modelValue', applyAmountKey(props.modelValue, key))
}

function submit(): void {
  if (props.disabled || !props.canSubmit) {
    return
  }

  emit('submit')
}

function pickDate(): void {
  if (props.disabled) {
    return
  }

  emit('pickDate')
}
</script>

<template>
  <div class="keypad">
    <button type="button" :disabled="disabled" @click="press('7')">7</button>
    <button type="button" :disabled="disabled" @click="press('8')">8</button>
    <button type="button" :disabled="disabled" @click="press('9')">9</button>
    <button type="button" class="key-date" :disabled="disabled" @click="pickDate">
      {{ dateLabel }}
    </button>

    <button type="button" :disabled="disabled" @click="press('4')">4</button>
    <button type="button" :disabled="disabled" @click="press('5')">5</button>
    <button type="button" :disabled="disabled" @click="press('6')">6</button>
    <button type="button" :disabled="disabled" aria-label="加" @click="press('+')">＋</button>

    <button type="button" :disabled="disabled" @click="press('1')">1</button>
    <button type="button" :disabled="disabled" @click="press('2')">2</button>
    <button type="button" :disabled="disabled" @click="press('3')">3</button>
    <button type="button" :disabled="disabled" aria-label="减" @click="press('-')">−</button>

    <button type="button" :disabled="disabled" aria-label="小数点" @click="press('.')">.</button>
    <button type="button" :disabled="disabled" @click="press('0')">0</button>
    <button type="button" :disabled="disabled" aria-label="删除一位" @click="press('back')">⌫</button>
    <button
      type="button"
      class="key-done"
      :disabled="disabled || !canSubmit"
      @click="submit"
    >
      完成
    </button>
  </div>
</template>

<style scoped>
.keypad {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 7px;
  padding: 10px 16px calc(12px + env(safe-area-inset-bottom));
  background: var(--color-bg-surface);
  border-top: 1px solid var(--color-border-subtle);
}

.keypad button {
  min-height: 48px;
  border: 1px solid var(--color-border-subtle);
  border-radius: 9px;
  background: var(--color-bg-surface);
  color: var(--color-text-primary);
  font-size: 19px;
  font-variant-numeric: tabular-nums;
}

.keypad button:hover:not(:disabled) {
  background: var(--color-bg-hover);
}

.keypad button:active:not(:disabled) {
  background: var(--color-bg-selected);
}

.keypad .key-date {
  background: var(--color-brand-soft);
  border-color: transparent;
  color: var(--color-brand);
  font-size: 13px;
  font-weight: 550;
}

.keypad .key-done {
  background: var(--color-brand);
  border-color: var(--color-brand);
  color: var(--color-on-brand);
  font-size: 15px;
  font-weight: 550;
}

.keypad .key-done:hover:not(:disabled) {
  background: var(--color-brand-hover);
  border-color: var(--color-brand-hover);
}

.keypad .key-done:active:not(:disabled) {
  background: var(--color-brand-active);
  border-color: var(--color-brand-active);
}

.keypad button:disabled {
  background: var(--color-bg-disabled);
  border-color: var(--color-border-disabled);
  color: var(--color-text-disabled);
}
</style>
