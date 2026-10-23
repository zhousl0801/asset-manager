<script setup lang="ts">
import { storeToRefs } from 'pinia'
import { computed, onMounted, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import { fetchAssetTrend, fetchMonthChange } from '../api/ledger'
import { fetchPlanOverview } from '../api/plans'
import AppNav from '../components/AppNav.vue'
import AppIcon from '../components/AppIcon.vue'
import EmptyState from '../components/EmptyState.vue'
import AssetTrendChart from '../components/AssetTrendChart.vue'
import { useLedgerStore } from '../stores/ledger'
import { formatBusinessDate, formatMoney, formatSignedMoney } from '../utils/format'
import type { MonthChange } from '../types/ledger'
import type { AutoPlan, PlanOccurrence, PlanOverview, PlanType } from '../types/plan'
import type { AssetTrend, AssetTrendPoint, TrendRange } from '../types/trend'

interface AssetCard {
  key: string
  label: string
  icon: string
  amount: number | null
  note: string
  isConfigured: boolean
  to: { name: 'cash' | 'provident' | 'funds' | 'gold' }
}

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

const router = useRouter()
const ledgerStore = useLedgerStore()
const { overview, isLoading, errorMessage } = storeToRefs(ledgerStore)
const trend = ref<AssetTrend | null>(null)
const trendRange = ref<TrendRange>('month')
const trendSelection = ref<AssetTrendPoint | null>(null)
const trendLoading = ref(false)
const trendError = ref('')
const monthChange = ref<MonthChange | null>(null)
const monthChangeExpanded = ref(false)
const monthChangeError = ref('')
const planOverview = ref<PlanOverview | null>(null)
const planError = ref('')
const rangeOptions: { value: TrendRange; label: string }[] = [
  { value: 'month', label: '近1月' },
  { value: 'quarter', label: '近3月' },
  { value: 'year', label: '今年' },
  { value: 'all', label: '全部' }
]

const assetCards = computed<AssetCard[]>(() => {
  const ledger = overview.value

  if (!ledger) {
    return []
  }

  return [
    {
      key: 'cash',
      label: '现金',
      icon: 'cash',
      amount: ledger.cash.amount,
      note: ledger.cash.note ?? '统一余额',
      isConfigured: ledger.cash.isConfigured,
      to: { name: 'cash' }
    },
    {
      key: 'fund',
      label: '基金',
      icon: 'chart',
      amount: ledger.fund.amount,
      note: ledger.fund.note ?? '尚未录入',
      isConfigured: ledger.fund.isConfigured,
      to: { name: 'funds' }
    },
    {
      key: 'provident',
      label: '公积金',
      icon: 'home',
      amount: ledger.providentFund.amount,
      note: ledger.providentFund.note ?? '尚未录入',
      isConfigured: ledger.providentFund.isConfigured,
      to: { name: 'provident' }
    },
    {
      key: 'gold',
      label: '积存金',
      icon: 'gold',
      amount: ledger.gold.amount,
      note: ledger.gold.note ?? '尚未录入',
      isConfigured: ledger.gold.isConfigured,
      to: { name: 'gold' }
    }
  ]
})

const hasUnconfiguredAssets = computed(() => assetCards.value.some((asset) => !asset.isConfigured))

const trendLatest = computed<AssetTrendPoint | null>(() => {
  const points = trend.value?.points ?? []

  for (let index = points.length - 1; index >= 0; index -= 1) {
    const point = points[index]

    if (point && point.totalAssets !== null) {
      return point
    }
  }

  return points[points.length - 1] ?? null
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
      amount: items.reduce((total, item) => total + item.amount, 0)
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
      amount: plans.reduce((total, plan) => total + plan.amount, 0)
    })
  }

  return rows.sort((first, second) => first.date.localeCompare(second.date)).slice(0, 2)
})

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
    <header class="app-header">
      <div>
        <p class="app-name">资产管家</p>
        <p class="app-sub">我的财务总览</p>
      </div>
      <RouterLink class="icon-btn" to="/plans" aria-label="自动记账计划">
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <circle cx="12" cy="12" r="8" />
          <path d="M12 7v5l3 2" />
        </svg>
      </RouterLink>
    </header>

    <p v-if="isLoading" class="state">正在读取账本…</p>

    <section v-else-if="errorMessage" class="state">
      <p>{{ errorMessage }}</p>
      <button class="secondary-button" type="button" @click="loadLedger">重新加载</button>
    </section>

    <template v-else-if="overview">
      <section class="total">
        <p class="total-label">
          {{ overview.isComplete ? '总资产' : '部分资产合计' }}
          <span class="tag">{{ overview.currency }}</span>
        </p>
        <h1 class="total-value"><span class="currency">¥</span>{{ formatMoney(overview.totalAssets) }}</h1>
        <button
          v-if="monthChange?.isComplete"
          type="button"
          class="change"
          :aria-expanded="monthChangeExpanded"
          @click="monthChangeExpanded = !monthChangeExpanded"
        >
          <span>本月变化</span>
          <b>{{ formatSignedMoney(monthChange.totalChange) }}</b>
          <span class="change-toggle">{{ monthChangeExpanded ? '收起构成 ‹' : '· 查看构成 ›' }}</span>
        </button>
        <p v-else-if="monthChangeError" class="meta">{{ monthChangeError }}</p>
        <a v-else-if="monthChange" class="setup-hint" href="#asset-classes">补全资产，查看本月变化 <span>↓</span></a>
        <div v-if="monthChangeExpanded && monthChange?.isComplete" class="change-breakdown">
          <div><span>生活结余</span><b>{{ formatSignedMoney(monthChange.livingSurplus) }}</b></div>
          <div><span>公积金缴存</span><b>{{ formatSignedMoney(monthChange.providentDeposit) }}</b></div>
          <div><span>投资盈亏</span><b>{{ formatSignedMoney(monthChange.investmentProfit) }}</b></div>
          <div><span>核对差额</span><b>{{ formatSignedMoney(monthChange.reconciliationDifference) }}</b></div>
        </div>
        <p class="meta">
          统计截至 {{ formatBusinessDate(overview.asOfDate) }}
        </p>

      </section>

      <section class="chart-panel">
        <div class="section-head">
          <h2>总资产趋势</h2>
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
        <div class="chart-readout">
          <span>{{ (trendSelection ?? trendLatest) ? formatBusinessDate((trendSelection ?? trendLatest)!.date) : '按日重算' }}</span>
          <b>{{ (trendSelection ?? trendLatest)?.totalAssets != null ? `¥${formatMoney((trendSelection ?? trendLatest)!.totalAssets)}` : '依据不足' }}</b>
        </div>
        <p v-if="trendLoading && !trend" class="hint">正在读取趋势…</p>
        <p v-else-if="trendError" class="hint">{{ trendError }}</p>
        <AssetTrendChart v-else-if="trend && trend.points.length" :trend="trend" @select="trendSelection = $event" />
        <p v-else class="hint">还没有可展示的趋势数据。</p>
        <div class="chart-note">
          <span>点选日期查看历史总额</span>
          <details class="chart-help">
            <summary>统计口径 ⓘ</summary>
            <div class="chart-help-content">
              <p>账本起始 {{ formatBusinessDate(overview.startDate) }}；{{ trend ? `${trend.from} 至 ${trend.to}` : '按日重算' }}。</p>
              <p v-for="note in trend?.notes ?? []" :key="note">{{ note }}</p>
              <p>空白表示该日依据不足；虚线只表示已有依据的部分资产合计。</p>
              <p v-if="!overview.isComplete">{{ hasUnconfiguredAssets ? '部分资产尚未录入，可在资产分类中补充。' : '持仓日期或行情待确认，请前往历史核对。' }}</p>
            </div>
          </details>
        </div>
      </section>

      <div class="section-head">
        <h2 id="asset-classes">资产分类</h2>
        <span class="tiny">4 类资产</span>
      </div>
      <div class="asset-grid">
        <RouterLink
          v-for="asset in assetCards"
          :key="asset.key"
          :to="asset.to"
          class="asset-card"
          :class="{ unset: !asset.isConfigured }"
        >
          <div class="asset-head">
            <span>{{ asset.label }}</span>
            <span class="asset-mark"><AppIcon :name="asset.icon" /></span>
          </div>
          <b>{{ asset.isConfigured ? (asset.amount === null ? '依据不足' : formatMoney(asset.amount)) : '未录入' }}</b>
          <small>{{ asset.isConfigured ? asset.note : '点击补充 ›' }}</small>
        </RouterLink>
      </div>

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
  </main>
