<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { fetchEntryPeriod, fetchEntryStats } from '../api/entries'
import { fetchCategories } from '../api/categories'
import { ApiError, getApiErrorMessage } from '../api/client'
import AppNav from '../components/AppNav.vue'
import LedgerStatsChart from '../components/LedgerStatsChart.vue'
import { ExpenseCategories, IncomeCategories } from '../constants/categories'
import type { EntryCategory } from '../types/category'
import { formatBusinessDate, formatMoney, todayInBusinessTimeZone } from '../utils/format'
import { formatPeriodLabel, periodStart, shiftAnchor } from '../utils/period'
import type { EntryKind, EntryPeriod, EntryPeriodSummary, EntryStats, LedgerEntry } from '../types/entry'

const router = useRouter()
const today = todayInBusinessTimeZone()
const periodOptions: Array<{ value: EntryPeriod; label: string }> = [
  { value: 'day', label: '日' },
  { value: 'month', label: '月' },
  { value: 'year', label: '年' }
]

const period = ref<EntryPeriod>('month')
const anchor = ref(today)
const view = ref<'list' | 'chart'>('list')
const statsKind = ref<EntryKind>('expense')
const stats = ref<EntryStats | null>(null)
const statsLoading = ref(false)
const statsError = ref('')
const kindFilter = ref<'all' | EntryKind>('all')
const categoryFilter = ref('')
const search = ref('')
const summary = ref<EntryPeriodSummary | null>(null)
const categoryItems = ref<EntryCategory[]>([])
const isLoading = ref(false)
const errorMessage = ref('')
let searchTimer: number | undefined
let loadSequence = 0

const periodLabel = computed(() => formatPeriodLabel(anchor.value, period.value))
const canGoNext = computed(() =>
  periodStart(anchor.value, period.value) < periodStart(today, period.value))
const categoryOptions = computed(() => {
  const loaded = categoryItems.value
    .filter((category) => kindFilter.value === 'all' || category.kind === kindFilter.value)
    .map((category) => category.name)

  if (loaded.length) {
    return [...new Set(loaded)]
  }

  if (kindFilter.value === 'income') {
    return IncomeCategories
  }

  if (kindFilter.value === 'expense') {
    return ExpenseCategories
  }

  return [...ExpenseCategories, ...IncomeCategories]
})
const statsCategories = computed(() => {
  if (!stats.value) {
    return []
  }

  return statsKind.value === 'income' ? stats.value.incomeCategories : stats.value.expenseCategories
})
const statsTotal = computed(() => {
  if (!stats.value) {
    return 0
  }

  return statsKind.value === 'income' ? stats.value.income : stats.value.expense
})
const statsAverage = computed(() => {
  if (!stats.value) {
    return 0
  }

  return statsKind.value === 'income' ? stats.value.incomeDailyAverage : stats.value.expenseDailyAverage
})
const hasFilters = computed(() =>
  kindFilter.value !== 'all' || categoryFilter.value !== '' || search.value.trim() !== '')
const groupedEntries = computed(() => {
  const groups = new Map<string, LedgerEntry[]>()

  for (const entry of summary.value?.entries ?? []) {
    const entries = groups.get(entry.businessDate) ?? []
    entries.push(entry)
    groups.set(entry.businessDate, entries)
  }

  return [...groups.entries()].map(([date, entries]) => ({
    date,
    entries,
    income: entries
      .filter((entry) => entry.kind === 'income')
      .reduce((sum, entry) => sum + entry.amount, 0),
    expense: entries
      .filter((entry) => entry.kind === 'expense')
      .reduce((sum, entry) => sum + entry.amount, 0)
  }))
})

watch([period, anchor, kindFilter, categoryFilter], () => {
  void loadEntries()
})

watch([period, anchor, view], () => {
  if (view.value === 'chart') {
    void loadStats()
  }
})

