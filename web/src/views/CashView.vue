<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { fetchCashOverview, reconcileCash } from '../api/cash'
import { ApiError, getApiErrorMessage } from '../api/client'
import AppNav from '../components/AppNav.vue'
import { formatBusinessDate, formatMoney, formatSignedMoney, parseAmount, subtractAmounts } from '../utils/format'
import { formatPeriodLabel } from '../utils/period'
import type { CashActivity, CashActivityType, CashOverview } from '../types/cash'
import { createRequestId } from '../utils/request'
import type { EntryKind } from '../types/entry'

type CashFilter = 'all' | CashActivityType

interface ActivityGroup {
  date: string
  activities: CashActivity[]
}

const router = useRouter()
const overview = ref<CashOverview | null>(null)
const isLoading = ref(false)
const errorMessage = ref('')
const notice = ref('')
const filter = ref<CashFilter>('all')
const isReconcileOpen = ref(false)
const actualBalance = ref('')
const reason = ref('')
const formError = ref('')
const isSaving = ref(false)
const requestId = ref(createRequestId())
const expectedVersion = ref(0)
const actualBalanceInput = ref<HTMLInputElement | null>(null)

const filters: Array<{ value: CashFilter; label: string }> = [
  { value: 'all', label: '全部' },
  { value: 'daily', label: '日常收支' },
  { value: 'assetTransfer', label: '资产流转' },
  { value: 'adjustment', label: '余额调整' }
]

const monthLabel = computed(() =>
  overview.value ? formatPeriodLabel(overview.value.from, 'month') : '')
const filteredActivities = computed(() => {
  const activities = overview.value?.activities ?? []

  if (filter.value === 'all') {
    return activities
  }

  return activities.filter((activity) => activity.type === filter.value)
})
const groupedActivities = computed<ActivityGroup[]>(() => {
  const groups = new Map<string, CashActivity[]>()

  for (const activity of filteredActivities.value) {
    const activities = groups.get(activity.businessDate) ?? []
    activities.push(activity)
    groups.set(activity.businessDate, activities)
  }

  return [...groups.entries()].map(([date, activities]) => ({ date, activities }))
})
const previewDifference = computed(() => {
  if (!overview.value) {
    return null
  }

  const amount = parseAmount(actualBalance.value)

  return amount === null ? null : subtractAmounts(amount, overview.value.balance)
})
const isBalanced = computed(() => previewDifference.value === 0)
const canSave = computed(() =>
  previewDifference.value !== null && !isSaving.value)

onMounted(async () => {
  document.addEventListener('keydown', handleKeydown)
  await loadCash()
})

onBeforeUnmount(() => {
  document.removeEventListener('keydown', handleKeydown)
})

async function loadCash(): Promise<void> {
  isLoading.value = true
  errorMessage.value = ''

  try {
    overview.value = await fetchCashOverview()
  } catch (error) {
    overview.value = null

    if (error instanceof ApiError && error.status === 404) {
      await router.replace({ name: 'setup' })
      return
    }

    errorMessage.value = getApiErrorMessage(error, '现金数据加载失败，请重试。')
  } finally {
    isLoading.value = false
  }
}

function openEntry(kind: EntryKind): void {
  void router.push({
    name: 'entry',
    query: { kind, returnTo: 'cash' }
  })
}

function openActivity(activity: CashActivity): void {
  if (activity.type === 'daily') {
    void router.push({
      name: 'entry',
      query: { id: activity.id, returnTo: 'cash' }
    })
    return
  }

  if (activity.type !== 'assetTransfer' || !activity.assetType) {
    return
  }

  const routeName = activity.assetType === 'providentFund'
    ? 'provident'
    : activity.assetType === 'fund' ? 'funds' : 'gold'

  void router.push({ name: routeName })
}

function activityMark(activity: CashActivity): string {
  if (activity.type === 'adjustment') {
    return '调'
  }

  if (activity.type === 'assetTransfer') {
    return '转'
  }

  return activity.amount > 0 ? '入' : '出'
}

function activityNote(activity: CashActivity): string {
  const parts: string[] = []

  if (activity.note) {
    parts.push(activity.note)
  }

  if (activity.source === 'automatic') {
    parts.push('自动记账')
  }

  if (parts.length === 0) {
    return activity.type === 'daily' ? '日常收支' : '资产流转'
  }

  return parts.join(' · ')
}

