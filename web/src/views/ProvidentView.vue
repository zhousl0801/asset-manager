<script setup lang="ts">
import CashAdjustmentLink from '../components/CashAdjustmentLink.vue'
import { computed, onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { depositProvident, fetchProvidentOverview, withdrawProvident, setProvidentOpening } from '../api/provident'
import { ApiError, getApiErrorMessage } from '../api/client'
import BottomSheet from '../components/BottomSheet.vue'
import { formatBusinessDate, formatMoney, formatSignedMoney, parseAmount, todayInBusinessTimeZone } from '../utils/format'
import { formatPeriodLabel } from '../utils/period'
import { createRequestId } from '../utils/request'
import type { ProvidentActivity, ProvidentOverview } from '../types/provident'

type SheetKind = 'opening' | 'deposit' | 'withdrawal'

const router = useRouter()
const today = todayInBusinessTimeZone()
const overview = ref<ProvidentOverview | null>(null)
const isLoading = ref(false)
const errorMessage = ref('')
const notice = ref('')
const sheet = ref<SheetKind | null>(null)
const requestId = ref(createRequestId())
const isSaving = ref(false)
const formError = ref('')
const form = reactive({
  amount: '',
  businessDate: today,
  note: '',
  relatedCashAdjustmentId: null as string | null
})

const monthLabel = computed(() =>
  overview.value ? formatPeriodLabel(overview.value.periodFrom, 'month') : '')
const sheetTitle = computed(() =>
  sheet.value === 'opening' ? '录入起始公积金' : sheet.value === 'deposit' ? '补记公积金缴存' : '记录公积金提取')
const sheetNote = computed(() =>
  sheet.value === 'opening' ? '记录指定日期的已有余额，不计入缴存、工资或生活收入。' : sheet.value === 'deposit'
    ? '只增加公积金余额，不改变现金，也不计入工资或生活收入。'
    : '公积金余额减少，现金等额增加；不计入生活收入，总资产不变。')
const canSubmit = computed(() => {
  const amount = parseAmount(form.amount)

  return !isSaving.value && amount !== null && (sheet.value === 'opening' ? amount >= 0 : amount > 0)
})

onMounted(loadOverview)

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

function openSheet(kind: SheetKind): void {
  sheet.value = kind
  requestId.value = createRequestId()
  form.amount = ''
  form.businessDate = today
  form.note = ''
  form.relatedCashAdjustmentId = null
  formError.value = ''
}

function closeSheet(): void {
  if (!isSaving.value) {
    sheet.value = null
  }
}

async function submit(): Promise<void> {
  if (!sheet.value) {
    return
  }

  const amount = parseAmount(form.amount)

  if (amount === null || (sheet.value === 'opening' ? amount < 0 : amount <= 0)) {
    formError.value = '请输入大于 0 的金额，最多两位小数。'
    return
  }

  if (!form.businessDate) {
    formError.value = '请选择业务日期。'
    return
  }

  if (form.businessDate > today) {
    formError.value = '业务日期不能晚于今天。'
    return
  }


  isSaving.value = true
  formError.value = ''

  try {
    const input = {
      amount,
      businessDate: form.businessDate,
      note: form.note.trim(),
      relatedCashAdjustmentId: form.relatedCashAdjustmentId,
      requestId: requestId.value
    }
    const result = sheet.value === 'opening' ? await setProvidentOpening({ balance: amount, businessDate: form.businessDate, note: form.note.trim(), requestId: requestId.value }) : sheet.value === 'deposit'
      ? await depositProvident(input)
      : await withdrawProvident(input)

    notice.value = sheet.value === 'opening' ? `已录入起始公积金 ¥${formatMoney(result.balance)}。` : sheet.value === 'deposit'
      ? `已补记缴存 ¥${formatMoney(result.activity.amount)}，当前余额 ¥${formatMoney(result.balance)}。`
      : `已提取 ¥${formatMoney(result.activity.amount)} 至现金，公积金余额 ¥${formatMoney(result.balance)}。`
    sheet.value = null
    await loadOverview()
  } catch (error) {
    formError.value = getApiErrorMessage(error, '保存失败，请重试。')
  } finally {
    isSaving.value = false
  }
}

function activityAmount(activity: ProvidentActivity): string {
  return `${activity.type === 'withdrawal' ? '−' : '+'}${formatMoney(activity.amount)}`
}

function activityNote(activity: ProvidentActivity): string {
  return activity.note ?? (activity.type === 'opening' ? '已有余额，不计入缴存' : activity.type === 'deposit' ? '个人＋单位合计' : '公积金 → 现金')
}

function goBack(): void {
  void router.push({ name: 'home' })
}
</script>

<template>
  <main class="page provident-page">
    <header class="provident-header">
      <button type="button" class="back-link" @click="goBack">‹ 总览</button>
      <h1>公积金</h1>
      <RouterLink class="back-link" to="/history">历史核对</RouterLink>
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
            <b>{{ formatMoney(overview.monthDeposit) }}</b>
          </div>
          <div>
            <span>本月提取</span>
            <b>{{ formatMoney(overview.monthWithdrawal) }}</b>
          </div>
          <div>
            <span>余额净变动</span>
            <b>{{ formatSignedMoney(overview.netChange) }}</b>
          </div>
        </div>
      </section>

      <div class="provident-actions">
        <button type="button" class="action-secondary" @click="openSheet('opening')">录入起始余额</button>
        <button type="button" class="action-primary" @click="openSheet('deposit')">补记缴存</button>
        <button type="button" class="action-secondary" @click="openSheet('withdrawal')">记录提取</button>
      </div>

      <p v-if="notice" class="notice" role="status">{{ notice }}</p>
      <p v-if="!overview.isConfigured" class="hint">
        尚未记录公积金，先补记缴存或录入起始余额。
      </p>
      <p v-else class="hint">缴存计入资产增加；提取转入现金，不计入生活收入。</p>

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
          <b class="activity-value transfer">{{ activityAmount(activity) }}</b>
        </div>
      </section>

      <div v-else class="empty-state">
        <p>本月还没有缴存或提取记录</p>
        <button type="button" class="primary-button" @click="openSheet('deposit')">补记缴存</button>
      </div>

      <p class="summary-note">
        {{ formatBusinessDate(overview.asOfDate) }} 余额 {{ formatMoney(overview.balance) }} ·
        缴存与提取单独留痕，不混入日常收支。
      </p>
    </template>

    <BottomSheet
      :open="sheet !== null"
      :title="sheetTitle"
      :subtitle="sheet === 'opening' ? '已有余额，独立记录起始日期' : sheet === 'deposit' ? '个人与单位合计记为一笔' : '提取后等额转入现金'"
      :close-disabled="isSaving"
      @close="closeSheet"
    >
      <div class="summary-line">
        <span>当前公积金余额</span>
        <b>¥{{ formatMoney(overview?.balance) }}</b>
      </div>

      <form class="form-stack" @submit.prevent="submit">
        <label class="form-field">
          <span>{{ sheet === 'opening' ? '起始余额（元）' : sheet === 'deposit' ? '缴存金额（元）' : '提取金额（元）' }}</span>
          <input
            v-model="form.amount"
            type="text"
            inputmode="decimal"
            autocomplete="off"
            placeholder="0.00"
            @input="formError = ''"
          />
        </label>

        <label class="form-field">
          <span>业务日期</span>
          <input v-model="form.businessDate" type="date" :max="today" />
        </label>

        <label class="form-field">
          <span>备注</span>
          <input v-model="form.note" type="text" maxlength="200" placeholder="选填" />
        </label>

        <CashAdjustmentLink v-if="sheet === 'withdrawal'" v-model="form.relatedCashAdjustmentId" :business-date="form.businessDate" :cash-effect="parseAmount(form.amount) ?? 0" :disabled="isSaving" />
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
  </main>
</template>

<style scoped>
.provident-page {
  padding-bottom: calc(32px + env(safe-area-inset-bottom));
}

.provident-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 10px;
  margin-bottom: 16px;
}

.provident-header h1 {
  margin: 0;
  font-size: 19px;
  font-weight: 600;
  letter-spacing: -0.3px;
}

.back-link {
  min-height: 36px;
  padding: 0 4px;
  border: 0;
  background: transparent;
  color: var(--color-brand);
  font-size: 12px;
}

.header-spacer {
  width: 46px;
}

.provident-hero {
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

.provident-actions {
  display: flex;
  gap: 10px;
  margin-top: 14px;
}

.provident-actions button {
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

.hint,
.summary-note {
  margin: 14px 0 0;
  color: var(--color-text-secondary);
  font-size: 10px;
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
  font-size: 14px;
  font-weight: 600;
  letter-spacing: -0.2px;
}

.meta {
  color: var(--color-text-secondary);
  font-size: 10px;
}

.activity-card {
  overflow: hidden;
  border: 1px solid var(--color-border-subtle);
  border-radius: 12px;
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
  font-size: 13px;
  font-weight: 500;
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

.transfer {
  color: var(--color-finance-transfer);
}

.empty-state {
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
  text-align: center;
}
</style>
