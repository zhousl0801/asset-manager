<template>
  <nav class="app-nav" aria-label="主导航">
    <RouterLink class="nav-item" exact-active-class="active" to="/">
      <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m3 10 9-7 9 7v10H3Z" /><path d="M9 20v-7h6v7" /></svg>
      <span>总览</span>
    </RouterLink>
    <RouterLink class="nav-item" active-class="active" to="/assets">
      <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M3 7h18v12H3z" /><path d="M3 7l2-3h14l2 3" /><path d="M16 13h2" /></svg>
      <span>资产</span>
    </RouterLink>
    <RouterLink class="nav-item" active-class="active" to="/ledger">
      <svg viewBox="0 0 24 24" aria-hidden="true"><rect x="5" y="3" width="15" height="18" rx="2" /><path d="M3 7h5M3 12h5M3 17h5M11 8h6M11 12h6" /></svg>
      <span>账本</span>
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
  display: flex;
  justify-content: space-around;
  align-items: flex-start;
  box-sizing: border-box;
  width: min(100%, 430px);
  height: calc(77px + env(safe-area-inset-bottom));
  padding: 5px 8px calc(11px + env(safe-area-inset-bottom));
  border-top: 1px solid var(--color-border-subtle);
  background: var(--color-bg-surface);
  transform: translateX(-50%);
}

.nav-item {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 6px;
  min-height: 48px;
  padding: 0 10px;
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
</style>