function activityAmountClass(activity: CashActivity): string {
  if (activity.type === 'adjustment' || activity.type === 'assetTransfer') {
    return 'transfer'
  }

  return activity.amount > 0 ? 'income' : ''
}

function formatDay(date: string): string {
  const parts = date.split('-')

  if (parts.length !== 3) {
    return date
  }

  return `${Number(parts[1])}月${Number(parts[2])}日`
}

function openReconcile(): void {
  if (!overview.value) {
    return
  }

  requestId.value = createRequestId()
  expectedVersion.value = overview.value.version
  actualBalance.value = overview.value.balance.toFixed(2)
  reason.value = ''
  formError.value = ''
  isReconcileOpen.value = true

  void nextTick(() => {
    actualBalanceInput.value?.focus()
    actualBalanceInput.value?.select()
  })
}

function closeReconcile(): void {
  if (!isSaving.value) {
    isReconcileOpen.value = false
  }
}

async function submitReconcile(): Promise<void> {
  const amount = parseAmount(actualBalance.value)

  if (amount === null) {
    formError.value = '请输入不小于 0、最多两位小数的实际余额。'
    return
  }

  if (previewDifference.value !== 0 && !reason.value.trim()) {
    formError.value = '存在差额时，请填写调整原因。'
    return
  }

  if (isSaving.value) {
    return
  }

  isSaving.value = true
  formError.value = ''

  try {
    const result = await reconcileCash({
      requestId: requestId.value,
      expectedVersion: expectedVersion.value,
      actualBalance: amount,
      reason: reason.value.trim()
    })

    isReconcileOpen.value = false
    notice.value = result.isBalanced
      ? '核对一致，无需调整。'
      : `余额已调整为 ¥${formatMoney(result.cashBalance)}，生活收支保持不变。`
    await loadCash()
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) {
      await router.replace({ name: 'setup' })
      return
    }

    if (error instanceof ApiError && error.status === 409) {
      await loadCash()
      expectedVersion.value = overview.value?.version ?? expectedVersion.value
      requestId.value = createRequestId()
    }
    formError.value = getApiErrorMessage(error, '核对失败，请重试。')
  } finally {
    isSaving.value = false
  }
}

function handleKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape' && isReconcileOpen.value) {
    event.preventDefault()
    closeReconcile()
  }
}
</script>

