<script setup lang="ts">
import { hasAccess, clearAccess } from './api/access'
import PendingMutationNotice from './components/PendingMutationNotice.vue'
import AccessView from './views/AccessView.vue'
</script>

<template>
  <AccessView v-if="!hasAccess" />
  <template v-else>
    <PendingMutationNotice />
    <button class="access-logout" type="button" @click="clearAccess">锁定</button>
    <RouterView />
  </template>
</template>

<style scoped>
.access-logout {
  position: fixed;
  z-index: 20;
  top: max(8px, env(safe-area-inset-top));
  right: max(8px, calc((100vw - 430px) / 2 + 8px));
  padding: 6px 10px;
  border: 1px solid var(--color-border-control);
  border-radius: 8px;
  background: var(--color-bg-surface);
  color: var(--color-text-secondary);
  font-size: 12px;
}
</style>
