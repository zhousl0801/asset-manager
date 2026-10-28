<script setup lang="ts">
import { onMounted, watch } from 'vue'
import { accessReady, hasAccess, refreshAccess } from './api/access'
import PendingMutationNotice from './components/PendingMutationNotice.vue'
import AccessView from './views/AccessView.vue'

onMounted(refreshAccess)

// 会话失效或退出登录后重新探测：口令页面要据此决定是否显示 Passkey 登录入口。
watch(hasAccess, (value) => {
  if (!value) void refreshAccess()
})
</script>

<template>
  <p v-if="!accessReady" class="access-splash">正在验证访问权限…</p>
  <AccessView v-else-if="!hasAccess" />
  <template v-else>
    <PendingMutationNotice />
    <RouterView v-slot="{ Component }">
      <Transition name="page" mode="out-in">
        <component :is="Component" />
      </Transition>
    </RouterView>
  </template>
</template>

<style scoped>
.access-splash {
  display: grid;
  min-height: 100dvh;
  margin: 0;
  place-items: center;
  color: var(--color-text-secondary);
  font-size: var(--text-footnote);
}
</style>
