<script setup lang="ts">
import { storeToRefs } from 'pinia'
import { computed, onMounted, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import { fetchAssetTrend, fetchMonthChange } from '../api/ledger'
import { fetchPlanOverview } from '../api/plans'
import AppNav from '../components/AppNav.vue'
import AssetDistribution from '../components/AssetDistribution.vue'
import AssetTrendChart from '../components/AssetTrendChart.vue'
import BottomSheet from '../components/BottomSheet.vue'
import EmptyState from '../components/EmptyState.vue'
import { useLedgerStore } from '../stores/ledger'
import { formatBusinessDate, formatMoney, formatSignedMoney, sumAmounts, todayInBusinessTimeZone } from '../utils/format'
import { buildScopedTrend } from '../utils/trend'
import type { MonthChange } from '../types/ledger'
import type { AutoPlan, PlanOccurrence, PlanOverview, PlanType } from '../types/plan'
import type { AssetTrend, AssetTrendPoint, TrendRange } from '../types/trend'

interface PlanPreviewRow {
  key: string
  date: string
  title: string
  detail: string
  amount: number | null
}

const planTypeLabels: Record<PlanType, string> = {
  salaryIncome: '工资到账',
  providentDeposit: '公积金缴存',
  providentWithdrawal: '公积金提取',
  fundSip: '基金定投'
}
const weekdayLabels = ['一', '二', '三', '四', '五', '六', '日']
const rangeOptions: { value: TrendRange; label: string }[] = [
  { value: 'week', label: '7日' },
  { value: 'month', label: '1月' },
  { value: 'quarter', label: '3月' },
  { value: 'all', label: '全部' }
]

const router = useRouter()
const ledgerStore = useLedgerStore()
const { overview, isLoading, errorMessage } = storeToRefs(ledgerStore)
const today = todayInBusinessTimeZone()
const trend = ref<AssetTrend | null>(null)
const trendRange = ref<TrendRange>('month')
const trendSelection = ref<AssetTrendPoint | null>(null)
const trendLoading = ref(false)
const trendError = ref('')
const monthChange = ref<MonthChange | null>(null)
const monthChangeError = ref('')
const planOverview = ref<PlanOverview | null>(null)
const planError = ref('')
const changeSheetOpen = ref(false)

const recordedCount = computed(() => {
  const ledger = overview.value

  if (!ledger) {
    return 0
  }

  return [ledger.cash, ledger.providentFund, ledger.fund, ledger.gold]
    .filter((asset) => asset.isConfigured && !asset.notHeld)
    .length
})

const daysRecorded = computed(() => {
  if (!overview.value) {
    return 0
  }

  return Math.max(0, daysBetween(overview.value.startDate, today))
})

const trendReady = computed(() => daysRecorded.value >= 7)
const trendProgress = computed(() => Math.min(100, Math.round(daysRecorded.value / 7 * 100)))
const updateDate = computed(() => trend.value?.to ?? today)
const scopedTrend = computed(() => trend.value ? buildScopedTrend(trend.value) : null)
const selectedPoint = computed(() => {
  const scoped = scopedTrend.value

  if (!scoped) {
    return null
  }

  const date = trendSelection.value?.date

  return scoped.points.find((point) => point.date === date) ?? scoped.latest
})
const addedRecords = computed(() => monthChange.value?.addedRecords ?? 0)
const changePercent = computed(() => {
  const change = monthChange.value

  if (!change?.hasComparableData || change.comparableChange === null || !change.comparableBase) {
    return null
  }

  return change.comparableChange / change.comparableBase * 100
})

const planPreview = computed<PlanPreviewRow[]>(() => {
  const data = planOverview.value

  if (!data) {
    return []
  }

  const rows: PlanPreviewRow[] = []
  const pendingByDate = new Map<string, PlanOccurrence[]>()

  for (const occurrence of data.pendingOccurrences) {
    const items = pendingByDate.get(occurrence.dueDate) ?? []
    items.push(occurrence)
    pendingByDate.set(occurrence.dueDate, items)
  }

  for (const [date, items] of [...pendingByDate.entries()].sort(([first], [second]) => first.localeCompare(second))) {
    rows.push({
      key: `pending-${date}`,
      date,
      title: items.length > 1 ? `${items.length} 项待处理记账` : items[0]!.planName,
      detail: items.some((item) => item.status === 'failed') ? '执行失败，请及时处理' : '等待执行，请确认现金与资产余额',
      amount: sumAmounts(items.map(item => item.amount))
    })
  }

  const upcomingByDate = new Map<string, AutoPlan[]>()

  for (const plan of data.plans) {
    if (!plan.isActive || !plan.nextDueDate || pendingByDate.has(plan.nextDueDate)) {
      continue
    }

    const items = upcomingByDate.get(plan.nextDueDate) ?? []
    items.push(plan)
    upcomingByDate.set(plan.nextDueDate, items)
  }

  for (const [date, plans] of [...upcomingByDate.entries()].sort(([first], [second]) => first.localeCompare(second))) {
    rows.push({
      key: `plan-${date}`,
      date,
      title: plans.length > 1 ? `${plans.length} 项自动记账` : plans[0]!.name,
      detail: plans.map(describePlan).join('、'),
      amount: sumAmounts(plans.map(plan => plan.amount))
    })
  }

  return rows.sort((first, second) => first.date.localeCompare(second.date)).slice(0, 2)
})

function daysBetween(from: string, to: string): number {
  const start = Date.parse(`${from}T00:00:00Z`)
  const end = Date.parse(`${to}T00:00:00Z`)

  if (Number.isNaN(start) || Number.isNaN(end)) {
    return 0
  }

  return Math.round((end - start) / 86_400_000)
}

function describePlan(plan: AutoPlan): string {
  const label = planTypeLabels[plan.type]

  if (plan.cycle === 'weekly' && plan.dayOfWeek) {
    return `每周${weekdayLabels[plan.dayOfWeek - 1] ?? ''} · ${label}`
  }

  if (plan.dayOfMonth) {
    return `每月${plan.dayOfMonth}日 · ${label}`
  }

  return label
}

function monthLabel(date: string): string {
  return `${Number(date.slice(5, 7))}月`
}

function dayLabel(date: string): string {
  return date.slice(8, 10)
}

function percentLabel(value: number | null): string {
  if (value === null) {
    return ''
  }

  const sign = value > 0 ? '+' : ''

  return `${sign}${value.toFixed(2)}%`
}

function changeClass(value: number | null | undefined): string {
  if (value === null || value === undefined || value === 0) {
    return ''
  }

  return value > 0 ? 'gain' : 'loss'
}

function selectPoint(point: AssetTrendPoint): void {
  trendSelection.value = point
}

async function loadLedger(): Promise<void> {
  const status = await ledgerStore.load()

  if (status === 'missing') {
    await router.replace({ name: 'setup' })
    return
  }

  if (status === 'ready') {
    await Promise.all([loadTrend(trendRange.value), loadMonthChange(), loadPlans()])
  }
}

async function loadTrend(range: TrendRange): Promise<void> {
  trendRange.value = range
  trendSelection.value = null
  trendLoading.value = true
  trendError.value = ''

  try {
    trend.value = await fetchAssetTrend(range)
  } catch {
    trend.value = null
    trendError.value = '趋势暂时无法读取，请稍后重试。'
  } finally {
    trendSelection.value = null
    trendLoading.value = false
  }
}

async function loadMonthChange(): Promise<void> {
  monthChangeError.value = ''

  try {
    monthChange.value = await fetchMonthChange()
  } catch {
    monthChange.value = null
    monthChangeError.value = '本月变化暂时无法读取，请稍后重试。'
  }
}

async function loadPlans(): Promise<void> {
  planError.value = ''

  try {
    planOverview.value = await fetchPlanOverview()
  } catch {
    planOverview.value = null
    planError.value = '自动记账计划暂时无法读取，请稍后重试。'
  }
}

onMounted(loadLedger)
</script>

<template>
  <main class="page with-nav">
    <header class="home-header">
      <div>
        <p class="brand">资产管家</p>
        <h1>总览</h1>
      </div>
      <span class="header-date">{{ formatBusinessDate(updateDate) }}</span>
    </header>

    <div v-if="isLoading" class="skeleton-stack" role="status" aria-label="正在读取账本">
      <div class="skeleton skeleton-hero" />
      <div class="skeleton skeleton-card" />
      <div class="skeleton skeleton-row" />
      <div class="skeleton skeleton-row" />
    </div>

    <section v-else-if="errorMessage" class="state">
      <p>{{ errorMessage }}</p>
      <button class="secondary-button" type="button" @click="loadLedger">重新加载</button>
    </section>

    <template v-else-if="overview">
      <section class="hero">
        <p class="hero-label">
          {{ overview.coverageComplete ? '总资产' : '已记录资产' }}
          <span class="tag">{{ overview.currency }}</span>
        </p>
        <h1 class="hero-value"><span class="currency">¥</span>{{ formatMoney(overview.totalAssets) }}</h1>

        <button
          v-if="monthChange?.hasComparableData"
          type="button"
          class="hero-change"
          :aria-expanded="changeSheetOpen"
          @click="changeSheetOpen = true"
        >
          <span>本月变化</span>
          <b :class="changeClass(monthChange.comparableChange)">
            {{ formatSignedMoney(monthChange.comparableChange ?? 0) }}
          </b>
          <span v-if="changePercent !== null" class="change-percent" :class="changeClass(changePercent)">
            {{ percentLabel(changePercent) }}
          </span>
          <span class="info-mark">ⓘ</span>
        </button>
        <button
          v-else
          type="button"
          class="hero-change muted"
          :aria-expanded="changeSheetOpen"
          @click="changeSheetOpen = true"
        >
          <span>本月变化</span>
          <b>暂无可比数据</b>
          <span class="info-mark">ⓘ</span>
        </button>

        <p v-if="addedRecords > 0" class="added-note">
          含新增记录 {{ formatSignedMoney(addedRecords) }}，不计入变化
        </p>
        <p v-if="monthChangeError" class="meta">{{ monthChangeError }}</p>

        <p class="hero-meta">
          已记录 {{ recordedCount }} 类 · 更新于 {{ formatBusinessDate(updateDate) }}
        </p>
      </section>

      <section class="trend-panel">
        <div class="section-head">
          <h2>资产趋势</h2>
          <div class="range" role="tablist" aria-label="趋势范围">
            <button
              v-for="option in rangeOptions"
              :key="option.value"
              type="button"
              class="range-button"
              :class="{ active: trendRange === option.value }"
              :disabled="trendLoading"
              @click="loadTrend(option.value)"
            >
              {{ option.label }}
            </button>
          </div>
        </div>

        <template v-if="trendReady">
          <div class="chart-readout">
            <span>{{ selectedPoint ? formatBusinessDate(selectedPoint.date) : '按日重算' }}</span>
            <b :class="{ faint: !selectedPoint || selectedPoint.value === null }">
              {{ selectedPoint?.value == null ? '依据不足' : `¥${formatMoney(selectedPoint.value)}` }}
            </b>
          </div>
          <p v-if="trendLoading && !trend" class="hint">正在读取趋势…</p>
          <p v-else-if="trendError" class="hint">{{ trendError }}</p>
          <AssetTrendChart v-else-if="trend && scopedTrend" :trend="trend" @select="selectPoint" />
          <p v-else class="hint">还没有可展示的趋势数据。</p>
        </template>

        <div v-else class="trend-wait">
          <p>记录满 7 天后显示趋势</p>
          <div class="progress" role="progressbar" :aria-valuenow="daysRecorded" aria-valuemin="0" aria-valuemax="7">
            <span :style="{ width: `${trendProgress}%` }" />
          </div>
          <small>已记录 {{ Math.min(daysRecorded, 7) }}/7 天</small>
        </div>
      </section>

      <AssetDistribution
        :overview="overview"
        :changes="monthChange?.assetChanges"
        :comparable="monthChange?.hasComparableData ?? false"
        manage-to="/assets"
      />

      <div class="section-head">
        <h2>本月收支</h2>
        <RouterLink class="text-btn" :to="{ name: 'ledger' }">查看账本 ›</RouterLink>
      </div>
      <section class="month-card">
        <div class="month-grid">
          <div>
            <span>收入</span>
            <b :class="{ income: overview.month.hasRecords && overview.month.income > 0 }">
              {{ formatMoney(overview.month.income) }}
            </b>
          </div>
          <div>
            <span>支出</span>
            <b>{{ formatMoney(overview.month.expense) }}</b>
          </div>
          <div>
            <span>结余</span>
            <b>{{ formatMoney(overview.month.surplus) }}</b>
          </div>
        </div>
        <p v-if="!overview.month.hasRecords" class="hint">本月还没有收支记录。<RouterLink class="text-btn" :to="{ name: 'entry' }">去记一笔 ›</RouterLink></p>
        <p class="card-foot">公积金缴存、内部转账、投资买卖单独记录</p>
      </section>

      <div class="section-head">
        <h2>自动记账</h2>
        <RouterLink v-if="planPreview.length" class="text-btn" to="/plans">管理计划 ›</RouterLink>
      </div>
      <div v-if="planOverview" class="plan-box">
        <RouterLink v-for="row in planPreview" :key="row.key" class="plan-row" to="/plans">
          <span class="date-box">
            <small>{{ monthLabel(row.date) }}</small>
            <b>{{ dayLabel(row.date) }}</b>
          </span>
          <span class="plan-main">
            <b>{{ row.title }}</b>
            <small>{{ row.detail }}</small>
          </span>
          <span class="plan-amount">{{ row.amount === null ? '›' : `¥${formatMoney(row.amount)}` }}</span>
        </RouterLink>
        <EmptyState v-if="!planPreview.length" compact icon="clock" title="让固定收支自动记录" description="工资、缴存和定投，到期后按计划记账。" action-label="设置自动记账" @action="router.push('/plans')" />
      </div>
      <p v-else-if="planError" class="hint">{{ planError }}</p>
      <p v-if="planPreview.length" class="tiny plan-note">待处理计划会在这里提醒。</p>
    </template>

    <AppNav />

    <BottomSheet :open="changeSheetOpen" title="本月变化构成" @close="changeSheetOpen = false">
      <div v-if="monthChange" class="breakdown">
        <template v-if="monthChange.hasComparableData">
          <div class="breakdown-row">
            <span>市值变动</span>
            <b :class="changeClass(monthChange.marketValueChange)">{{ formatSignedMoney(monthChange.marketValueChange ?? 0) }}</b>
          </div>
          <div class="breakdown-row">
            <span>净存入·转账</span>
            <b :class="changeClass(monthChange.netDeposit)">{{ formatSignedMoney(monthChange.netDeposit ?? 0) }}</b>
          </div>
          <div class="breakdown-total">
            <span>本月变化</span>
            <b :class="changeClass(monthChange.comparableChange)">{{ formatSignedMoney(monthChange.comparableChange ?? 0) }}</b>
          </div>
        </template>
        <p v-else class="hint">账本起始日晚于本月月初，暂无可比基线。记录满一个完整月份后即可看到同口径变化。</p>
        <div v-if="addedRecords > 0" class="breakdown-row new-record">
          <span>新增记录（不计入变化）</span>
          <b>{{ formatSignedMoney(addedRecords) }}</b>
        </div>
        <p class="sheet-note">只比较月初和今天都已记录的类别；现金和公积金只叫余额变化，基金和积存金的市值变化才叫收益。</p>
      </div>
      <p v-else class="hint">{{ monthChangeError || '本月变化暂时无法读取。' }}</p>
    </BottomSheet>
  </main>
</template>

<style scoped>
.page {
  padding-top: 0;
}

.home-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-end;
  gap: 12px;
  margin: 0 calc(var(--page-gutter) * -1) 18px;
  padding: max(12px, env(safe-area-inset-top)) var(--page-gutter) 14px;
  border-bottom: 1px solid var(--color-border-subtle);
  background: var(--color-bg-surface);
}

