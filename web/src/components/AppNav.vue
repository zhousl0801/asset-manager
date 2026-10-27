<script setup lang="ts">
import AppIcon from './AppIcon.vue'
</script>

<template>
  <nav class="app-nav" aria-label="主导航">
    <RouterLink class="nav-item" exact-active-class="active" to="/">
      <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m3 10 9-7 9 7v10H3Z" /><path d="M9 20v-7h6v7" /></svg>
      <span>总览</span>
    </RouterLink>
    <RouterLink class="nav-item" active-class="active" to="/ledger">
      <svg viewBox="0 0 24 24" aria-hidden="true"><rect x="5" y="3" width="15" height="18" rx="2" /><path d="M3 7h5M3 12h5M3 17h5M11 8h6M11 12h6" /></svg>
      <span>账本</span>
    </RouterLink>
    <RouterLink class="nav-add" to="/entry" aria-label="记一笔">
      <span class="nav-add-circle"><AppIcon name="plus" /></span>
      <span class="nav-add-label">记一笔</span>
    </RouterLink>
    <RouterLink class="nav-item" active-class="active" to="/plans">
      <svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="8" /><path d="M12 7v5l3 2" /></svg>
      <span>计划</span>
    </RouterLink>
    <RouterLink class="nav-item" active-class="active" to="/mine">
      <svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="8" r="4" /><path d="M4 21v-2a8 8 0 0 1 16 0v2" /></svg>
      <span>我的</span>
    </RouterLink>
  </nav>
</template>

<style scoped>
.app-nav {
  position: fixed;
  bottom: 0;
  left: 50%;
  z-index: 20;
  display: grid;
  grid-template-columns: repeat(5, minmax(0, 1fr));
  box-sizing: border-box;
  width: min(100%, 430px);
  height: calc(68px + env(safe-area-inset-bottom));
  padding: 6px 2px calc(6px + env(safe-area-inset-bottom));
  border-top: 1px solid var(--color-border-subtle);
  background: var(--color-bg-surface);
  transform: translateX(-50%);
}

.nav-item {
  display: flex;
  flex-direction: column;
  justify-content: center;
  align-items: center;
  gap: 4px;
  min-width: 0;
  padding: 0;
  color: var(--color-text-secondary);
  font-size: 10px;
  text-decoration: none;
}

.nav-item svg {
  width: 21px;
  height: 21px;
  fill: none;
  stroke: currentColor;
  stroke-width: 1.7;
  stroke-linecap: round;
  stroke-linejoin: round;
}

.nav-item:hover {
  color: var(--color-brand);
}

.nav-item.active {
  color: var(--color-brand);
  font-weight: 600;
}

.nav-add {
  display: flex;
  flex-direction: column;
  justify-content: flex-end;
  align-items: center;
  gap: 3px;
  min-width: 0;
  padding: 0;
  color: var(--color-brand);
  font-size: 10px;
  font-weight: 600;
  text-decoration: none;
}

.nav-add-circle {
  display: grid;
  place-items: center;
  width: 46px;
  height: 46px;
  margin-top: -24px;
  border: 3px solid var(--color-bg-surface);
  border-radius: 50%;
  background: var(--color-brand);
  color: var(--color-on-brand);
  box-shadow: 0 5px 14px var(--color-shadow);
  transition: background .15s ease, transform .15s ease;
}

.nav-add-circle :deep(.app-icon) {
  width: 23px;
  height: 23px;
}

.nav-add:hover .nav-add-circle {
  background: var(--color-brand-hover);
}

.nav-add:active .nav-add-circle {
  transform: scale(.94);
}

.nav-add-label {
  line-height: 1.2;
}
</style>
