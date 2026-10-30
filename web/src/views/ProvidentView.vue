<script setup lang="ts">
import BackButton from '../components/BackButton.vue'
import CashAdjustmentLink from '../components/CashAdjustmentLink.vue'
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { depositProvident, fetchProvidentOverview, withdrawProvident, setProvidentOpening } from '../api/provident'
import { fetchPlanOverview, pausePlan, resumePlan, savePlan } from '../api/plans'
import { ApiError, getApiErrorMessage } from '../api/client'
import AppNav from '../components/AppNav.vue'
import BottomSheet from '../components/BottomSheet.vue'
import { amountTone, formatBusinessDate, formatMoney, formatSignedMoney, parseAmount, todayInBusinessTimeZone } from '../utils/format'
import { formatPeriodLabel } from '../utils/period'
import { createRequestId } from '../utils/request'
import type { PlanOverview } from '../types/plan'
import type { ProvidentActivity, ProvidentOverview } from '../types/provident'

type SheetKind = 'opening' | 'record'

const router = useRouter()
const today = todayInBusinessTimeZone()
const overview = ref<ProvidentOverview | null>(null)
const planOverview = ref<PlanOverview | null>(null)
const isLoading = ref(false)
const errorMessage = ref('')
const notice = ref('')
const sheet = ref<SheetKind | null>(null)
const requestId = ref(createRequestId())
const depositRequestId = ref(createRequestId())
const withdrawalRequestId = ref(createRequestId())
const isSaving = ref(false)
const formError = ref('')
const form = reactive({
  amount: '',
  depositAmount: '',
  withdrawalAmount: '',
  note: '',
  relatedCashAdjustmentId: null as string | null,
  monthlyEnabled: false,
  monthlyAmount: '',
  monthlyDay: '15',
  monthlyTime: '09:00'
})

const depositPlan = computed(() =>
  planOverview.value?.plans.find((plan) => plan.type === 'providentDeposit') ?? null)

const monthLabel = computed(() =>
  overview.value ? formatPeriodLabel(overview.value.periodFrom, 'month') : '')
const sheetTitle = computed(() =>
  sheet.value === 'opening' ? '录入公积金余额' : '缴存/提取')
const sheetNote = computed(() =>
  sheet.value === 'opening'
    ? '记录已有的公积金余额，不计入缴存、工资或生活收入。'
    : '缴存与提取可只填其中一项。')
const depositAmount = computed(() => parseAmount(form.depositAmount))
const withdrawalAmount = computed(() => parseAmount(form.withdrawalAmount))
const monthlyAmount = computed(() => parseAmount(form.monthlyAmount))
const monthlyDay = computed(() => Number(form.monthlyDay))
const monthlyValuesChanged = computed(() => {
  const plan = depositPlan.value

  if (!form.monthlyEnabled) {
    return false
  }

  if (!plan) {
    return true
  }

  return monthlyAmount.value !== plan.amount
    || monthlyDay.value !== plan.dayOfMonth
    || form.monthlyTime.slice(0, 5) !== plan.time.slice(0, 5)
})
const monthlyPlanChanged = computed(() => {
  const plan = depositPlan.value

  if (!form.monthlyEnabled) {
    return plan?.isActive ?? false
  }

  return !plan || !plan.isActive || monthlyValuesChanged.value
})
const monthlyValid = computed(() => !form.monthlyEnabled || (
  (monthlyAmount.value ?? 0) > 0
  && Number.isInteger(monthlyDay.value)
  && monthlyDay.value >= 1
  && monthlyDay.value <= 31
  && form.monthlyTime !== ''
))
const canSubmit = computed(() => {
  if (isSaving.value) {
    return false
  }

  if (sheet.value === 'opening') {
    const amount = parseAmount(form.amount)
    return amount !== null && amount >= 0
  }

  if (sheet.value !== 'record') {
    return false
  }

  if (!monthlyValid.value) {
    return false
  }

  const deposit = depositAmount.value
  const withdrawal = withdrawalAmount.value

  if ((deposit ?? 0) < 0 || (withdrawal ?? 0) < 0) {
    return false
  }

  return (deposit ?? 0) > 0 || (withdrawal ?? 0) > 0 || monthlyPlanChanged.value
})

onMounted(loadPage)

watch(
  () => form.monthlyEnabled,
  (enabled) => {
    if (enabled && !form.monthlyAmount.trim() && (depositAmount.value ?? 0) > 0) {
      form.monthlyAmount = form.depositAmount
    }
  }
)