</template>

<style scoped>
.page {
  padding-top: 0;
}

.app-header {
  margin: 0 -19px 22px;
  padding: max(16px, env(safe-area-inset-top)) 19px 14px;
  border-bottom: 1px solid var(--color-border-subtle);
  background: var(--color-bg-surface);
}

.icon-btn {
  display: inline-flex;
  justify-content: center;
  align-items: center;
  width: 36px;
  height: 36px;
  border: 1px solid var(--color-border-subtle);
  border-radius: 9px;
  background: var(--color-bg-surface);
  color: var(--color-brand);
}

.icon-btn svg {
  width: 18px;
  height: 18px;
  fill: none;
  stroke: currentColor;
  stroke-width: 1.8;
  stroke-linecap: round;
  stroke-linejoin: round;
}

.icon-btn:hover {
  background: var(--color-bg-hover);
}

.total {
  padding: 0 3px 24px;
}

.total-label {
  display: flex;
  align-items: center;
  gap: 8px;
  margin: 0;
  color: var(--color-text-secondary);
  font-size: 12px;
}

.tag {
  padding: 2px 5px;
  border-radius: 4px;
  background: var(--color-bg-subtle);
  color: var(--color-text-secondary);
  font-size: 10px;
}

.total-value {
  margin: 10px 0 12px;
  font-size: 43px;
  font-weight: 550;
  letter-spacing: -1.7px;
  line-height: 1.25;
  font-variant-numeric: tabular-nums;
}