.brand {
  margin: 0 0 2px;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
  font-weight: 500;
  letter-spacing: 0.2px;
}

.home-header h1 {
  margin: 0;
  font-size: var(--text-large-title);
  font-weight: 600;
  letter-spacing: -0.8px;
  line-height: var(--leading-tight);
}

.header-date {
  padding-bottom: 4px;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
}

.hero {
  padding: 4px 3px 22px;
}

.hero-label {
  display: flex;
  align-items: center;
  gap: 8px;
  margin: 0;
  color: var(--color-text-secondary);
  font-size: var(--text-footnote);
}

.tag {
  padding: 2px 5px;
  border-radius: var(--radius-tag);
  background: var(--color-bg-subtle);
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
}

.hero-value {
  margin: 8px 0 12px;
  font-size: clamp(34px, 10.5vw, 44px);
  font-weight: 600;
  letter-spacing: -1.4px;
  line-height: 1.2;
  font-variant-numeric: tabular-nums;
}

.hero-value .currency {
  margin-right: 5px;
  font-size: 24px;
  font-weight: 400;
  letter-spacing: 0;
}

.hero-change {
  display: inline-flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
  min-height: var(--touch-target);
  padding: 6px 12px;
  border: 1px solid var(--color-border-subtle);
  border-radius: var(--radius-pill);
  background: var(--color-bg-surface);
  color: var(--color-text-secondary);
  font-size: var(--text-footnote);
  line-height: 1.4;
}