watch(search, () => {
  window.clearTimeout(searchTimer)
  searchTimer = window.setTimeout(() => {
    void loadEntries()
  }, 300)
})

onMounted(() => {
  void loadCategories()
  void loadEntries()
})

function switchView(next: 'list' | 'chart'): void {
  view.value = next
}

async function loadStats(): Promise<void> {
  statsLoading.value = true
  statsError.value = ''

  try {
    stats.value = await fetchEntryStats(period.value, anchor.value)
  } catch (error) {
    stats.value = null
    statsError.value = getApiErrorMessage(error, '图表数据暂时无法读取，请稍后重试。')
  } finally {
    statsLoading.value = false
  }
}

function selectStatsCategory(category: string): void {
  kindFilter.value = statsKind.value
  categoryFilter.value = category
  view.value = 'list'
}

async function loadCategories(): Promise<void> {
  try {
    categoryItems.value = await fetchCategories()
  } catch {
    categoryItems.value = []
  }
}

onBeforeUnmount(() => {
  window.clearTimeout(searchTimer)
})

async function loadEntries(append: boolean = false): Promise<void> {
  const sequence = ++loadSequence
  isLoading.value = true
  errorMessage.value = ''

  try {
    const result = await fetchEntryPeriod({
      page: append ? (summary.value?.page ?? 0) + 1 : 1,
      period: period.value,
      anchor: anchor.value,
      kind: kindFilter.value === 'all' ? null : kindFilter.value,
      category: categoryFilter.value,
      search: search.value.trim()
    })

    if (sequence === loadSequence) {
      summary.value = append && summary.value
        ? { ...result, entries: [...summary.value.entries, ...result.entries] }
        : result
    }
  } catch (error) {
    if (sequence !== loadSequence) {
      return
    }

    summary.value = null

    if (error instanceof ApiError && error.status === 404) {
      await router.replace({ name: 'setup' })
      return
    }

    errorMessage.value = getApiErrorMessage(error, '账本加载失败，请重试。')
  } finally {
    if (sequence === loadSequence) {
      isLoading.value = false
    }
  }
}

function setKind(kind: 'all' | EntryKind): void {
  kindFilter.value = kind

  if (categoryFilter.value && !categoryOptions.value.includes(categoryFilter.value)) {
    categoryFilter.value = ''
  }
}

function clearFilters(): void {
  kindFilter.value = 'all'
  categoryFilter.value = ''
  search.value = ''
}

function setPeriod(nextPeriod: EntryPeriod): void {
  period.value = nextPeriod
}

function shiftPeriod(direction: -1 | 1): void {
  if (direction === 1 && !canGoNext.value) {
    return
  }

  anchor.value = shiftAnchor(anchor.value, period.value, direction)
}

function formatDayHeader(date: string): string {
  const weekdays = ['日', '一', '二', '三', '四', '五', '六']
  const day = new Date(`${date}T12:00:00`)

  return `${formatBusinessDate(date)} 星期${weekdays[day.getDay()]}`
}

function formatEntryAmount(entry: LedgerEntry): string {
  return `${entry.kind === 'income' ? '+' : '−'}${formatMoney(entry.amount)}`
}

function openEntry(entryId?: string): void {
  void router.push({
    name: 'entry',
    query: entryId ? { id: entryId } : {}
  })
}

function goBack(): void {
  void router.push({ name: 'home' })
}
</script>

