<script setup lang="ts">
import CashAdjustmentLink from '../components/CashAdjustmentLink.vue'
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import {
  arriveFundSell,
  buyFund,
  confirmFundBuy,
  confirmFundSell,
  createFundPosition,
  fetchFundOverview,
  refreshFundQuote,
  sellFund,
  updateFundQuote
} from '../api/funds'
import { ApiError, getApiErrorMessage } from '../api/client'
import AppNav from '../components/AppNav.vue'
import BottomSheet from '../components/BottomSheet.vue'
import {
  formatBusinessDate,
  formatMoney,
  formatQuantity,
  formatSignedMoney,
  parseAmount,
  parseNonNegativeQuantity,
  parsePrice,
  parseQuantity,
  todayInBusinessTimeZone
} from '../utils/format'
import { createRequestId } from '../utils/request'
import type {
  FundActivity,
  FundOverview,
  FundPosition,
  PendingFundArrival,
  PendingFundBuy,
  PendingFundSell
} from '../types/fund'

type SheetKind = 'opening' | 'buy' | 'confirm' | 'sell' | 'sellConfirm' | 'sellArrival' | 'quote'

const router = useRouter()
const today = todayInBusinessTimeZone()
const overview = ref<FundOverview | null>(null)
const isLoading = ref(false)
const errorMessage = ref('')
const notice = ref('')
const sheet = ref<SheetKind | null>(null)
const requestId = ref(createRequestId())
const isSaving = ref(false)
const formError = ref('')
const confirmingPendingId = ref<string | null>(null)
const confirmingSellId = ref<string | null>(null)
const arrivingConfirmId = ref<string | null>(null)
const refreshingQuote = ref(false)
const form = reactive({
  positionId: '',
  name: '',
  code: '',
  shareClass: '',
  platform: '',
  shares: '',
  cost: '',
  amount: '',
  netAmount: '',
  netValue: '',
  netValueDate: today,
  fee: '0',
  businessDate: today,
  note: '',
  relatedCashAdjustmentId: null as string | null
})

const selectedPosition = computed<FundPosition | null>(() =>
  overview.value?.positions.find((position) => position.id === form.positionId) ?? null)
const selectedPending = computed<PendingFundBuy | null>(() =>
  overview.value?.pendingBuys.find((pending) => pending.transactionId === confirmingPendingId.value) ?? null)
const selectedSellApplication = computed<PendingFundSell | null>(() =>
  overview.value?.sellApplications.find((pending) => pending.transactionId === confirmingSellId.value) ?? null)
const selectedPendingArrival = computed<PendingFundArrival | null>(() =>
  overview.value?.pendingArrivals.find((pending) => pending.transactionId === arrivingConfirmId.value) ?? null)
const sheetTitle = computed(() => {
  switch (sheet.value) {
    case 'opening':
      return '录入基金持仓'
    case 'buy':
      return '记录基金买入'
    case 'confirm':
      return '确认基金买入'
    case 'sell':
      return '申请赎回基金'
    case 'sellConfirm':
      return '确认基金赎回'
    case 'sellArrival':
      return '登记赎回到账'
    case 'quote':
      return '更新基金净值'
    default:
      return ''
  }
})
const sheetSubtitle = computed(() => {
  switch (sheet.value) {
    case 'opening':
      return '用于已有持仓，不扣现金'
    case 'buy':
      return '现金减少，买入待确认增加'
    case 'confirm':
      return '待确认转为持仓，不再次扣款'
    case 'sell':
      return '份额冻结，仍在持仓中估值'
    case 'sellConfirm':
      return '份额减少，净回款转为待到账'
    case 'sellArrival':
      return '待到账转入现金，不重复计算收益'
    case 'quote':
      return '只更新估值，不改变现金和成本'
    default:
      return ''
  }
})
const sheetNote = computed(() => {
  switch (sheet.value) {
    case 'opening':
      return '录入已有持仓不会从现金扣除购买成本，也不产生生活收支。'
    case 'buy':
      return '先按实际扣款计入买入待确认，总资产不变；确认份额和净值后再转入持仓。'
    case 'confirm':
      return '确认沿用原扣款金额，不再扣现金；费用从已扣款金额中列示，成交差额体现在持有收益中。'
    case 'sell':
      return '申请后份额仍计入持仓估值，但会冻结，不能重复申请超过可售份额。'
    case 'sellConfirm':
      return '按确认份额比例结转成本；净回款转为待到账，与成本差额计入已实现收益。'
    case 'sellArrival':
      return '到账金额必须与待到账金额一致，不再次确认收益；如有差额，请先更正赎回确认。'
    case 'quote':
      return '净值日期不能晚于今天；未披露时继续保留最近有效净值。'
    default:
      return ''
  }
})
const canSubmit = computed(() => {
  if (isSaving.value) {
    return false
  }

  switch (sheet.value) {
    case 'opening':
      return form.name.trim() !== ''
        && parseNonNegativeQuantity(form.shares) !== null
        && parseAmount(form.cost) !== null
        && parsePrice(form.netValue) !== null
    case 'buy':
      return form.positionId !== '' && (parseAmount(form.amount) ?? 0) > 0
    case 'confirm':
      return confirmingPendingId.value !== null
        && parseQuantity(form.shares) !== null
        && parsePrice(form.netValue) !== null
        && parseAmount(form.fee) !== null
        && (parseAmount(form.fee) ?? 0) >= 0
    case 'sell':
      return form.positionId !== '' && parseQuantity(form.shares) !== null
    case 'sellConfirm':
      return confirmingSellId.value !== null
        && parseQuantity(form.shares) !== null
        && parsePrice(form.netValue) !== null
        && (parseAmount(form.netAmount) ?? 0) > 0
        && parseAmount(form.fee) !== null
    case 'sellArrival':
      return arrivingConfirmId.value !== null && (parseAmount(form.netAmount) ?? 0) > 0
    case 'quote':
      return form.positionId !== '' && parsePrice(form.netValue) !== null
    default:
      return false
  }
})