.hero-change b {
  color: var(--color-text-primary);
  font-weight: 600;
  font-variant-numeric: tabular-nums;
}

.hero-change.muted b {
  color: var(--color-text-secondary);
  font-weight: 500;
}

.change-percent {
  font-variant-numeric: tabular-nums;
}

.info-mark {
  color: var(--color-text-secondary);
  font-size: var(--text-footnote);
}

.added-note {
  margin: 4px 0 0;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
}

.hero-meta {
  margin: 8px 0 0;
  color: var(--color-text-secondary);
  font-size: var(--text-footnote);
  line-height: 1.7;
}

.meta {
  margin: 4px 0 0;
  color: var(--color-text-secondary);
  font-size: var(--text-footnote);
}

b.gain {
  color: var(--color-finance-gain);
}

b.loss {
  color: var(--color-finance-loss);
}

.trend-panel {
  position: relative;
  margin-bottom: 20px;
  padding: 15px 13px 11px;
  border: 1px solid var(--color-border-subtle);
  border-radius: var(--radius-card);
  background: var(--color-bg-surface);
}

.section-head {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 12px;
  margin-bottom: 10px;
}

.section-head h2 {
  margin: 0;
  font-size: var(--text-headline);
  font-weight: 600;
  letter-spacing: -0.2px;
}