<template>
  <main class="page ledger-page with-nav">
    <header class="ledger-header">
      <button type="button" class="back-link" @click="goBack">‹ 总览</button>
      <h1>账本</h1>
      <button type="button" class="add-button" @click="openEntry()">＋ 记一笔</button>
    </header>
    <RouterLink class="back-link history-entry-link" to="/history">历史核对与资产回看 ›</RouterLink>

    <div class="view-tabs" role="tablist" aria-label="账本视图">
      <button type="button" :class="{ active: view === 'list' }" @click="switchView('list')">明细</button>
      <button type="button" :class="{ active: view === 'chart' }" @click="switchView('chart')">图表</button>
    </div>

    <section class="period-panel">
      <div class="period-top">
        <div class="period-tabs" role="tablist" aria-label="账本周期">
          <button
            v-for="item in periodOptions"
            :key="item.value"
            type="button"
            :class="{ active: period === item.value }"
            @click="setPeriod(item.value)"
          >
            {{ item.label }}
          </button>
        </div>
        <div class="period-nav">
          <button type="button" aria-label="上一周期" @click="shiftPeriod(-1)">‹</button>
          <strong>{{ periodLabel }}</strong>
          <button
            type="button"
            aria-label="下一周期"
            :disabled="!canGoNext"
            @click="shiftPeriod(1)"
          >
            ›
          </button>
        </div>
      </div>
      <div class="summary-grid">
        <div>
          <span>收入</span>
          <b :class="{ income: (summary?.income ?? 0) > 0 }">{{ formatMoney(summary?.income) }}</b>
        </div>
        <div>
          <span>支出</span>
          <b>{{ formatMoney(summary?.expense) }}</b>
        </div>
        <div>
          <span>结余</span>
          <b>{{ formatMoney(summary?.surplus) }}</b>
        </div>
      </div>
    </section>

    <template v-if="view === 'list'">
    <div class="filter-bar">
      <div class="kind-tabs" role="tablist" aria-label="收支类型">
        <button
          type="button"
          :class="{ active: kindFilter === 'all' }"
          @click="setKind('all')"
        >
          全部
        </button>
        <button
          type="button"
          :class="{ active: kindFilter === 'expense' }"
          @click="setKind('expense')"
        >
          支出
        </button>
        <button
          type="button"
          :class="{ active: kindFilter === 'income' }"
          @click="setKind('income')"
        >
          收入
        </button>
      </div>
      <div class="filter-row">
        <input v-model="search" type="search" placeholder="搜索分类或备注" aria-label="搜索分类或备注" />
        <select v-model="categoryFilter" aria-label="分类筛选">
          <option value="">全部分类</option>
          <option v-for="category in categoryOptions" :key="category" :value="category">
            {{ category }}
          </option>
        </select>
      </div>
    </div>

    <p v-if="isLoading && !summary" class="state">正在读取记录…</p>

    <section v-else-if="errorMessage" class="state">
      <p>{{ errorMessage }}</p>
      <button type="button" class="secondary-button" @click="loadEntries()">重新加载</button>
    </section>

    <template v-else>
      <section v-for="group in groupedEntries" :key="group.date" class="day-group">
        <header class="day-header">
          <span>{{ formatDayHeader(group.date) }}</span>
          <span class="day-totals">
            <template v-if="summary && summary.entries.length < summary.totalCount">已显示小计 · </template>
            <template v-if="group.income">收入 {{ formatMoney(group.income) }}</template>
            <template v-if="group.expense">支出 {{ formatMoney(group.expense) }}</template>
          </span>
        </header>
        <div class="day-card">
          <button
            v-for="entry in group.entries"
            :key="entry.id"
            type="button"
            class="entry-row"
            @click="openEntry(entry.id)"
          >
            <span class="entry-mark">{{ entry.category.slice(0, 1) }}</span>
            <span class="entry-copy">
              <span class="entry-title">{{ entry.category }}</span>
              <small v-if="entry.note">{{ entry.note }}</small>
            </span>
            <b :class="{ income: entry.kind === 'income' }">{{ formatEntryAmount(entry) }}</b>
          </button>
        </div>
      </section>

      <div v-if="!groupedEntries.length" class="empty-state">
        <p>{{ hasFilters ? '没有符合条件的记录' : '这段时间还没有记录' }}</p>
        <button
          v-if="hasFilters"
          type="button"
          class="secondary-button"
          @click="clearFilters"
        >
          清除筛选
        </button>
        <button v-else type="button" class="primary-button" @click="openEntry()">记一笔</button>
      </div>

      <p class="list-note">{{ summary?.entries.length ?? 0 }} 笔日常收支</p>
    </template>
  <button v-if="summary && summary.entries.length < summary.totalCount" type="button" class="secondary-button" :disabled="isLoading" @click="loadEntries(true)">加载更多（已显示 {{ summary.entries.length }} / {{ summary.totalCount }}）</button>
    </template>

    <template v-else>
      <div class="stats-direction" role="tablist" aria-label="图表方向">
        <button type="button" :class="{ active: statsKind === 'expense' }" @click="statsKind = 'expense'">支出</button>
        <button type="button" :class="{ active: statsKind === 'income' }" @click="statsKind = 'income'">收入</button>
      </div>

      <p v-if="statsLoading && !stats" class="state">正在读取图表…</p>
      <section v-else-if="statsError" class="state">
        <p>{{ statsError }}</p>
        <button type="button" class="secondary-button" @click="loadStats">重新加载</button>
      </section>

      <template v-else-if="stats">
        <section class="stats-panel">
          <div class="stats-head">
            <h2>{{ statsKind === 'income' ? '收入' : '支出' }}</h2>
            <b>¥{{ formatMoney(statsTotal) }}</b>
          </div>
          <p class="stats-meta">
            日均 ¥{{ formatMoney(statsAverage) }} · 覆盖 {{ stats.coveredDays }} 天 · {{ periodLabel }}
          </p>
          <p v-if="stats.note" class="stats-note">{{ stats.note }}</p>
        </section>

        <section class="chart-card">
          <div class="stats-head">
            <h2>{{ statsKind === 'income' ? '收入趋势' : '支出趋势' }}</h2>
            <span class="tiny">{{ stats.period === 'day' ? '当天' : stats.period === 'month' ? '按日' : '按月' }}</span>
          </div>
          <p v-if="stats.period === 'day'" class="state-inline">日视图只显示当天合计，切换月或年可查看趋势。</p>
          <LedgerStatsChart
            v-else-if="stats.points.length"
            :points="stats.points"
            :direction="statsKind"
            :period="stats.period"
            :total="statsTotal"
          />
          <p v-else class="state-inline">该周期还没有记录。</p>
        </section>

        <section class="ranking-panel" :class="{ income: statsKind === 'income' }">
          <div class="stats-head">
            <h2>分类排行</h2>
            <span class="tiny">{{ statsCategories.length }} 类</span>
          </div>
          <button
            v-for="category in statsCategories"
            :key="category.category"
            type="button"
            class="rank-row"
            @click="selectStatsCategory(category.category)"
          >
            <span class="rank-top">
              <span>{{ category.category }}</span>
              <b>¥{{ formatMoney(category.amount) }}</b>
            </span>
            <span class="rank-bar"><span :style="{ width: `${Math.max(category.share, 1)}%` }" /></span>
            <small>{{ category.count }} 笔 · {{ category.share.toFixed(2) }}%</small>
          </button>
          <p v-if="!statsCategories.length" class="state-inline">该周期还没有{{ statsKind === 'income' ? '收入' : '支出' }}记录。</p>
        </section>
      </template>
    </template>
    <AppNav />
  </main>
