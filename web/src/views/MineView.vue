<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { fetchPlanOverview } from '../api/plans'
import AppIcon from '../components/AppIcon.vue'
import AppNav from '../components/AppNav.vue'
import BottomSheet from '../components/BottomSheet.vue'
import { useTheme, type ThemeMode } from '../utils/theme'

const planCount = ref<number | null>(null)
const themeSheetOpen = ref(false)
const { mode, setThemeMode } = useTheme()
const themeOptions: { value: ThemeMode; label: string; description: string }[] = [
  { value: 'system', label: '跟随系统', description: '随设备的亮色 / 暗色外观自动切换' },
  { value: 'light', label: '亮色', description: '始终使用浅色界面' },
  { value: 'dark', label: '暗色', description: '始终使用深色界面' }
]
const themeLabel = computed(() =>
  themeOptions.find((option) => option.value === mode.value)?.label ?? '跟随系统')

function chooseTheme(value: ThemeMode): void {
  setThemeMode(value)
}

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
      <button type="button" class="list-row" @click="themeSheetOpen = true">
        <span>主题</span>
        <b>{{ themeLabel }} ›</b>
      </button>
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

    <BottomSheet
      :open="themeSheetOpen"
      title="主题"
      subtitle="选择亮色、暗色，或跟随设备外观"
      @close="themeSheetOpen = false"
    >
      <div class="theme-options" role="radiogroup" aria-label="主题">
        <button
          v-for="option in themeOptions"
          :key="option.value"
          type="button"
          role="radio"
          class="theme-option"
          :class="{ active: mode === option.value }"
          :aria-checked="mode === option.value"
          @click="chooseTheme(option.value)"
        >
          <span class="theme-option-copy">
            <strong>{{ option.label }}</strong>
            <small>{{ option.description }}</small>
          </span>
          <span class="theme-check" :class="{ checked: mode === option.value }">
            <AppIcon v-if="mode === option.value" name="check" />
          </span>
        </button>
      </div>
      <p class="theme-note">主题选择只保存在当前设备，不会改变账本数据。</p>
    </BottomSheet>

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
  width: 100%;
  min-height: 56px;
  padding: 0 16px;
  border: 0;
  background: transparent;
  color: var(--color-text-primary);
  font-size: var(--text-subhead);
  text-align: left;
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

.theme-options {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.theme-option {
  display: flex;
  align-items: center;
  gap: 12px;
  width: 100%;
  padding: 12px;
  border: 1px solid var(--color-border-subtle);
  border-radius: var(--radius-control);
  background: var(--color-bg-surface);
  color: var(--color-text-primary);
  text-align: left;
}

.theme-option.active {
  border-color: var(--color-brand);
  background: var(--color-brand-soft);
}

.theme-option-copy {
  flex: 1;
  min-width: 0;
}

.theme-option-copy strong {
  display: block;
  font-size: var(--text-footnote);
  font-weight: 600;
}

.theme-option-copy small {
  display: block;
  margin-top: 3px;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
  line-height: 1.5;
}

.theme-check {
  display: grid;
  flex: 0 0 auto;
  place-items: center;
  width: 22px;
  height: 22px;
  border: 1.5px solid var(--color-border-default);
  border-radius: 50%;
  color: var(--color-on-brand);
}

.theme-check.checked {
  border-color: var(--color-brand);
  background: var(--color-brand);
}

.theme-check .app-icon {
  width: 14px;
  height: 14px;
}

.theme-note {
  margin: 0;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
  line-height: 1.6;
}
</style>