async function loadPage(): Promise<void> {
  await Promise.all([loadOverview(), loadPlan()])
}

async function loadOverview(): Promise<void> {
  isLoading.value = true
  errorMessage.value = ''

  try {
    overview.value = await fetchProvidentOverview()
  } catch (error) {
    overview.value = null

    if (error instanceof ApiError && error.status === 404) {
      await router.replace({ name: 'setup' })
      return
    }

    errorMessage.value = getApiErrorMessage(error, '公积金数据加载失败，请重试。')
  } finally {
    isLoading.value = false
  }
}

async function loadPlan(): Promise<void> {
  try {
    planOverview.value = await fetchPlanOverview()
  } catch {
    planOverview.value = null
  }
}

function openSheet(kind: SheetKind): void {
  sheet.value = kind
  requestId.value = createRequestId()
  depositRequestId.value = createRequestId()
  withdrawalRequestId.value = createRequestId()
  form.amount = ''
  form.depositAmount = ''
  form.withdrawalAmount = ''
  form.note = ''
  form.relatedCashAdjustmentId = null
  formError.value = ''

  const plan = depositPlan.value
  form.monthlyEnabled = plan?.isActive ?? false
  form.monthlyAmount = plan ? String(plan.amount) : ''
  form.monthlyDay = plan?.dayOfMonth ? String(plan.dayOfMonth) : '15'
  form.monthlyTime = plan?.time.slice(0, 5) || '09:00'
}

function closeSheet(): void {
  if (!isSaving.value) {
    sheet.value = null
  }
}

async function submit(): Promise<void> {
  if (!sheet.value || !canSubmit.value) {
    return
  }

  formError.value = ''

  if (sheet.value === 'opening') {
    const balance = parseAmount(form.amount)

    if (balance === null) {
      formError.value = '请输入有效的余额，最多两位小数。'
      return
    }

    isSaving.value = true

    try {
      const result = await setProvidentOpening({
        balance,
        businessDate: today,
        note: form.note.trim(),
        requestId: requestId.value
      })
      notice.value = `已录入起始公积金 ¥${formatMoney(result.balance)}。`
      sheet.value = null
      await loadOverview()
    } catch (error) {
      formError.value = getApiErrorMessage(error, '保存失败，请重试。')
    } finally {
      isSaving.value = false
    }

    return
  }

  const deposit = depositAmount.value ?? 0
  const withdrawal = withdrawalAmount.value ?? 0

  if (deposit <= 0 && withdrawal <= 0 && !monthlyPlanChanged.value) {
    formError.value = '请至少填写缴存或提取其中一项。'
    return
  }

  if (form.depositAmount.trim() && depositAmount.value === null) {
    formError.value = '缴存金额格式不正确，最多两位小数。'
    return
  }

  if (form.withdrawalAmount.trim() && withdrawalAmount.value === null) {
    formError.value = '提取金额格式不正确，最多两位小数。'
    return
  }

  if (form.monthlyEnabled && (monthlyAmount.value ?? 0) <= 0) {
    formError.value = '请填写有效的每月缴存金额。'
    return
  }

  if (form.monthlyEnabled && (!Number.isInteger(monthlyDay.value) || monthlyDay.value < 1 || monthlyDay.value > 31)) {
    formError.value = '每月缴存日期须为 1 至 31。'
    return
  }

  if (form.monthlyEnabled && form.monthlyTime === '') {
    formError.value = '请选择每月缴存的执行时间。'
    return
  }

  isSaving.value = true
  let deposited = false
  const messages: string[] = []

  try {
    const plan = depositPlan.value

    if (form.monthlyEnabled) {
      const amount = monthlyAmount.value ?? 0
      const time = form.monthlyTime.length === 5 ? `${form.monthlyTime}:00` : form.monthlyTime

      if (monthlyValuesChanged.value) {
        await savePlan({
          id: plan?.id ?? null,
          type: 'providentDeposit',
          name: plan?.name ?? '',
          amount,
          cycle: 'monthly',
          dayOfMonth: monthlyDay.value,
          dayOfWeek: null,
          time,
          startDate: plan?.startDate ?? today,
          fundPositionId: null,
          requestId: createRequestId(),
          expectedVersion: plan?.version ?? null
        })
        messages.push(plan
          ? '每月缴存已更新'
          : `已设置每月 ${monthlyDay.value} 日缴存 ¥${formatMoney(amount)}`)
      }

      if (plan && !plan.isActive) {
        await resumePlan(plan.id, createRequestId())
        messages.push('已恢复每月缴存')
      }
    } else if (plan?.isActive) {
      await pausePlan(plan.id, createRequestId())
      messages.push('每月缴存已暂停')
    }

    if (deposit > 0) {
      const result = await depositProvident({
        amount: deposit,
        businessDate: today,
        note: form.note.trim(),
        relatedCashAdjustmentId: null,
        requestId: depositRequestId.value
      })
      deposited = true
      messages.push(`已缴存 ¥${formatMoney(result.activity.amount)}`)
    }

    if (withdrawal > 0) {
      const result = await withdrawProvident({
        amount: withdrawal,
        businessDate: today,
        note: form.note.trim(),
        relatedCashAdjustmentId: form.relatedCashAdjustmentId,
        requestId: withdrawalRequestId.value
      })
      messages.push(`已提取 ¥${formatMoney(result.activity.amount)} 至现金`)
    }

    sheet.value = null
    await Promise.all([loadOverview(), loadPlan()])
    const balanceNote = deposit > 0 || withdrawal > 0
      ? `；当前余额 ¥${formatMoney(overview.value?.balance ?? 0)}`
      : ''
    notice.value = `${messages.join('，')}${balanceNote}。`
  } catch (error) {
    const prefix = messages.length ? `${messages.join('，')}；` : ''
    formError.value = deposited
      ? `${prefix}缴存已登记，但提取保存失败：${getApiErrorMessage(error, '请稍后单独补记提取。')}`
      : `${prefix}${getApiErrorMessage(error, '保存失败，请重试。')}`
  } finally {
    isSaving.value = false
  }
}