</template>

<style scoped>
.ledger-page {
  padding-bottom: calc(97px + env(safe-area-inset-bottom));
}

.ledger-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 10px;
  margin-bottom: 18px;
}

.ledger-header h1 {
  margin: 0;
  font-size: 19px;
  font-weight: 600;
  letter-spacing: -0.3px;
}

.back-link,
.add-button {
  min-height: 36px;
  padding: 0 4px;
  border: 0;
  background: transparent;
  color: var(--color-brand);
  font-size: 12px;
}

.add-button {
  font-weight: 550;
}

.view-tabs {
  display: flex;
  gap: 2px;
  width: fit-content;
  margin-bottom: 14px;
  padding: 2px;
  border-radius: 8px;
  background: var(--color-bg-subtle);
}

.view-tabs button {
  min-height: 30px;
  padding: 0 16px;
  border: 0;
  border-radius: 6px;
  background: transparent;
  color: var(--color-text-secondary);
  font-size: 11px;
}

.view-tabs button.active {
  background: var(--color-bg-surface);
  color: var(--color-brand);
  font-weight: 600;
}

.stats-direction {
  display: flex;
  gap: 8px;
  margin-bottom: 14px;
}

.stats-direction button {
  min-height: 34px;
  padding: 0 16px;
  border: 1px solid var(--color-border-subtle);
  border-radius: 8px;
  background: var(--color-bg-surface);
  color: var(--color-text-secondary);
  font-size: 12px;
}