watch(
  () => form.positionId,
  (positionId) => {
    const position = overview.value?.positions.find((item) => item.id === positionId)

    if (!position || sheet.value !== 'quote') {
      return
    }

    form.netValue = String(position.netValue)
    form.netValueDate = position.netValueDate
  }
)

onMounted(loadOverview)

async function loadOverview(): Promise<void> {
  isLoading.value = true
  errorMessage.value = ''

  try {
    overview.value = await fetchFundOverview()

    if (!form.positionId && overview.value.positions.length > 0) {
      form.positionId = overview.value.positions[0]!.id
    }
  } catch (error) {
    overview.value = null

    if (error instanceof ApiError && error.status === 404) {
      await router.replace({ name: 'setup' })
      return
    }

    errorMessage.value = getApiErrorMessage(error, '基金数据加载失败，请重试。')
  } finally {
    isLoading.value = false
  }
}

function openSheet(kind: SheetKind, positionId?: string): void {
  sheet.value = kind
  confirmingPendingId.value = null
  confirmingSellId.value = null
  arrivingConfirmId.value = null
  requestId.value = createRequestId()
  formError.value = ''
  form.note = ''
  form.relatedCashAdjustmentId = null
  form.businessDate = today
  form.shares = ''
  form.cost = ''
  form.amount = ''
  form.netAmount = ''
  form.fee = '0'
  form.name = ''
  form.code = ''
  form.shareClass = ''
  form.platform = ''

  if (kind === 'opening') {
    form.positionId = ''
    form.netValue = ''
    form.netValueDate = today
    return
  }

  form.positionId = positionId ?? overview.value?.positions[0]?.id ?? ''

  const position = selectedPosition.value
  form.netValue = position ? String(position.netValue) : ''
  form.netValueDate = position?.netValueDate ?? today
}

function openConfirmSheet(pending: PendingFundBuy): void {
  openSheet('confirm', pending.positionId)
  confirmingPendingId.value = pending.transactionId
  form.netValue = ''
  form.netValueDate = today
  form.businessDate = today
  form.note = pending.note ?? ''
}

function openSellConfirmSheet(application: PendingFundSell): void {
  openSheet('sellConfirm', application.positionId)
  confirmingSellId.value = application.transactionId
  form.shares = String(application.shares)
  form.netValue = ''
  form.netValueDate = today
  form.businessDate = today
  form.note = application.note ?? ''
}

function openArrivalSheet(arrival: PendingFundArrival): void {
  openSheet('sellArrival', arrival.positionId)
  arrivingConfirmId.value = arrival.transactionId
  form.netAmount = String(arrival.amount)
  form.businessDate = today
  form.note = arrival.note ?? ''
}

function closeSheet(): void {
  if (!isSaving.value) {
    sheet.value = null
  }
}

async function submit(): Promise<void> {
  if (!sheet.value || !overview.value) {
    return
  }

  formError.value = ''

  try {
    if (sheet.value === 'opening') {
      await submitOpening()
    } else if (sheet.value === 'buy') {
      await submitBuy()
    } else if (sheet.value === 'confirm') {
      await submitConfirm()
    } else if (sheet.value === 'sell') {
      await submitSell()
    } else if (sheet.value === 'sellConfirm') {
      await submitSellConfirm()
    } else if (sheet.value === 'sellArrival') {
      await submitSellArrival()
    } else {
      await submitQuote()
    }
  } catch (error) {
    formError.value = getApiErrorMessage(error, '保存失败，请重试。')
  } finally {
    isSaving.value = false
  }
}

async function submitOpening(): Promise<void> {
  const shares = parseNonNegativeQuantity(form.shares)
  const cost = parseAmount(form.cost)
  const netValue = parsePrice(form.netValue)

  if (!form.name.trim()) {
    formError.value = '请输入基金名称。'
    return
  }

  if (shares === null || cost === null || netValue === null) {
    formError.value = '请填写有效的份额、成本和净值。'
    return
  }

  if (shares === 0 && cost !== 0) {
    formError.value = '份额为 0 时，持仓成本也应为 0。'
    return
  }

  const dateError = validateValueDate(form.netValueDate, '净值日期')

  if (dateError) {
    formError.value = dateError
    return
  }

  const holdingDateError = validateBusinessDate()
  if (holdingDateError) { formError.value = holdingDateError; return }

  isSaving.value = true
  await createFundPosition({
    name: form.name.trim(),
    code: form.code.trim(),
    shareClass: form.shareClass.trim(),
    platform: form.platform.trim(),
    shares,
    cost,
    netValue,
    netValueDate: form.netValueDate,
    note: form.note.trim(),
    holdingDate: form.businessDate,
    requestId: requestId.value
  })
  notice.value = `已录入基金「${form.name.trim()}」。`
  sheet.value = null
  await loadOverview()
}