.total-value .currency {
  margin-right: 5px;
  font-size: 25px;
  font-weight: 400;
  letter-spacing: 0;
}

.change {
  display: inline-flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 5px;
  padding: 0;
  border: 0;
  border-radius: 4px;
  background: transparent;
  color: var(--color-text-secondary);
  font-size: 11px;
  line-height: 1.7;
}

.change b {
  color: var(--color-text-primary);
  font-weight: 550;
  font-variant-numeric: tabular-nums;
}

.change-toggle {
  color: var(--color-brand);
}

.change-breakdown {
  display: grid;
  gap: 8px;
  margin: 10px 0 2px;
  padding: 11px 12px;
  border-radius: 9px;
  background: var(--color-bg-subtle);
}

.change-breakdown > div {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 12px;
  color: var(--color-text-secondary);
  font-size: 11px;
}

.change-breakdown b {
  color: var(--color-text-primary);
  font-size: 12px;
  font-weight: 550;
  font-variant-numeric: tabular-nums;
}

.meta {
  margin: 0;
  color: var(--color-text-secondary);
  font-size: 11px;
  line-height: 1.7;
}

.chart-panel {
  margin-bottom: 24px;
  position: relative;
  padding: 17px 13px 11px;
  border: 1px solid var(--color-border-subtle);
  border-radius: 13px;
  background: var(--color-bg-surface);
}

.section-head {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 12px;
  margin-bottom: 12px;
}

.section-head h2 {
  margin: 0;
  font-size: 14px;
  font-weight: 600;
  letter-spacing: -0.2px;
}

.chart-panel .section-head h2 {
  font-size: 13px;
}

.range {
  display: flex;
  gap: 1px;
}

.range-button {
  min-height: 28px;
  padding: 5px 6px;
  border: 0;
  border-radius: 5px;
  background: transparent;
  color: var(--color-text-secondary);
  font-size: 10px;
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
  min-height: 24px;
  margin: 14px 1px 6px;
  color: var(--color-text-secondary);
  font-size: 10px;
}

.chart-readout b {
  color: var(--color-text-primary);
  font-size: 14px;
  font-weight: 550;
  font-variant-numeric: tabular-nums;
}

.chart-note {
  display: flex;
  justify-content: space-between;
  gap: 10px;
  margin-top: 7px;
  padding-top: 10px;
  border-top: 1px solid var(--color-border-subtle);
  color: var(--color-text-secondary);
  font-size: 10px;
}

.asset-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
  margin-bottom: 25px;
}

.asset-card {
  display: block;
  box-sizing: border-box;
  min-height: 111px;
  padding: 14px 13px 13px;
  border: 1px solid var(--color-border-subtle);
  border-radius: 12px;
  background: var(--color-bg-surface);
  color: inherit;
  text-decoration: none;
}

