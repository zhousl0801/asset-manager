<script setup lang="ts">
import AppIcon from './AppIcon.vue'
</script>

<template>
  <nav class="app-nav" aria-label="主导航">
    <RouterLink class="nav-item" exact-active-class="active" to="/">
      <svg class="nav-icon nav-icon-outline" viewBox="0 0 24 24" aria-hidden="true"><path d="m3 10 9-7 9 7v10H3Z" /><path d="M9 20v-7h6v7" /></svg>
      <svg class="nav-icon nav-icon-filled" viewBox="0 0 24 24" aria-hidden="true"><path d="M12 3 2.5 10.5V21h7v-6h5v6h7V10.5Z" fill="currentColor" /><path class="knockout" d="M10 15h4v6h-4z" /></svg>
      <span>总览</span>
    </RouterLink>
    <RouterLink class="nav-item" active-class="active" to="/ledger">
      <svg class="nav-icon nav-icon-outline" viewBox="0 0 24 24" aria-hidden="true"><rect x="5" y="3" width="15" height="18" rx="2" /><path d="M3 7h5M3 12h5M3 17h5M11 8h6M11 12h6" /></svg>
      <svg class="nav-icon nav-icon-filled" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 3h12a3 3 0 0 1 3 3v12a3 3 0 0 1-3 3H6a3 3 0 0 1-3-3V6a3 3 0 0 1 3-3Z" fill="currentColor" /><path class="knockout-line" d="M7.5 8h9M7.5 12h9M7.5 16h5.5" /></svg>
      <span>账本</span>
    </RouterLink>
    <RouterLink class="nav-add" to="/entry" aria-label="记一笔" title="记一笔">
      <span class="nav-add-circle"><AppIcon name="plus" /></span>
      <span class="nav-add-label">记一笔</span>
    </RouterLink>
    <RouterLink class="nav-item" active-class="active" to="/plans">
      <svg class="nav-icon nav-icon-outline" viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="8" /><path d="M12 7v5l3 2" /></svg>
      <svg class="nav-icon nav-icon-filled" viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="9" fill="currentColor" /><path class="knockout-line" d="M12 7v5l3 2" /></svg>
      <span>计划</span>
    </RouterLink>
    <RouterLink class="nav-item" active-class="active" to="/mine">
      <svg class="nav-icon nav-icon-outline" viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="8" r="4" /><path d="M4 21v-2a8 8 0 0 1 16 0v2" /></svg>
      <svg class="nav-icon nav-icon-filled" viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="8" r="4.25" fill="currentColor" /><path d="M4 21v-1.6a8 8 0 0 1 16 0V21Z" fill="currentColor" /></svg>
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
  height: calc(var(--nav-height) + env(safe-area-inset-bottom));
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
  gap: 3px;
  min-width: 0;
  padding: 0;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
  text-decoration: none;
}

.nav-icon {
  width: 22px;
  height: 22px;
  fill: none;
  stroke: currentColor;
  stroke-width: 1.7;
  stroke-linecap: round;
  stroke-linejoin: round;
}

.nav-icon-filled {
  display: none;
  stroke: none;
}

.nav-icon-filled .knockout {
  fill: var(--color-bg-surface);
}

.nav-icon-filled .knockout-line {
  fill: none;
  stroke: var(--color-bg-surface);
  stroke-width: 2;
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

.nav-item.active .nav-icon-outline {
  display: none;
}

.nav-item.active .nav-icon-filled {
  display: block;
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
  font-size: var(--text-caption);
  font-weight: 600;
  text-decoration: none;
}

.nav-add-circle {
  display: grid;
  place-items: center;
  width: 50px;
  height: 50px;
  margin-top: -26px;
  border: 3px solid var(--color-bg-surface);
  border-radius: 50%;
  background: var(--color-brand);
  color: var(--color-on-brand);
  box-shadow: 0 6px 16px var(--color-shadow);
  transition: background var(--motion-fast) ease, transform var(--motion-fast) ease;
}

.nav-add-circle :deep(.app-icon) {
  width: 24px;
  height: 24px;
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
