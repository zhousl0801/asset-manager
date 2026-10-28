<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { fetchPlanOverview } from '../api/plans'
import AppNav from '../components/AppNav.vue'

const planCount = ref<number | null>(null)

onMounted(async () => {
  try {
    const overview = await fetchPlanOverview()
    planCount.value = overview.activeCount
  } catch {
    planCount.value = null
  }
})
</script>

<template>
  <main class="page mine-page with-nav">
    <header class="mine-header">
      <h1>我的</h1>
      <p>个人账本与记录设置</p>
    </header>

    <div class="list-box">
      <RouterLink class="list-row" to="/plans">
        <span>自动记账计划</span>
        <b>{{ planCount === null ? '查看 ›' : `${planCount} 项 ›` }}</b>
      </RouterLink>
      <RouterLink class="list-row" to="/categories">
        <span>收支分类</span>
        <b>管理 ›</b>
      </RouterLink>
      <RouterLink class="list-row" to="/quotes">
        <span>行情更新状态</span>
        <b>查看 ›</b>
      </RouterLink>
      <RouterLink class="list-row" to="/security">
        <span>登录与安全</span>
        <b>管理 ›</b>
      </RouterLink>
      <RouterLink class="list-row" to="/backup">
        <span>数据导出与备份</span>
        <b>管理 ›</b>
      </RouterLink>
      <RouterLink class="list-row" to="/ledger">
        <span>账本明细</span>
        <b>查看 ›</b>
      </RouterLink>
    </div>

    <AppNav />
  </main>
</template>

<style scoped>
.mine-header {
  margin-bottom: 22px;
}

.mine-header h1 {
  margin: 0;
  font-size: var(--text-title1);
  font-weight: 600;
  letter-spacing: -0.6px;
}

.mine-header p {
  margin: 5px 0 0;
  color: var(--color-text-secondary);
  font-size: var(--text-footnote);
}

.list-box {
  overflow: hidden;
  border: 1px solid var(--color-border-subtle);
  border-radius: var(--radius-card);
  background: var(--color-bg-surface);
}

.list-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
  box-sizing: border-box;
  min-height: 56px;
  padding: 0 16px;
  color: var(--color-text-primary);
  font-size: var(--text-subhead);
  text-decoration: none;
}

.list-row + .list-row {
  border-top: 1px solid var(--color-border-subtle);
}

.list-row:hover {
  background: var(--color-bg-hover);
}

.list-row b {
  color: var(--color-text-secondary);
  font-size: var(--text-footnote);
  font-weight: 400;
}
</style>