async function submitBuy(): Promise<void> {
  const position = selectedPosition.value
  const amount = parseAmount(form.amount)

  if (!position) {
    formError.value = '请选择基金持仓。'
    return
  }

  if (amount === null || amount <= 0) {
    formError.value = '请填写有效的扣款金额。'
    return
  }

  const businessDateError = validateBusinessDate()

  if (businessDateError) {
    formError.value = businessDateError
    return
  }

  isSaving.value = true
  const result = await buyFund({
    positionId: position.id,
    amount,
    businessDate: form.businessDate,
    note: form.note.trim(),
    relatedCashAdjustmentId: form.relatedCashAdjustmentId,
    requestId: requestId.value
  })
  notice.value = `已记录买入待确认 ¥${formatMoney(result.activity.amount)}，当前现金 ¥${formatMoney(result.cashBalance)}。`
  sheet.value = null
  await loadOverview()
}

async function submitConfirm(): Promise<void> {
  const pending = selectedPending.value
  const shares = parseQuantity(form.shares)
  const netValue = parsePrice(form.netValue)
  const fee = parseAmount(form.fee)

  if (!pending) {
    formError.value = '买入待确认记录不存在，请刷新后重试。'
    return
  }

  if (shares === null || netValue === null || fee === null || fee < 0) {
    formError.value = '请填写有效的确认份额、净值和费用。'
    return
  }

  if (fee > pending.amount) {
    formError.value = '费用不能超过原扣款金额。'
    return
  }

  const businessDateError = validateBusinessDate()

  if (businessDateError) {
    formError.value = businessDateError
    return
  }

  if (form.businessDate < pending.businessDate) {
    formError.value = '确认日期不能早于买入日期。'
    return
  }

  const valueDateError = validateValueDate(form.netValueDate, '净值日期')

  if (valueDateError) {
    formError.value = valueDateError
    return
  }

  if (form.netValueDate > form.businessDate) {
    formError.value = '净值日期不能晚于确认日期。'
    return
  }

  isSaving.value = true
  const result = await confirmFundBuy(pending.transactionId, {
    shares,
    netValue,
    netValueDate: form.netValueDate,
    fee,
    businessDate: form.businessDate,
    note: form.note.trim(),
    requestId: requestId.value
  })
  notice.value = `已确认 ${formatQuantity(result.activity.shares)} 份，当前现金 ¥${formatMoney(result.cashBalance)}。`
  sheet.value = null
  await loadOverview()
}

async function submitSell(): Promise<void> {
  const position = selectedPosition.value
  const shares = parseQuantity(form.shares)

  if (!position) {
    formError.value = '请选择基金持仓。'
    return
  }

  if (shares === null) {
    formError.value = '请填写有效的申请赎回份额。'
    return
  }

  const dateError = validateBusinessDate()

  if (dateError) {
    formError.value = dateError
    return
  }

  isSaving.value = true
  const result = await sellFund({
    positionId: position.id,
    shares,
    businessDate: form.businessDate,
    note: form.note.trim(),
    requestId: requestId.value
  })
  notice.value = `已申请赎回 ${formatQuantity(result.activity.shares)} 份，确认后转为待到账。`
  sheet.value = null
  await loadOverview()
}

async function submitSellConfirm(): Promise<void> {
  const application = selectedSellApplication.value
  const shares = parseQuantity(form.shares)
  const netValue = parsePrice(form.netValue)
  const netAmount = parseAmount(form.netAmount)
  const fee = parseAmount(form.fee)

  if (!application) {
    formError.value = '赎回申请不存在，请刷新后重试。'
    return
  }

  if (shares === null || netValue === null || netAmount === null || netAmount <= 0 || fee === null || fee < 0) {
    formError.value = '请填写有效的确认份额、净值、净回款和费用。'
    return
  }

  if (shares > application.shares) {
    formError.value = '确认份额不能超过申请赎回份额。'
    return
  }

  if (fee > netAmount) {
    formError.value = '费用不能超过净回款金额。'
    return
  }

  const businessDateError = validateBusinessDate()

  if (businessDateError) {
    formError.value = businessDateError
    return
  }

  if (form.businessDate < application.businessDate) {
    formError.value = '确认日期不能早于申请日期。'
    return
  }

  const valueDateError = validateValueDate(form.netValueDate, '净值日期')

  if (valueDateError) {
    formError.value = valueDateError
    return
  }

  if (form.netValueDate > form.businessDate) {
    formError.value = '净值日期不能晚于确认日期。'
    return
  }

  isSaving.value = true
  const result = await confirmFundSell(application.transactionId, {
    shares,
    netValue,
    netValueDate: form.netValueDate,
    fee,
    netAmount,
    businessDate: form.businessDate,
    note: form.note.trim(),
    requestId: requestId.value
  })
  notice.value = `已确认赎回 ${formatQuantity(result.activity.shares)} 份，待到账 ¥${formatMoney(result.activity.amount)}。`
  sheet.value = null
  await loadOverview()
}

