<script setup lang="ts">
defineProps<{ title: string; backLabel?: string }>()
defineEmits<{ back: [] }>()
</script>

<template>
  <header class="page-header">
    <div class="header-side"><button v-if="backLabel" type="button" class="back-link" @click="$emit('back')">‹ {{ backLabel }}</button><slot name="leading" /></div>
    <h1>{{ title }}</h1>
    <div class="header-side header-actions"><slot name="actions" /></div>
  </header>
</template>

<style scoped>
.page-header {
  display: grid;
  grid-template-columns: 1fr auto 1fr;
  align-items: center;
  gap: 10px;
  min-height: 44px;
  margin-bottom: 20px;
}
h1 {
  margin: 0;
  color: var(--color-text-primary);
  font-size: 18px;
  font-weight: 650;
  letter-spacing: -0.4px;
  white-space: nowrap;
}
.header-side {
  display: flex;
  align-items: center;
  min-width: 0;
}
.header-actions {
  justify-content: flex-end;
  gap: 8px;
}
</style>