<template>
  <main class="page cash-page with-nav">
    <header class="cash-header">
      <button type="button" class="back-link" @click="router.push({ name: 'home' })">‹ 总览</button>
      <h1>现金</h1>
      <button type="button" class="reconcile-link" @click="openReconcile">核对余额</button>
    </header>
    <RouterLink class="back-link history-entry-link" to="/history">历史核对与资产回看 ›</RouterLink>

    <p v-if="isLoading && !overview" class="state">正在读取现金…</p>

    <section v-else-if="errorMessage" class="state">
      <p>{{ errorMessage }}</p>
      <button type="button" class="secondary-button" @click="loadCash">重新加载</button>
    </section>

    <template v-else-if="overview">
      <section class="cash-hero">
        <p class="hero-label">
          可用现金
          <span class="hero-tag">统一余额</span>
        </p>
        <h2 class="hero-balance"><span>¥</span>{{ formatMoney(overview.balance) }}</h2>
        <p class="hero-meta">
          截至 {{ formatBusinessDate(overview.asOfDate) }} · 不区分银行卡与支付平台
        </p>
        <div class="hero-metrics">
          <div>
            <span>本月现金流入</span>
            <b class="income">{{ formatMoney(overview.inflow) }}</b>
          </div>
          <div>
            <span>本月现金流出</span>
            <b>{{ formatMoney(overview.outflow) }}</b>
          </div>
          <div>
            <span>余额净变动</span>
            <b>{{ formatSignedMoney(overview.netChange) }}</b>
          </div>
        </div>
      </section>

      <div class="cash-actions">
        <button type="button" class="action-primary" @click="openEntry('expense')">记支出</button>
        <button type="button" class="action-secondary" @click="openEntry('income')">记收入</button>
      </div>

      <p v-if="notice" class="notice" role="status">{{ notice }}</p>

      <p class="cash-note">现金流包含资产流转。生活收支统计仍以账本为准。</p>

      <div class="section-head">
        <h2>现金变动</h2>
        <span class="meta">{{ monthLabel }}</span>
      </div>

      <div class="filter-tabs" role="tablist" aria-label="现金变动类型">
        <button
          v-for="item in filters"
          :key="item.value"
          type="button"
          role="tab"
          :aria-selected="filter === item.value"
          :class="{ active: filter === item.value }"
          @click="filter = item.value"
        >
          {{ item.label }}
        </button>
      </div>

      <section v-for="group in groupedActivities" :key="group.date" class="day-group">
        <header class="day-header">
          <span>{{ formatDay(group.date) }}</span>
          <span>{{ group.activities.length }} 笔</span>
        </header>
        <div class="day-card">
          <component
            :is="activity.type === 'daily' || activity.type === 'assetTransfer' ? 'button' : 'div'"
            v-for="activity in group.activities"
            :key="activity.id"
            :type="activity.type === 'daily' || activity.type === 'assetTransfer' ? 'button' : undefined"
            class="activity-row"
            :class="{ clickable: activity.type === 'daily' || activity.type === 'assetTransfer' }"
            @click="openActivity(activity)"
          >
            <span class="activity-mark">{{ activityMark(activity) }}</span>
            <span class="activity-copy">
              <strong>{{ activity.title }}</strong>
              <small>{{ activityNote(activity) }}</small>
            </span>
            <b class="activity-value" :class="activityAmountClass(activity)">
              {{ formatSignedMoney(activity.amount) }}
            </b>
          </component>
        </div>
      </section>

      <div v-if="!filteredActivities.length" class="empty-state">
        <p>{{ filter === 'all' ? '本月还没有现金变动' : '暂无这类变动' }}</p>
        <button
          v-if="filter === 'all'"
          type="button"
          class="primary-button"
          @click="openEntry('expense')"
        >
          记一笔支出
        </button>
      </div>

      <p class="summary-note">
        月初余额 {{ formatMoney(overview.openingBalance) }} · 现金流入 − 现金流出 + 余额调整 =
        净变动
      </p>
      <p class="summary-note">余额调整单独留痕，不自动补成收入或支出。</p>
    </template>

    <Teleport to="body">
      <div
        v-if="isReconcileOpen && overview"
        class="sheet-backdrop"
        @click.self="closeReconcile"
      >
        <section
          class="sheet"
          role="dialog"
          aria-modal="true"
          aria-labelledby="reconcile-title"
        >
          <header class="sheet-head">
            <div>
              <h2 id="reconcile-title">核对现金余额</h2>
              <p>按实际可用现金修正差额</p>
            </div>
            <button type="button" class="sheet-close" :disabled="isSaving" @click="closeReconcile">
              关闭
            </button>
          </header>

          <div class="sheet-body">
            <div class="balance-row">
              <span>当前账面余额</span>
              <b>¥{{ formatMoney(overview.balance) }}</b>
            </div>

            <label class="field">
              <span>核对后的实际余额（元）</span>
              <input
                ref="actualBalanceInput"
                v-model="actualBalance"
                type="text"
                inputmode="decimal"
                autocomplete="off"
                placeholder="0.00"
                @input="formError = ''"
              />
            </label>

            <label class="field">
              <span>调整原因</span>
              <input
                v-model="reason"
                type="text"
                maxlength="100"
                placeholder="例如：漏记一笔消费"
                @input="formError = ''"
              />
            </label>

            <div class="difference-row">
              <span>核对差额</span>
              <b :class="{ transfer: previewDifference !== null && !isBalanced }">
                {{ formatSignedMoney(previewDifference) }}
              </b>
            </div>

            <p class="sheet-note">
              {{ isBalanced ? '核对一致，不会生成调整记录。' : '只记录与当前余额的差额，不改变生活收支。' }}
            </p>

            <p v-if="formError" class="form-error" role="alert">{{ formError }}</p>

            <button
              type="button"
              class="primary-button"
              :disabled="!canSave"
              @click="submitReconcile"
            >
              {{ isSaving ? '保存中…' : '保存核对' }}
            </button>
          </div>
        </section>
      </div>
    </Teleport>
    <AppNav />
  </main>
</template>

<style scoped>
.cash-page {
  padding-bottom: calc(97px + env(safe-area-inset-bottom));
}