.stats-direction button.active {
  border-color: var(--color-brand);
  background: var(--color-brand-soft);
  color: var(--color-brand);
  font-weight: 550;
}

.stats-panel,
.chart-card,
.ranking-panel {
  margin-bottom: 14px;
  padding: 14px;
  border: 1px solid var(--color-border-subtle);
  border-radius: 12px;
  background: var(--color-bg-surface);
}

.stats-head {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 10px;
  margin-bottom: 8px;
}

.stats-head h2 {
  margin: 0;
  font-size: 14px;
  font-weight: 600;
}

.stats-head b {
  font-size: 22px;
  font-weight: 550;
  font-variant-numeric: tabular-nums;
}

.stats-meta {
  margin: 0;
  color: var(--color-text-secondary);
  font-size: 10px;
  line-height: 1.7;
}

.stats-note {
  margin: 8px 0 0;
  color: var(--color-warning);
  font-size: 10px;
  line-height: 1.6;
}

.state-inline {
  margin: 8px 0 4px;
  color: var(--color-text-secondary);
  font-size: 11px;
  line-height: 1.7;
}

.rank-row {
  display: block;
  box-sizing: border-box;
  width: 100%;
  padding: 9px 0;
  border: 0;
  border-top: 1px solid var(--color-border-subtle);
  background: transparent;
  color: inherit;
  text-align: left;
}

.rank-row:hover {
  background: var(--color-bg-hover);
}

.rank-top {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 10px;
  font-size: 12px;
}

.rank-top b {
  font-weight: 550;
  font-variant-numeric: tabular-nums;
}

.rank-bar {
  display: block;
  height: 5px;
  margin: 6px 0 4px;
  overflow: hidden;
  border-radius: 3px;
  background: var(--color-bg-subtle);
}

.rank-bar span {
  display: block;
  height: 100%;
  border-radius: 3px;
  background: var(--color-brand);
}

.ranking-panel.income .rank-bar span {
  background: var(--color-finance-income);
}

.rank-row small {
  color: var(--color-text-secondary);
  font-size: 9px;
}

.period-panel {
  margin-bottom: 14px;
  padding: 14px;
  border: 1px solid var(--color-border-subtle);
  border-radius: 12px;
  background: var(--color-bg-surface);
}

.period-top {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
  margin-bottom: 14px;
}

.period-tabs {
  display: flex;
  gap: 2px;
  padding: 2px;
  border-radius: 8px;
  background: var(--color-bg-subtle);
}

.period-tabs button {
  min-height: 30px;
  padding: 0 10px;
  border: 0;
  border-radius: 6px;
  background: transparent;
  color: var(--color-text-secondary);
  font-size: 11px;
}

.period-tabs button.active {
  background: var(--color-bg-surface);
  color: var(--color-brand);
  font-weight: 600;
}

.period-nav {
  display: flex;
  align-items: center;
  gap: 4px;
}