.trend-panel .section-head h2 {
  font-size: var(--text-headline);
}

.range {
  display: flex;
  gap: 1px;
}

.range-button {
  min-height: var(--touch-target);
  padding: 5px 6px;
  border: 0;
  border-radius: var(--radius-tag);
  background: transparent;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
}

.range-button:hover {
  background: var(--color-bg-hover);
  color: var(--color-brand);
}

.range-button.active {
  background: var(--color-brand-soft);
  color: var(--color-brand);
  font-weight: 600;
}

.range-button:disabled {
  opacity: 0.6;
}

.chart-readout {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 10px;
  min-height: 22px;
  margin: 10px 1px 2px;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
}

.chart-readout b {
  color: var(--color-text-primary);
  font-size: 13px;
  font-weight: 500;
  font-variant-numeric: tabular-nums;
}

.chart-readout b.faint {
  color: var(--color-text-secondary);
  font-size: var(--text-footnote);
  font-weight: 500;
}

.trend-wait {
  padding: 14px 4px 16px;
  color: var(--color-text-secondary);
  font-size: var(--text-footnote);
}

.trend-wait p {
  margin: 0 0 10px;
  color: var(--color-text-primary);
  font-size: 12px;
  font-weight: 500;
}

.trend-wait small {
  display: block;
  margin-top: 7px;
  font-size: var(--text-caption);
}