function activityAmount(activity: ProvidentActivity): string {
  return `${activity.type === 'withdrawal' ? '−' : '+'}${formatMoney(activity.amount)}`
}

function activityAmountClass(activity: ProvidentActivity): string {
  if (activity.type === 'opening') {
    return 'transfer'
  }

  return activity.type === 'deposit' ? 'amount-gain' : 'amount-loss'
}

function activityNote(activity: ProvidentActivity): string {
  return activity.note ?? (activity.type === 'opening' ? '已有余额，不计入缴存' : activity.type === 'deposit' ? '个人＋单位合计' : '公积金 → 现金')
}

function goBack(): void {
  void router.push({ name: 'home' })
}
</script>

<template>
  <main class="page provident-page with-nav">
    <header class="provident-header">
      <BackButton @back="goBack" />
    </header>

    <p v-if="isLoading && !overview" class="state">正在读取公积金…</p>

    <section v-else-if="errorMessage" class="state">
      <p>{{ errorMessage }}</p>
      <button type="button" class="secondary-button" @click="loadOverview">重新加载</button>
    </section>

    <template v-else-if="overview">
      <section class="provident-hero">
        <p class="hero-label">
          公积金余额
          <span class="hero-tag">个人＋单位</span>
        </p>
        <h2 class="hero-balance"><span>¥</span>{{ formatMoney(overview.balance) }}</h2>
        <p class="hero-meta">
          截至 {{ formatBusinessDate(overview.asOfDate) }} · 按实际缴存与提取记录
        </p>
        <div class="hero-metrics">
          <div>
            <span>本月缴存</span>
            <b :class="amountTone(overview.monthDeposit)">{{ formatSignedMoney(overview.monthDeposit) }}</b>
          </div>
          <div>
            <span>本月提取</span>
            <b :class="amountTone(-overview.monthWithdrawal)">
              {{ overview.monthWithdrawal > 0 ? `−${formatMoney(overview.monthWithdrawal)}` : formatMoney(0) }}
            </b>
          </div>
          <div>
            <span>余额净变动</span>
            <b :class="amountTone(overview.netChange)">{{ formatSignedMoney(overview.netChange) }}</b>
          </div>
        </div>
      </section>

      <div class="provident-actions">
        <button type="button" class="action-secondary" @click="openSheet('opening')">录入余额</button>
        <button type="button" class="action-primary" @click="openSheet('record')">缴存/提取</button>
      </div>

      <p v-if="notice" class="notice" role="status">{{ notice }}</p>
      <p v-if="!overview.isConfigured" class="hint">
        尚未记录公积金，先录入余额或记录缴存。
      </p>

      <div class="section-head">
        <h2>余额变动</h2>
        <span class="meta">{{ monthLabel }}</span>
      </div>

      <section v-if="overview.activities.length" class="activity-card">
        <div v-for="activity in overview.activities" :key="activity.id" class="activity-row">
          <span class="activity-mark">{{ activity.type === 'opening' ? '始' : activity.type === 'deposit' ? '存' : '提' }}</span>
          <span class="activity-copy">
            <strong>{{ activity.type === 'opening' ? '起始公积金' : activity.type === 'deposit' ? '公积金缴存' : '公积金提取' }}</strong>
            <small>{{ activityNote(activity) }} · 余额 {{ formatMoney(activity.balanceAfter) }}</small>
          </span>
          <b class="activity-value" :class="activityAmountClass(activity)">{{ activityAmount(activity) }}</b>
        </div>
      </section>

      <div v-else class="empty-state">
        <p>本月还没有缴存或提取记录</p>
      </div>
    </template>

    <BottomSheet
      :open="sheet !== null"
      :title="sheetTitle"
      :subtitle="sheet === 'opening' ? '记录已有余额，按今天入账' : '缴存与提取可一起登记'"
      :close-disabled="isSaving"
      @close="closeSheet"
    >
      <div class="summary-line">
        <span>当前公积金余额</span>
        <b>¥{{ formatMoney(overview?.balance) }}</b>
      </div>

      <form class="form-stack" @submit.prevent="submit">
        <template v-if="sheet === 'opening'">
          <label class="form-field">
            <span>起始余额（元）</span>
            <input
              v-model="form.amount"
              type="text"
              inputmode="decimal"
              autocomplete="off"
              placeholder="0.00"
              @input="formError = ''"
            />
          </label>
        </template>

        <template v-else>
          <label class="form-field">
            <span>缴存金额（元）</span>
            <input
              v-model="form.depositAmount"
              type="text"
              inputmode="decimal"
              autocomplete="off"
              placeholder="0.00"
              @input="formError = ''"
            />
            <small>个人＋单位合计，可留空。</small>
          </label>

          <label class="form-field">
            <span>提取金额（元）</span>
            <input
              v-model="form.withdrawalAmount"
              type="text"
              inputmode="decimal"
              autocomplete="off"
              placeholder="0.00"
              @input="formError = ''"
            />
            <small>提取后等额转入现金，可留空。</small>
          </label>

          <section class="monthly-setting">
            <label class="monthly-toggle">
              <input v-model="form.monthlyEnabled" type="checkbox" />
              <span>
                <b>每月自动缴存</b>
                <small v-if="depositPlan">
                  当前每月 {{ depositPlan.dayOfMonth }} 日 ¥{{ formatMoney(depositPlan.amount) }}{{ depositPlan.isActive ? '' : '（已暂停）' }}
                </small>
              </span>
            </label>
            <div v-if="form.monthlyEnabled" class="monthly-fields">
              <div class="form-row">
                <label class="form-field">
                  <span>每月金额（元）</span>
                  <input
                    v-model="form.monthlyAmount"
                    type="text"
                    inputmode="decimal"
                    autocomplete="off"
                    placeholder="0.00"
                    @input="formError = ''"
                  />
                </label>
                <label class="form-field">
                  <span>每月日期</span>
                  <input v-model="form.monthlyDay" type="number" min="1" max="31" @input="formError = ''" />
                </label>
              </div>
              <label class="form-field">
                <span>执行时间</span>
                <input v-model="form.monthlyTime" type="time" @input="formError = ''" />
              </label>
              <small class="form-hint">当月没有该日期时按当月最后一天执行。</small>
            </div>
          </section>
        </template>

        <label class="form-field">
          <span>备注</span>
          <input v-model="form.note" type="text" maxlength="200" placeholder="选填" />
        </label>

        <CashAdjustmentLink v-if="sheet === 'record' && (withdrawalAmount ?? 0) > 0" v-model="form.relatedCashAdjustmentId" :business-date="today" :cash-effect="withdrawalAmount ?? 0" :disabled="isSaving" />
        <p class="form-note">{{ sheetNote }}</p>
        <p v-if="formError" class="form-error" role="alert">{{ formError }}</p>

        <div class="form-actions">
          <button type="button" class="secondary-button" :disabled="isSaving" @click="closeSheet">
            取消
          </button>
          <button type="submit" class="primary-button" :disabled="!canSubmit">
            {{ isSaving ? '保存中…' : '保存' }}
          </button>
        </div>
      </form>
    </BottomSheet>
    <AppNav />
  </main>