.cash-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 10px;
  margin-bottom: 16px;
}

.cash-header h1 {
  margin: 0;
  font-size: 19px;
  font-weight: 600;
  letter-spacing: -0.3px;
}

.back-link,
.reconcile-link {
  min-height: 36px;
  padding: 0 4px;
  border: 0;
  background: transparent;
  color: var(--color-brand);
  font-size: 12px;
}

.reconcile-link {
  font-weight: 550;
}

.cash-hero {
  padding: 19px 17px 17px;
  border-radius: 14px;
  background: var(--color-brand-soft);
}

.hero-label {
  display: flex;
  align-items: center;
  gap: 7px;
  margin: 0;
  color: var(--color-text-secondary);
  font-size: 11px;
}

.hero-tag {
  padding: 2px 5px;
  border: 1px solid var(--color-border-subtle);
  border-radius: 3px;
  background: var(--color-bg-surface);
  color: var(--color-brand);
  font-size: 9px;
}

.hero-balance {
  margin: 8px 0 7px;
  font-size: 38px;
  font-weight: 600;
  letter-spacing: -1.1px;
  line-height: 1.2;
  font-variant-numeric: tabular-nums;
}

.hero-balance span {
  margin-right: 5px;
  font-size: 19px;
  font-weight: 400;
  letter-spacing: 0;
}

.hero-meta {
  margin: 0;
  color: var(--color-text-secondary);
  font-size: 10px;
  line-height: 1.6;
}

.hero-metrics {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 10px;
  margin-top: 22px;
}

.hero-metrics > div + div {
  padding-left: 12px;
  border-left: 1px solid var(--color-border-subtle);
}

.hero-metrics span {
  display: block;
  color: var(--color-text-secondary);
  font-size: 10px;
}

.hero-metrics b {
  display: block;
  margin-top: 7px;
  font-size: 15px;
  font-weight: 550;
  letter-spacing: -0.3px;
  font-variant-numeric: tabular-nums;
}

.income {
  color: var(--color-finance-income);
}

.transfer {
  color: var(--color-finance-transfer);
}

.cash-actions {
  display: flex;
  gap: 10px;
  margin-top: 14px;
}

.cash-actions button {
  flex: 1;
  min-height: 44px;
  border-radius: 9px;
  font-size: 13px;
  font-weight: 550;
}

.action-primary {
  border: 1px solid var(--color-brand);
  background: var(--color-brand);
  color: var(--color-on-brand);
}

.action-primary:hover {
  background: var(--color-brand-hover);
  border-color: var(--color-brand-hover);
}

.action-secondary {
  border: 1px solid var(--color-border-control);
  background: var(--color-bg-surface);
  color: var(--color-brand);
}

.action-secondary:hover {
  background: var(--color-brand-soft);
}

.notice {
  margin: 14px 0 0;
  padding: 10px 12px;
  border-radius: 8px;
  background: var(--color-success-bg);
  color: var(--color-success);
  font-size: 12px;
  line-height: 1.7;
}

.cash-note {
  margin: 14px 0 22px;
  color: var(--color-text-secondary);
  font-size: 10px;
  line-height: 1.7;
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
  font-size: 14px;
  font-weight: 600;
  letter-spacing: -0.2px;
}

.meta {
  color: var(--color-text-secondary);
  font-size: 10px;
}

.filter-tabs {
  display: flex;
  gap: 5px;
  margin-bottom: 4px;
  overflow-x: auto;
  scrollbar-width: none;
}

.filter-tabs::-webkit-scrollbar {
  display: none;
}

.filter-tabs button {
  flex: 0 0 auto;
  min-height: 30px;
  padding: 0 11px;
  border: 1px solid var(--color-border-subtle);
  border-radius: 999px;
  background: var(--color-bg-surface);
  color: var(--color-text-secondary);
  font-size: 10px;
  white-space: nowrap;
}

.filter-tabs button.active {
  border-color: var(--color-brand);
  background: var(--color-brand-soft);
  color: var(--color-brand);
  font-weight: 600;
}

.day-group {
  margin-top: 16px;
}

.day-header {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 10px;
  padding: 0 2px 7px;
  color: var(--color-text-secondary);
  font-size: 10px;
}