.progress {
  overflow: hidden;
  height: 5px;
  border-radius: var(--radius-pill);
  background: var(--color-bg-subtle);
}

.progress span {
  display: block;
  height: 100%;
  border-radius: var(--radius-pill);
  background: var(--color-brand);
  transition: width 0.2s ease;
}

.text-btn {
  padding: 0;
  border: 0;
  background: transparent;
  color: var(--color-brand);
  font-size: var(--text-footnote);
  text-decoration: none;
}

.text-btn:hover {
  color: var(--color-brand-hover);
  text-decoration: underline;
}

.month-card {
  margin-bottom: 22px;
  padding: 15px 13px 11px;
  border: 1px solid var(--color-border-subtle);
  border-radius: var(--radius-card);
  background: var(--color-bg-surface);
}

.month-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 10px;
}

.month-grid > div + div {
  padding-left: 12px;
  border-left: 1px solid var(--color-border-subtle);
}

.month-grid span {
  display: block;
  color: var(--color-text-secondary);
  font-size: var(--text-footnote);
}

.month-grid b {
  display: block;
  margin-top: 7px;
  font-size: 18px;
  font-weight: 500;
  font-variant-numeric: tabular-nums;
}

.income {
  color: var(--color-finance-income);
}

.card-foot {
  margin: 13px 0 0;
  padding-top: 10px;
  border-top: 1px solid var(--color-border-subtle);
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
  line-height: 1.7;
}