async function submitSellArrival(): Promise<void> {
  const arrival = selectedPendingArrival.value
  const amount = parseAmount(form.netAmount)

  if (!arrival) {
    formError.value = '待到账记录不存在，请刷新后重试。'
    return
  }

  if (amount === null || amount <= 0) {
    formError.value = '请填写有效的到账金额。'
    return
  }

  if (amount !== arrival.amount) {
    formError.value = '到账金额必须与待到账金额一致；如有差额，请先更正赎回确认。'
    return
  }

  const businessDateError = validateBusinessDate()

  if (businessDateError) {
    formError.value = businessDateError
    return
  }

  if (form.businessDate < arrival.businessDate) {
    formError.value = '到账日期不能早于确认日期。'
    return
  }

  isSaving.value = true
  const result = await arriveFundSell(arrival.transactionId, {
    amount,
    businessDate: form.businessDate,
    note: form.note.trim(),
    requestId: requestId.value
  })
  notice.value = `赎回到账 ¥${formatMoney(result.activity.amount)}，当前现金 ¥${formatMoney(result.cashBalance)}。`
  sheet.value = null
  await loadOverview()
}

async function submitQuote(): Promise<void> {
  const position = selectedPosition.value
  const netValue = parsePrice(form.netValue)

  if (!position) {
    formError.value = '请选择基金持仓。'
    return
  }

  if (netValue === null) {
    formError.value = '请输入大于 0 的净值，最多四位小数。'
    return
  }

  const dateError = validateValueDate(form.netValueDate, '净值日期')

  if (dateError) {
    formError.value = dateError
    return
  }

  isSaving.value = true
  await updateFundQuote({
    positionId: position.id,
    netValue,
    netValueDate: form.netValueDate,
    requestId: requestId.value
  })
  notice.value = `「${position.name}」净值已更新。`
  sheet.value = null
  await loadOverview()
}

function validateBusinessDate(): string | null {
  if (!form.businessDate) {
    return '请选择业务日期。'
  }

  if (form.businessDate > today) {
    return '业务日期不能晚于今天。'
  }

  return null
}

function validateValueDate(value: string, label: string): string | null {
  if (!value) {
    return `请选择${label}。`
  }

  if (value > today) {
    return `${label}不能晚于今天。`
  }

  return null
}

function positionLabel(position: FundPosition): string {
  return position.shareClass ? `${position.name} ${position.shareClass}` : position.name
}

function quoteSourceLabel(source: string | null): string {
  switch (source) {
    case 'eastmoney':
      return '天天基金'
    case 'manual':
      return '手动录入'
    default:
      return source ?? '手动录入'
  }
}

async function refreshQuote(): Promise<void> {
  const position = selectedPosition.value

  if (!position) {
    formError.value = '请选择基金持仓。'
    return
  }

  refreshingQuote.value = true
  formError.value = ''

  try {
    const result = await refreshFundQuote(position.id)
    notice.value = result.updated
      ? `已获取最新净值 ${formatQuantity(result.netValue)}（${formatBusinessDate(result.effectiveDate)}，${quoteSourceLabel(result.source)}）。`
      : `净值已是最新 ${formatQuantity(result.netValue)}（${formatBusinessDate(result.effectiveDate)}）。`
    sheet.value = null
    await loadOverview()
  } catch (error) {
    formError.value = getApiErrorMessage(error, '获取净值失败，已保留最近有效值。')
  } finally {
    refreshingQuote.value = false
  }
}

function activityTitle(activity: FundActivity): string {
  switch (activity.type) {
    case 'opening':
      return '录入持仓'
    case 'buy':
      return '基金买入'
    case 'buyPending':
      return '买入待确认'
    case 'buyConfirmed':
      return '买入确认'
    case 'sell':
      return '基金卖出'
    case 'sellApply':
      return '赎回申请'
    case 'sellConfirm':
      return '赎回确认'
    case 'sellArrival':
      return '赎回到账'
    case 'quote':
      return '更新净值'
    default:
      return '基金记录'
  }
}

function activityNote(activity: FundActivity): string {
  if (activity.note) {
    return activity.note
  }

  switch (activity.type) {
    case 'opening':
      return '已有持仓，不扣现金'
    case 'buy':
      return '现金 → 基金持仓'
    case 'buyPending':
      return '现金 → 买入待确认'
    case 'buyConfirmed':
      return '买入待确认 → 基金持仓'
    case 'sell':
      return '基金持仓 → 现金'
    case 'sellApply':
      return '份额冻结，仍在持仓'
    case 'sellConfirm':
      return '基金持仓 → 待到账'
    case 'sellArrival':
      return '待到账 → 现金'
    default:
      return '仅更新估值'
  }
}

function activityAmountText(activity: FundActivity): string {
  if (activity.type === 'buy' || activity.type === 'buyPending' || activity.type === 'sell'
    || activity.type === 'sellArrival') {
    return formatSignedMoney(activity.cashEffect)
  }

  if (activity.type === 'quote') {
    return `净值 ${formatQuantity(activity.netValue)}`
  }

  if (activity.type === 'buyConfirmed' || activity.type === 'sellApply') {
    return `${formatQuantity(activity.shares)} 份`
  }

  if (activity.type === 'sellConfirm') {
    return `待到账 ${formatMoney(activity.amount)}`
  }

  return `成本 ${formatMoney(activity.amount)}`
}

function activityMeta(activity: FundActivity): string {
  if (activity.type === 'quote') {
    return `${activity.positionName} · ${formatBusinessDate(activity.businessDate)}`
  }

  const shares = activity.shares === null ? '' : `${formatQuantity(activity.shares)} 份`
  const value = activity.netValue === null ? '' : `净值 ${formatQuantity(activity.netValue)}`
  const fee = activity.fee > 0 ? ` · 费用 ${formatMoney(activity.fee)}` : ''
  const details = [shares, value].filter(Boolean).join(' · ')

  return `${activity.positionName}${details ? ` · ${details}` : ''}${fee}`
}