.day-card {
  overflow: hidden;
  border: 1px solid var(--color-border-subtle);
  border-radius: 12px;
  background: var(--color-bg-surface);
}

.activity-row {
  display: flex;
  align-items: center;
  gap: 11px;
  box-sizing: border-box;
  width: 100%;
  padding: 13px;
  border: 0;
  border-bottom: 1px solid var(--color-border-subtle);
  background: transparent;
  color: var(--color-text-primary);
  text-align: left;
}

.activity-row:last-child {
  border-bottom: 0;
}

.activity-row.clickable:hover {
  background: var(--color-bg-hover);
}

.activity-mark {
  display: inline-flex;
  justify-content: center;
  align-items: center;
  flex: 0 0 auto;
  width: 33px;
  height: 33px;
  border-radius: 50%;
  background: var(--color-brand-soft);
  color: var(--color-brand);
  font-size: 12px;
  font-weight: 550;
}

.activity-copy {
  display: flex;
  flex-direction: column;
  gap: 3px;
  flex: 1;
  min-width: 0;
}

.activity-copy strong {
  overflow: hidden;
  font-size: 13px;
  font-weight: 500;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.activity-copy small {
  overflow: hidden;
  color: var(--color-text-secondary);
  font-size: 10px;
  line-height: 1.5;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.activity-value {
  flex: 0 0 auto;
  font-size: 14px;
  font-weight: 550;
  font-variant-numeric: tabular-nums;
}

.empty-state {
  margin-top: 16px;
  padding: 34px 16px;
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

.summary-note {
  margin: 14px 0 0;
  color: var(--color-text-secondary);
  font-size: 10px;
  line-height: 1.7;
  text-align: center;
}

.sheet-backdrop {
  position: fixed;
  inset: 0;
  z-index: 50;
  display: flex;
  justify-content: center;
  align-items: flex-end;
  background: var(--color-overlay);
}

.sheet {
  box-sizing: border-box;
  width: min(100%, 430px);
  max-height: 88dvh;
  overflow-y: auto;
  padding: 20px 19px calc(24px + env(safe-area-inset-bottom));
  border-radius: 18px 18px 0 0;
  background: var(--color-bg-surface);
  box-shadow: 0 -8px 30px var(--color-shadow);
}

.sheet-head {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 12px;
  margin-bottom: 18px;
}

.sheet-head h2 {
  margin: 0;
  font-size: 17px;
  font-weight: 600;
  letter-spacing: -0.3px;
}

.sheet-head p {
  margin: 5px 0 0;
  color: var(--color-text-secondary);
  font-size: 11px;
}

.sheet-close {
  min-height: 32px;
  padding: 0 6px;
  border: 0;
  background: transparent;
  color: var(--color-brand);
  font-size: 12px;
}

.sheet-body {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.balance-row,
.difference-row {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 12px;
  padding: 11px 12px;
  border-radius: 9px;
  background: var(--color-bg-subtle);
  color: var(--color-text-secondary);
  font-size: 12px;
}

.balance-row b,
.difference-row b {
  color: var(--color-text-primary);
  font-size: 15px;
  font-weight: 550;
  font-variant-numeric: tabular-nums;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 8px;
  color: var(--color-text-secondary);
  font-size: 12px;
}

.field input {
  box-sizing: border-box;
  width: 100%;
  min-height: 46px;
  padding: 0 12px;
  border: 1px solid var(--color-border-control);
  border-radius: 9px;
  background: var(--color-bg-surface);
  color: var(--color-text-primary);
  font-size: 16px;
}

.field input:hover {
  border-color: var(--color-border-hover);
}

.field input:focus-visible {
  border-color: var(--color-border-focus);
}

.sheet-note {
  margin: 0;
  padding: 10px 12px;
  border-radius: 8px;
  background: var(--color-bg-subtle);
  color: var(--color-text-secondary);
  font-size: 10px;
  line-height: 1.7;
}

.form-error {
  margin: 0;
  padding: 10px 12px;
  border-radius: 8px;
  background: var(--color-error-bg);
  color: var(--color-error);
  font-size: 12px;
  line-height: 1.7;
}

.primary-button {
  width: 100%;
}

@media (min-width: 600px) {
  .sheet-backdrop {
    align-items: center;
  }

  .sheet {
    border-radius: 16px;
  }
}
</style>
