<script setup lang="ts">
import AppIcon from './AppIcon.vue'

defineEmits<{ back: [] }>()
</script>

<template>
  <button class="back-button" type="button" aria-label="返回" @click="$emit('back')">
    <AppIcon name="chevron-back" />
  </button>
</template>

<style scoped>
.back-button {
  display: inline-flex;
  justify-content: center;
  align-items: center;
  width: var(--touch-target);
  height: var(--touch-target);
  padding: 0;
  border: 1px solid var(--color-border-subtle);
  border-radius: 50%;
  background: var(--color-bg-surface);
  color: var(--color-text-primary);
  transition: background var(--motion-fast) ease, transform var(--motion-fast) ease;
}

.back-button:hover {
  background: var(--color-bg-hover);
}

.back-button:active {
  transform: scale(.94);
}

.back-button :deep(.app-icon) {
  width: 20px;
  height: 20px;
}
</style>