function goBack(): void {
  void router.push({ name: 'home' })
}
</script>

<template>
  <main class="page funds-page with-nav">
    <header class="funds-header">
      <button type="button" class="back-link" @click="goBack">‹ 总览</button>
      <h1>基金</h1>
      <RouterLink class="back-link" to="/history">历史核对</RouterLink>
    </header>

    <p v-if="isLoading && !overview" class="state">正在读取基金…</p>

    <section v-else-if="errorMessage" class="state">
      <p>{{ errorMessage }}</p>
      <button type="button" class="secondary-button" @click="loadOverview">重新加载</button>
    </section>

    <template v-else-if="overview">
      <section class="funds-hero">
        <p class="hero-label">
          基金资产
          <span class="hero-tag">{{ overview.historyConfirmed ? '已确认持仓' : '持仓日期待确认' }}</span>
        </p>
        <h2 class="hero-balance"><span>¥</span>{{ overview.hasEffectiveQuotes ? formatMoney(overview.totalValue) : '行情待补' }}</h2>
        <p class="hero-meta">
          {{ overview.positions.length }} 只基金 · 持仓成本 {{ formatMoney(overview.totalCost) }}
          <template v-if="overview.pendingAmount > 0"> · 含待确认 ¥{{ formatMoney(overview.pendingAmount) }}</template>
          <template v-if="overview.pendingArrivalAmount > 0"> · 含待到账 ¥{{ formatMoney(overview.pendingArrivalAmount) }}</template>
        </p>
        <div class="hero-metrics">
          <div>
            <span>持有收益</span>
            <b :class="{ profit: overview.holdingProfit > 0, loss: overview.holdingProfit < 0 }">
              {{ overview.hasEffectiveQuotes ? formatSignedMoney(overview.holdingProfit) : '行情待补' }}
            </b>
          </div>
          <div>
            <span>持有收益率</span>
            <b>{{ overview.hasEffectiveQuotes && overview.totalCost > 0 ? `${formatSignedMoney(overview.holdingProfit / overview.totalCost * 100)}%` : '—' }}</b>
          </div>
          <div>
            <span>已实现收益</span>
            <b>{{ overview.historyConfirmed ? formatSignedMoney(overview.totalRealizedProfit) : '待核对' }}</b>
          </div>
        </div>
      </section>

      <div class="funds-actions">
        <button type="button" class="action-secondary" @click="openSheet('opening')">录入持仓</button>
        <button
          type="button"
          class="action-primary"
          :disabled="overview.positions.length === 0"
          @click="openSheet('buy')"
        >
          买入
        </button>
        <button
          type="button"
          class="action-secondary"
          :disabled="overview.positions.length === 0"
          @click="openSheet('sell')"
        >
          卖出
        </button>
        <button
          type="button"
          class="action-secondary"
          :disabled="overview.positions.length === 0"
          @click="openSheet('quote')"
        >
          更新净值
        </button>
      </div>

      <p v-if="notice" class="notice" role="status">{{ notice }}</p>

      <template v-if="overview.pendingBuys.length">
        <div class="section-head">
          <h2>买入待确认</h2>
          <span class="meta">{{ overview.pendingBuys.length }} 笔 · ¥{{ formatMoney(overview.pendingAmount) }}</span>
        </div>
        <section class="pending-list">
          <article v-for="pending in overview.pendingBuys" :key="pending.transactionId" class="pending-card">
            <div class="pending-copy">
              <strong>{{ pending.positionName }}</strong>
              <small>
                {{ formatBusinessDate(pending.businessDate) }}<template v-if="pending.note"> · {{ pending.note }}</template>
              </small>
            </div>
            <div class="pending-side">
              <b>¥{{ formatMoney(pending.amount) }}</b>
              <button type="button" class="confirm-button" @click="openConfirmSheet(pending)">确认份额</button>
            </div>
          </article>
        </section>
      </template>

      <template v-if="overview.sellApplications.length">
        <div class="section-head">
          <h2>赎回申请</h2>
          <span class="meta">{{ overview.sellApplications.length }} 笔 · 份额已冻结</span>
        </div>
        <section class="pending-list">
          <article v-for="application in overview.sellApplications" :key="application.transactionId" class="pending-card">
            <div class="pending-copy">
              <strong>{{ application.positionName }}</strong>
              <small>
                {{ formatBusinessDate(application.businessDate) }} · {{ formatQuantity(application.shares) }} 份
                <template v-if="application.note"> · {{ application.note }}</template>
              </small>
            </div>
            <div class="pending-side">
              <button type="button" class="confirm-button" @click="openSellConfirmSheet(application)">确认赎回</button>
            </div>
          </article>
        </section>
      </template>

      <template v-if="overview.pendingArrivals.length">
        <div class="section-head">
          <h2>赎回待到账</h2>
          <span class="meta">{{ overview.pendingArrivals.length }} 笔 · ¥{{ formatMoney(overview.pendingArrivalAmount) }}</span>
        </div>
        <section class="pending-list">
          <article v-for="arrival in overview.pendingArrivals" :key="arrival.transactionId" class="pending-card">
            <div class="pending-copy">
              <strong>{{ arrival.positionName }}</strong>
              <small>
                {{ formatBusinessDate(arrival.businessDate) }} · {{ formatQuantity(arrival.shares) }} 份
                <template v-if="arrival.note"> · {{ arrival.note }}</template>
              </small>
            </div>
            <div class="pending-side">
              <b>¥{{ formatMoney(arrival.amount) }}</b>
              <button type="button" class="confirm-button" @click="openArrivalSheet(arrival)">登记到账</button>
            </div>
          </article>
        </section>
      </template>

      <div class="section-head">
        <h2>持仓</h2>
        <span class="meta">可用现金 ¥{{ formatMoney(overview.cashBalance) }}</span>
      </div>

      <section v-if="overview.positions.length" class="position-list">
        <article v-for="position in overview.positions" :key="position.id" class="position-card">
          <header>
            <div>
              <strong>{{ positionLabel(position) }}</strong>
              <small>
                {{ [position.platform, position.code].filter(Boolean).join(' · ') || '未填写平台' }}
              </small>
            </div>
            <b>{{ position.hasEffectiveQuote ? formatMoney(position.value) : '行情待补' }}</b>
          </header>
          <div class="position-metrics">
            <div>
              <span>份额</span>
              <b>{{ formatQuantity(position.shares) }}</b>
            </div>
            <div>
              <span>成本</span>
              <b>{{ formatMoney(position.cost) }}</b>
            </div>
            <div>
              <span>持有收益</span>
              <b :class="{ profit: position.holdingProfit > 0, loss: position.holdingProfit < 0 }">
                {{ position.hasEffectiveQuote ? formatSignedMoney(position.holdingProfit) : '行情待补' }}
              </b>
            </div>
          </div>
          <footer>
            <template v-if="position.hasEffectiveQuote">净值 {{ formatQuantity(position.netValue) }} · {{ formatBusinessDate(position.netValueDate) }} · {{ quoteSourceLabel(position.lastQuoteSource) }}</template><template v-else>行情依据不足</template>
            <span>已实现 {{ formatSignedMoney(position.realizedProfit) }}</span>
          </footer>
          <small v-if="position.lastQuoteError" class="quote-error">最近获取失败：{{ position.lastQuoteError }}</small>
        </article>
      </section>

      <div v-else class="empty-state">
        <p>还没有基金持仓</p>
        <button type="button" class="primary-button" @click="openSheet('opening')">录入已有持仓</button>
      </div>

      <div class="section-head">
        <h2>交易记录</h2>
        <span class="meta">最近 200 条</span>
      </div>

      <section v-if="overview.activities.length" class="activity-card">
        <div v-for="activity in overview.activities" :key="activity.id" class="activity-row">
          <span class="activity-mark">{{ activity.type === 'buy' ? '买' : activity.type === 'buyPending' ? '待' : activity.type === 'buyConfirmed' ? '确' : activity.type === 'sell' ? '卖' : activity.type === 'sellApply' ? '申' : activity.type === 'sellConfirm' ? '赎' : activity.type === 'sellArrival' ? '到' : activity.type === 'quote' ? '净' : '录' }}</span>
          <span class="activity-copy">
            <strong>{{ activityTitle(activity) }}</strong>
            <small>{{ activityNote(activity) }} · {{ activityMeta(activity) }}</small>
          </span>
          <b class="activity-value" :class="{ income: activity.cashEffect > 0 }">
            {{ activityAmountText(activity) }}
          </b>
        </div>
      </section>

      <p v-else class="summary-note">还没有基金交易记录。</p>

      <p v-if="!overview.historyConfirmed || !overview.hasEffectiveQuotes" class="summary-note">部分持仓日期或行情依据待核对，当前缓存仅供核对，请到历史核对确认。</p>
      <p class="summary-note">
        基金买卖属于资产流转，不计入生活收支；净值更新只影响估值。
      </p>
    </template>

    <BottomSheet
      :open="sheet !== null"
      :title="sheetTitle"
      :subtitle="sheetSubtitle"
      :close-disabled="isSaving"
      @close="closeSheet"
    >
      <div class="sheet-summary">
        <div class="summary-line">
          <span>可用现金</span>
          <b>¥{{ formatMoney(overview?.cashBalance) }}</b>
        </div>
        <div v-if="selectedPosition" class="summary-line">
          <span>当前持仓</span>
          <b>{{ formatQuantity(selectedPosition.shares) }} 份</b>
        </div>
        <div v-if="selectedPending" class="summary-line">
          <span>买入待确认</span>
          <b>¥{{ formatMoney(selectedPending.amount) }}</b>
        </div>
        <div v-if="selectedSellApplication" class="summary-line">
          <span>申请赎回</span>
          <b>{{ formatQuantity(selectedSellApplication.shares) }} 份</b>
        </div>
        <div v-if="selectedPendingArrival" class="summary-line">
          <span>待到账</span>
          <b>¥{{ formatMoney(selectedPendingArrival.amount) }}</b>
        </div>
      </div>

      <form class="form-stack" @submit.prevent="submit">
        <label v-if="sheet === 'opening'" class="form-field">
          <span>起始持仓日期</span>
          <input v-model="form.businessDate" type="date" :max="today" required />
          <small>所填份额和成本在这一天成立，独立于行情日期。</small>
        </label>
        <template v-if="sheet === 'opening'">
          <label class="form-field">
            <span>基金名称</span>
            <input v-model="form.name" type="text" maxlength="50" placeholder="例如：纳指基金" />
          </label>
          <div class="form-row">
            <label class="form-field">
              <span>基金代码</span>
              <input v-model="form.code" type="text" maxlength="20" placeholder="选填" />
            </label>
            <label class="form-field">
              <span>份额类别</span>
              <input v-model="form.shareClass" type="text" maxlength="10" placeholder="A / C" />
            </label>
          </div>
          <label class="form-field">
            <span>平台</span>
            <input v-model="form.platform" type="text" maxlength="30" placeholder="选填" />
          </label>
          <div class="form-row">
            <label class="form-field">
              <span>持有份额</span>
              <input v-model="form.shares" type="text" inputmode="decimal" placeholder="0.0000" />
            </label>
            <label class="form-field">
              <span>持仓成本（元）</span>
              <input v-model="form.cost" type="text" inputmode="decimal" placeholder="0.00" />
            </label>
          </div>
          <div class="form-row">
            <label class="form-field">
              <span>最新净值</span>
              <input v-model="form.netValue" type="text" inputmode="decimal" placeholder="0.0000" />
            </label>
            <label class="form-field">
              <span>净值日期</span>
              <input v-model="form.netValueDate" type="date" :max="today" />
            </label>
          </div>
        </template>

        <template v-else-if="sheet === 'buy'">
          <label class="form-field">
            <span>基金持仓</span>
            <select v-model="form.positionId">
              <option v-for="position in overview?.positions ?? []" :key="position.id" :value="position.id">
                {{ positionLabel(position) }}
              </option>
            </select>
          </label>
          <label class="form-field">
            <span>扣款金额（元）</span>
            <input v-model="form.amount" type="text" inputmode="decimal" placeholder="0.00" />
          </label>
          <label class="form-field">
            <span>业务日期</span>
            <input v-model="form.businessDate" type="date" :max="today" />
          </label>
        </template>

        <template v-else-if="sheet === 'confirm'">
          <div class="form-row">
            <label class="form-field">
              <span>确认份额</span>
              <input v-model="form.shares" type="text" inputmode="decimal" placeholder="0.0000" />
            </label>
            <label class="form-field">
              <span>成交净值</span>
              <input v-model="form.netValue" type="text" inputmode="decimal" placeholder="0.0000" />
            </label>
          </div>
          <div class="form-row">
            <label class="form-field">
              <span>净值日期</span>
              <input v-model="form.netValueDate" type="date" :max="today" />
            </label>
            <label class="form-field">
              <span>费用（元）</span>
              <input v-model="form.fee" type="text" inputmode="decimal" placeholder="0.00" />
            </label>
          </div>
          <label class="form-field">
            <span>确认日期</span>
            <input v-model="form.businessDate" type="date" :max="today" />
            <small>确认日期不能早于买入日期，净值日期不能晚于确认日期。</small>
          </label>
        </template>

        <template v-else-if="sheet === 'sell'">
          <label class="form-field">
            <span>基金持仓</span>
            <select v-model="form.positionId">
              <option v-for="position in overview?.positions ?? []" :key="position.id" :value="position.id">
                {{ positionLabel(position) }}
              </option>
            </select>
          </label>
          <label class="form-field">
            <span>申请赎回份额</span>
            <input v-model="form.shares" type="text" inputmode="decimal" placeholder="0.0000" />
            <small>申请后份额冻结，仍计入持仓估值；确认时才减少份额并转为待到账。</small>
          </label>
          <label class="form-field">
            <span>申请日期</span>
            <input v-model="form.businessDate" type="date" :max="today" />
          </label>
        </template>

        <template v-else-if="sheet === 'sellConfirm'">
          <div class="form-row">
            <label class="form-field">
              <span>确认赎回份额</span>
              <input v-model="form.shares" type="text" inputmode="decimal" placeholder="0.0000" />
            </label>
            <label class="form-field">
              <span>成交净值</span>
              <input v-model="form.netValue" type="text" inputmode="decimal" placeholder="0.0000" />
            </label>
          </div>
          <div class="form-row">
            <label class="form-field">
              <span>净值日期</span>
              <input v-model="form.netValueDate" type="date" :max="today" />
            </label>
            <label class="form-field">
              <span>费用（元）</span>
              <input v-model="form.fee" type="text" inputmode="decimal" placeholder="0.00" />
            </label>
          </div>
          <label class="form-field">
            <span>净到账金额（元）</span>
            <input v-model="form.netAmount" type="text" inputmode="decimal" placeholder="0.00" />
          </label>
          <label class="form-field">
            <span>确认日期</span>
            <input v-model="form.businessDate" type="date" :max="today" />
            <small>确认份额不能超过申请份额；确认日期不能早于申请日期。</small>
          </label>
        </template>

        <template v-else-if="sheet === 'sellArrival'">
          <label class="form-field">
            <span>到账金额（元）</span>
            <input v-model="form.netAmount" type="text" inputmode="decimal" placeholder="0.00" />
            <small>必须与待到账金额一致；如有差额，请先更正赎回确认。</small>
          </label>
          <label class="form-field">
            <span>到账日期</span>
            <input v-model="form.businessDate" type="date" :max="today" />
          </label>
        </template>

        <template v-else>
          <label class="form-field">
            <span>基金持仓</span>
            <select v-model="form.positionId">
              <option v-for="position in overview?.positions ?? []" :key="position.id" :value="position.id">
                {{ positionLabel(position) }}
              </option>
            </select>
          </label>
          <div class="form-row">
            <label class="form-field">
              <span>最新净值</span>
              <input v-model="form.netValue" type="text" inputmode="decimal" placeholder="0.0000" />
            </label>
            <label class="form-field">
              <span>净值日期</span>
              <input v-model="form.netValueDate" type="date" :max="today" />
            </label>
          </div>
        </template>

        <label v-if="sheet !== 'quote'" class="form-field">
          <span>备注</span>
          <input v-model="form.note" type="text" maxlength="200" placeholder="选填" />
        </label>

        <button
          v-if="sheet === 'quote'"
          type="button"
          class="secondary-button"
          :disabled="isSaving || refreshingQuote || !selectedPosition?.code"
          @click="refreshQuote"
        >
          {{ refreshingQuote ? '获取中…' : '获取最新净值' }}
        </button>
        <small v-if="sheet === 'quote' && !selectedPosition?.code" class="form-note">该基金未填写基金代码，无法自动获取净值，可继续手动录入。</small>
        <CashAdjustmentLink v-if="sheet === 'buy'" v-model="form.relatedCashAdjustmentId" :business-date="form.businessDate" :cash-effect="-(parseAmount(form.amount) ?? 0)" :disabled="isSaving" />
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
.funds-page {
  padding-bottom: calc(97px + env(safe-area-inset-bottom));
}