.period-nav button {
  width: 28px;
  height: 28px;
  border: 0;
  border-radius: 6px;
  background: transparent;
  color: var(--color-brand);
  font-size: 18px;
  line-height: 1;
}

.period-nav button:disabled {
  color: var(--color-text-disabled);
  cursor: not-allowed;
}

.period-nav strong {
  font-size: 13px;
  font-weight: 600;
  letter-spacing: -0.2px;
  white-space: nowrap;
}

.summary-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 10px;
}

.summary-grid > div + div {
  padding-left: 12px;
  border-left: 1px solid var(--color-border-subtle);
}

.summary-grid span {
  display: block;
  color: var(--color-text-secondary);
  font-size: 11px;
}

.summary-grid b {
  display: block;
  margin-top: 6px;
  font-size: 18px;
  font-weight: 550;
  font-variant-numeric: tabular-nums;
}

.income {
  color: var(--color-finance-income);
}

.filter-bar {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin-bottom: 16px;
}

.kind-tabs {
  display: flex;
  gap: 4px;
}

.kind-tabs button {
  min-height: 30px;
  padding: 0 12px;
  border: 1px solid var(--color-border-subtle);
  border-radius: 999px;
  background: var(--color-bg-surface);
  color: var(--color-text-secondary);
  font-size: 11px;
}

.kind-tabs button.active {
  border-color: var(--color-brand);
  background: var(--color-brand-soft);
  color: var(--color-brand);
  font-weight: 600;
}

.filter-row {
  display: flex;
  gap: 8px;
}

.filter-row input,
.filter-row select {
  box-sizing: border-box;
  min-height: 38px;
  padding: 0 10px;
  border: 1px solid var(--color-border-control);
  border-radius: 8px;
  background: var(--color-bg-surface);
  color: var(--color-text-primary);
  font-size: 13px;
}

.filter-row input {
  flex: 1;
  min-width: 0;
}

.filter-row select {
  max-width: 118px;
}

.day-group {
  margin-bottom: 16px;
}

.day-header {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 10px;
  padding: 0 2px 8px;
  color: var(--color-text-secondary);
  font-size: 10px;
}

.day-totals {
  display: inline-flex;
  gap: 10px;
}

.day-card {
  overflow: hidden;
  border: 1px solid var(--color-border-subtle);
  border-radius: 12px;
  background: var(--color-bg-surface);
}

.entry-row {
  display: flex;
  align-items: center;
  gap: 11px;
  width: 100%;
  padding: 12px 13px;
  border: 0;
  border-bottom: 1px solid var(--color-border-subtle);
  background: transparent;
  text-align: left;
}

.entry-row:last-child {
  border-bottom: 0;
}

.entry-row:hover {
  background: var(--color-bg-hover);
}

.entry-mark {
  display: inline-flex;
  justify-content: center;
  align-items: center;
  flex: 0 0 auto;
  width: 34px;
  height: 34px;
  border-radius: 50%;
  background: var(--color-brand-soft);
  color: var(--color-brand);
  font-size: 13px;
  font-weight: 550;
}

.entry-copy {
  display: flex;
  flex-direction: column;
  gap: 3px;
  flex: 1;
  min-width: 0;
}

.entry-title {
  font-size: 13px;
}

.entry-copy small {
  overflow: hidden;
  color: var(--color-text-secondary);
  font-size: 10px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.entry-row b {
  flex: 0 0 auto;
  font-size: 14px;
  font-weight: 550;
  font-variant-numeric: tabular-nums;
}

.empty-state {
  padding: 36px 16px;
  border: 1px dashed var(--color-border-default);
  border-radius: 12px;
  text-align: center;
}

.empty-state p {
  margin: 0 0 14px;
  color: var(--color-text-secondary);
  font-size: 12px;
}

.empty-state .primary-button {
  width: 100%;
}

.list-note {
  margin: 4px 0 0;
  color: var(--color-text-secondary);
  font-size: 10px;
  text-align: center;
}
</style>