.asset-card:hover {
  border-color: var(--color-border-control);
}

.asset-card:active {
  border-color: var(--color-brand);
  background: var(--color-bg-selected);
}

.asset-head {
  display: flex;
  justify-content: space-between;
  align-items: center;
  color: var(--color-text-secondary);
  font-size: 12px;
}

.asset-mark .app-icon {
  width: 17px;
  height: 17px;
}

.asset-mark {
  display: inline-flex;
  justify-content: center;
  align-items: center;
  width: 25px;
  height: 25px;
  border-radius: 7px;
  background: var(--color-brand-soft);
  color: var(--color-brand);
  font-size: 12px;
  font-weight: 500;
}

.asset-card b {
  display: block;
  margin-top: 8px;
  font-size: 22px;
  font-weight: 550;
  letter-spacing: -0.5px;
  font-variant-numeric: tabular-nums;
}

.asset-card small {
  display: block;
  margin-top: 6px;
  color: var(--color-text-secondary);
  font-size: 10px;
  line-height: 1.6;
}

.asset-card.unset b {
  color: var(--color-text-secondary);
  font-size: 16px;
  font-weight: 500;
}

.text-btn {
  padding: 0;
  border: 0;
  background: transparent;
  color: var(--color-brand);
  font-size: 11px;
  text-decoration: none;
}

.text-btn:hover {
  color: var(--color-brand-hover);
  text-decoration: underline;
}

.month-card {
  margin-bottom: 25px;
  padding: 16px 13px 12px;
  border: 1px solid var(--color-border-subtle);
  border-radius: 12px;
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
  font-size: 11px;
}

.month-grid b {
  display: block;
  margin-top: 8px;
  font-size: 19px;
  font-weight: 550;
  font-variant-numeric: tabular-nums;
}

.income {
  color: var(--color-finance-income);
}

.card-foot {
  margin: 15px 0 0;
  padding-top: 11px;
  border-top: 1px solid var(--color-border-subtle);
  color: var(--color-text-secondary);
  font-size: 10px;
  line-height: 1.7;
}

.hint {
  margin: 12px 0 0;
  padding-top: 11px;
  border-top: 1px solid var(--color-border-subtle);
  color: var(--color-text-secondary);
  font-size: 10px;
  line-height: 1.7;
}

.month-card .hint {
  border-top: 0;
  padding-top: 0;
}

.plan-box {
  overflow: hidden;
  border: 1px solid var(--color-border-subtle);
  border-radius: 12px;
  background: var(--color-bg-surface);
}

.plan-row {
  display: flex;
  align-items: center;
  gap: 12px;
  box-sizing: border-box;
  padding: 14px 13px;
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
  border-radius: 7px;
  background: var(--color-brand-soft);
  color: var(--color-brand);
  font-size: 10px;
}

.date-box b {
  margin-top: 3px;
  font-size: 15px;
  font-weight: 550;
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
  font-weight: 550;
}

.plan-main small {
  color: var(--color-text-secondary);
  font-size: 10px;
  line-height: 1.6;
}

.plan-amount {
  color: var(--color-text-primary);
  font-size: 12px;
  font-weight: 550;
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}

.plan-note {
  margin: 8px 0 0;
  color: var(--color-text-secondary);
  font-size: 10px;
  line-height: 1.7;
}

.setup-hint {
  display: inline-flex;
  gap: 8px;
  align-items: center;
  min-height: 28px;
  margin-bottom: 2px;
  color: var(--color-brand);
  font-size: 12px;
  text-decoration: none;
}
.chart-help {
  position: static;
  color: var(--color-brand);
}
.chart-help summary {
  min-height: 28px;
  list-style: none;
  cursor: pointer;
}
.chart-help summary::-webkit-details-marker {
  display: none;
}
.chart-help-content {
  position: absolute;
  z-index: 10;
  top: calc(100% - 6px);
  right: 0;
  left: 0;
  padding: 12px 14px;
  border: 1px solid var(--color-border-subtle);
  border-radius: 12px;
  background: var(--color-bg-surface);
  color: var(--color-text-secondary);
  box-shadow: 0 8px 24px var(--color-shadow);
  font-size: 12px;
  line-height: 1.7;
}
.chart-help-content p {
  margin: 4px 0;
}

.tiny {
  color: var(--color-text-secondary);
  font-size: 11px;
}
</style>