.funds-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 10px;
  margin-bottom: 16px;
}

.funds-header h1 {
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

.funds-hero {
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

.profit {
  color: var(--color-finance-gain);
}

.loss {
  color: var(--color-finance-loss);
}

.funds-actions {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 7px;
  margin-top: 14px;
}

.funds-actions button {
  min-height: 42px;
  padding: 0 4px;
  border-radius: 9px;
  font-size: 11px;
  font-weight: 550;
}

.action-primary {
  border: 1px solid var(--color-brand);
  background: var(--color-brand);
  color: var(--color-on-brand);
}

.action-primary:hover:not(:disabled) {
  background: var(--color-brand-hover);
  border-color: var(--color-brand-hover);
}

.action-secondary {
  border: 1px solid var(--color-border-control);
  background: var(--color-bg-surface);
  color: var(--color-brand);
}

.action-secondary:hover:not(:disabled) {
  background: var(--color-brand-soft);
}

.funds-actions button:disabled {
  border-color: var(--color-border-disabled);
  background: var(--color-bg-disabled);
  color: var(--color-text-disabled);
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

.pending-list {
  display: flex;
  flex-direction: column;
  gap: 9px;
}

.pending-card {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
  padding: 13px 14px;
  border: 1px dashed var(--color-border-default);
  border-radius: 12px;
  background: var(--color-brand-soft);
}

.pending-copy {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}

.pending-copy strong {
  font-size: 13px;
  font-weight: 600;
}

.pending-copy small {
  overflow: hidden;
  color: var(--color-text-secondary);
  font-size: 10px;
  line-height: 1.5;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.pending-side {
  display: flex;
  flex: 0 0 auto;
  align-items: center;
  gap: 10px;
}

.pending-side b {
  font-size: 14px;
  font-weight: 550;
  font-variant-numeric: tabular-nums;
}

.confirm-button {
  min-height: 30px;
  padding: 0 10px;
  border: 1px solid var(--color-brand);
  border-radius: 7px;
  background: var(--color-bg-surface);
  color: var(--color-brand);
  font-size: 10px;
  font-weight: 550;
}

.position-list {
  display: flex;
  flex-direction: column;
  gap: 9px;
}

.position-card {
  padding: 14px;
  border: 1px solid var(--color-border-subtle);
  border-radius: 12px;
  background: var(--color-bg-surface);
}

.position-card header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 12px;
}

.position-card header strong {
  display: block;
  font-size: 13px;
  font-weight: 600;
}

.position-card header small {
  display: block;
  margin-top: 4px;
  color: var(--color-text-secondary);
  font-size: 10px;
}

.position-card header > b {
  font-size: 16px;
  font-weight: 550;
  font-variant-numeric: tabular-nums;
}

.position-metrics {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 10px;
  margin-top: 13px;
  padding-top: 11px;
  border-top: 1px solid var(--color-border-subtle);
}

.position-metrics > div + div {
  padding-left: 10px;
  border-left: 1px solid var(--color-border-subtle);
}

.position-metrics span {
  display: block;
  color: var(--color-text-secondary);
  font-size: 9px;
}

.position-metrics b {
  display: block;
  margin-top: 5px;
  font-size: 12px;
  font-weight: 550;
  font-variant-numeric: tabular-nums;
}

.position-card footer {
  display: flex;
  justify-content: space-between;
  gap: 10px;
  margin-top: 11px;
  color: var(--color-text-secondary);
  font-size: 9px;
}

.quote-error {
  display: block;
  margin-top: 7px;
  color: var(--color-finance-loss);
  font-size: 9px;
  line-height: 1.5;
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
  font-size: 13px;
  font-weight: 550;
  font-variant-numeric: tabular-nums;
}

.income {
  color: var(--color-finance-income);
}

.summary-note {
  margin: 14px 0 0;
  color: var(--color-text-secondary);
  font-size: 10px;
  line-height: 1.7;
  text-align: center;
}
</style>