</template>

<style scoped>
.provident-page {
  padding-bottom: calc(97px + env(safe-area-inset-bottom));
}

.provident-header {
  display: flex;
  align-items: center;
  margin-bottom: 16px;
}


.provident-hero {
  padding: var(--card-padding);
  border-radius: var(--radius-card);
  background: var(--color-brand-soft);
}

.hero-label {
  display: flex;
  align-items: center;
  gap: 7px;
  margin: 0;
  color: var(--color-text-secondary);
  font-size: var(--text-footnote);
}

.hero-tag {
  padding: 2px 5px;
  border: 1px solid var(--color-border-subtle);
  border-radius: var(--radius-tag);
  background: var(--color-bg-surface);
  color: var(--color-brand);
  font-size: var(--text-caption);
}

.hero-balance {
  margin: 8px 0 7px;
  font-size: 2.375rem;
  font-weight: 600;
  letter-spacing: -1.1px;
  line-height: 1.2;
  font-variant-numeric: tabular-nums;
}

.hero-balance span {
  margin-right: 5px;
  font-size: 1.1875rem;
  font-weight: 400;
  letter-spacing: 0;
}

.hero-meta {
  margin: 0;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
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
  font-size: var(--text-caption);
}

.hero-metrics b {
  display: block;
  margin-top: 7px;
  font-size: 0.9375rem;
  font-weight: 500;
  letter-spacing: -0.3px;
  font-variant-numeric: tabular-nums;
}

