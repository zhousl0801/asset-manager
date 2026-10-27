<script setup lang="ts">
import { onMounted } from 'vue'
import { accessReady, hasAccess, logout, refreshAccess } from './api/access'
import PendingMutationNotice from './components/PendingMutationNotice.vue'
import AccessView from './views/AccessView.vue'

onMounted(refreshAccess)
</script>

<template>
  <p v-if="!accessReady" class="access-splash">正在验证访问权限…</p>
  <AccessView v-else-if="!hasAccess" />
  <template v-else>
    <PendingMutationNotice />
    <button class="access-logout" type="button" @click="logout">锁定</button>
    <RouterView />
  </template>
</template>

<style scoped>
.access-splash {
  display: grid;
  min-height: 100dvh;
  margin: 0;
  place-items: center;
  color: var(--color-text-secondary);
  font-size: 13px;
}
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
