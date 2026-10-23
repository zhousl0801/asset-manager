<script setup lang="ts">
import AppIcon from './AppIcon.vue'

withDefaults(defineProps<{
  title: string
  description?: string
  icon?: string
  actionLabel?: string
  compact?: boolean
}>(), { icon: 'ledger' })

defineEmits<{ action: [] }>()
</script>

<template>
  <div class="app-empty-state" :class="{ compact }">
    <span class="empty-icon"><AppIcon :name="icon" /></span>
    <h3>{{ title }}</h3>
    <p v-if="description">{{ description }}</p>
    <button v-if="actionLabel" type="button" class="secondary-button" @click="$emit('action')">{{ actionLabel }}</button>
    <slot />
  </div>
</template>

<style scoped>
.app-empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: 36px 20px;
  border-radius: 14px;
  background: var(--color-bg-surface);
  text-align: center;
}
.empty-icon {
  display: grid;
  place-items: center;
  width: 48px;
  height: 48px;
  margin-bottom: 14px;
  border-radius: 16px;
  background: var(--color-brand-soft);
  color: var(--color-brand);
}
h3 {
  margin: 0;
  color: var(--color-text-primary);
  font-size: 15px;
  font-weight: 550;
  line-height: 1.5;
}
p {
  max-width: 260px;
  margin: 7px 0 0;
  color: var(--color-text-secondary);
  font-size: 12px;
  line-height: 1.7;
}
.secondary-button {
  margin-top: 18px;
  min-height: 42px;
}
.compact {
  padding: 22px 16px;
}
.compact .empty-icon {
  width: 38px;
  height: 38px;
  margin-bottom: 10px;
  border-radius: 12px;
}
.compact h3 {
  font-size: 13px;
}
</style>