.provident-actions {
  display: flex;
  gap: 10px;
  margin-top: 14px;
}

.provident-actions button {
  flex: 1;
  min-height: 44px;
  border-radius: var(--radius-control);
  font-size: 0.8125rem;
  font-weight: 500;
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
  border-radius: var(--radius-tag);
  background: var(--color-success-bg);
  color: var(--color-success);
  font-size: 0.75rem;
  line-height: 1.7;
}

.hint,
.summary-note {
  margin: 14px 0 0;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
  line-height: 1.7;
}

.section-head {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 12px;
  margin: 22px 0 10px;
}

.section-head h2 {
  margin: 0;
  font-size: 0.875rem;
  font-weight: 600;
  letter-spacing: -0.2px;
}

.meta {
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
}

.activity-card {
  overflow: hidden;
  border: 1px solid var(--color-border-subtle);
  border-radius: var(--radius-card);
  background: var(--color-bg-surface);
}

.activity-row {
  display: flex;
  align-items: center;
  gap: 11px;
  padding: 13px;
  border-bottom: 1px solid var(--color-border-subtle);
}

.activity-row:last-child {
  border-bottom: 0;
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
  font-size: 0.75rem;
  font-weight: 500;
}

.activity-copy {
  display: flex;
  flex-direction: column;
  gap: 3px;
  flex: 1;
  min-width: 0;
}

.activity-copy strong {
  font-size: 0.8125rem;
  font-weight: 500;
}

.activity-copy small {
  overflow: hidden;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
  line-height: 1.5;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.activity-value {
  flex: 0 0 auto;
  font-size: 0.875rem;
  font-weight: 500;
  font-variant-numeric: tabular-nums;
}

.transfer {
  color: var(--color-finance-transfer);
}

.empty-state {
  padding: 34px 16px;
  border: 1px dashed var(--color-border-default);
  border-radius: var(--radius-card);
  text-align: center;
}

.empty-state p {
  margin: 0 0 14px;
  color: var(--color-text-secondary);
  font-size: 0.75rem;
}

.summary-note {
  text-align: center;
}

.monthly-setting {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 12px;
  border: 1px solid var(--color-border-subtle);
  border-radius: var(--radius-control);
  background: var(--color-bg-subtle);
}

.monthly-toggle {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  cursor: pointer;
}

.monthly-toggle input {
  width: 18px;
  height: 18px;
  margin: 1px 0 0;
  accent-color: var(--color-brand);
}

.monthly-toggle span {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.monthly-toggle b {
  color: var(--color-text-primary);
  font-size: 0.75rem;
  font-weight: 500;
}

.monthly-toggle small {
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
  line-height: 1.5;
}

.monthly-fields {
  display: flex;
  flex-direction: column;
  gap: 12px;
}
</style>