.hint {
  margin: 12px 0 0;
  padding-top: 11px;
  border-top: 1px solid var(--color-border-subtle);
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
  line-height: 1.7;
}

.month-card .hint {
  border-top: 0;
  padding-top: 0;
}

.plan-box {
  overflow: hidden;
  border: 1px solid var(--color-border-subtle);
  border-radius: var(--radius-card);
  background: var(--color-bg-surface);
}

.plan-row {
  display: flex;
  align-items: center;
  gap: 12px;
  box-sizing: border-box;
  padding: 13px;
  color: inherit;
  text-decoration: none;
}

.plan-row + .plan-row {
  border-top: 1px solid var(--color-border-subtle);
}

.plan-row:hover {
  background: var(--color-bg-hover);
}

.date-box {
  display: flex;
  flex-direction: column;
  align-items: center;
  min-width: 40px;
  padding: 7px 8px;
  border-radius: var(--radius-tag);
  background: var(--color-brand-soft);
  color: var(--color-brand);
  font-size: var(--text-caption);
}

.date-box b {
  margin-top: 3px;
  font-size: 15px;
  font-weight: 500;
  font-variant-numeric: tabular-nums;
}

.plan-main {
  display: flex;
  flex: 1;
  flex-direction: column;
  gap: 5px;
  min-width: 0;
}

.plan-main b {
  font-size: 12px;
  font-weight: 500;
}

.plan-main small {
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
  line-height: 1.6;
}

.plan-amount {
  color: var(--color-text-primary);
  font-size: 12px;
  font-weight: 500;
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}

.plan-note {
  margin: 8px 0 0;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
  line-height: 1.7;
}

.tiny {
  color: var(--color-text-secondary);
  font-size: var(--text-footnote);
}

.breakdown {
  display: grid;
  gap: 10px;
}

.breakdown-row,
.breakdown-total {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 12px;
  color: var(--color-text-secondary);
  font-size: 12px;
}

.breakdown-row b,
.breakdown-total b {
  color: var(--color-text-primary);
  font-size: 13px;
  font-weight: 500;
  font-variant-numeric: tabular-nums;
}

.breakdown-total {
  margin-top: 2px;
  padding-top: 10px;
  border-top: 1px solid var(--color-border-subtle);
  color: var(--color-text-primary);
}

.new-record {
  color: var(--color-text-secondary);
}

.new-record b {
  color: var(--color-text-secondary);
  font-weight: 500;
}

.sheet-note {
  margin: 6px 0 0;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
  line-height: 1.7;
}
</style>
