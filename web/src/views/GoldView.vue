<script setup lang="ts">
import CashAdjustmentLink from '../components/CashAdjustmentLink.vue'
import { computed, onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { arriveGoldSell, buyGold, fetchGoldOverview, refreshGoldQuote, saveGoldHolding, sellGold, updateGoldQuote } from '../api/gold'
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
import type { GoldActivity, GoldOverview, GoldSellSettlement, PendingGoldArrival } from '../types/gold'

type SheetKind = 'buy' | 'sell' | 'sellArrival' | 'quote' | 'holding'

const router = useRouter()
const today = todayInBusinessTimeZone()
const overview = ref<GoldOverview | null>(null)
const isLoading = ref(false)
const errorMessage = ref('')
const notice = ref('')
const sheet = ref<SheetKind | null>(null)
const requestId = ref(createRequestId())
const isSaving = ref(false)
const expectedVersion = ref(0)
const formError = ref('')
const arrivingPendingId = ref<string | null>(null)
const refreshingQuote = ref(false)
const form = reactive({
  grams: '',
  amount: '',
  netAmount: '',
  cost: '',
  quote: '',
  quoteDate: today,
  businessDate: today,
  note: '',
  relatedCashAdjustmentId: null as string | null,
  settlement: 'received' as GoldSellSettlement
})

const selectedPendingArrival = computed<PendingGoldArrival | null>(() =>
  overview.value?.pendingArrivals.find((pending) => pending.transactionId === arrivingPendingId.value) ?? null)

const sheetTitle = computed(() => {
  switch (sheet.value) {
    case 'buy':
      return '记录积存金买入'
    case 'sell':
      return '记录积存金卖出'
    case 'sellArrival':
      return '登记积存金卖到账'
    case 'quote':
      return '更新积存金报价'
    case 'holding':
      return '录入 / 核对持仓'
    default:
      return ''
  }
})
const sheetSubtitle = computed(() => {
  switch (sheet.value) {
    case 'buy':
      return '从统一现金扣款，增加克数与成本'
    case 'sell':
      return '减少克数，按收款状态转入现金或待到账'
    case 'sellArrival':
      return '待到账转入现金，不重复计算盈亏'
    case 'quote':
      return '只影响估值，不改变现金和成本'
    case 'holding':
      return '用于已有持仓或更正，不扣现金'
    default:
      return ''
  }
})
const sheetNote = computed(() => {
  switch (sheet.value) {
    case 'buy':
      return '买入金额包含费用；实际扣款与持仓估值不同时，差额体现在持有盈亏中。'
    case 'sell':
      return '按加权平均成本结转；卖出净回款与结转成本差额计入已实现盈亏。待到账不增加现金。'
    case 'sellArrival':
      return '到账金额必须与待到账金额一致，不再次确认盈亏；如有差额，请先更正卖出记录。'
    case 'quote':
      return '报价日期不能晚于今天；行情失败时保留最近有效报价。'
    case 'holding':
      return '持有克数为 0 时，持仓成本也应为 0。此操作不改变现金。'
    default:
      return ''
  }
})
const canSubmit = computed(() => {
  if (isSaving.value) {
    return false
  }

  switch (sheet.value) {
    case 'buy':
      return parseQuantity(form.grams) !== null
        && (parseAmount(form.amount) ?? 0) > 0
        && parsePrice(form.quote) !== null
    case 'sell':
      return parseQuantity(form.grams) !== null && (parseAmount(form.netAmount) ?? 0) > 0
    case 'sellArrival':
      return arrivingPendingId.value !== null && (parseAmount(form.netAmount) ?? 0) > 0
    case 'quote':
      return parsePrice(form.quote) !== null
    case 'holding':
      return parseNonNegativeQuantity(form.grams) !== null
        && parseAmount(form.cost) !== null
        && parsePrice(form.quote) !== null
    default:
      return false
  }
})

onMounted(loadOverview)

async function loadOverview(): Promise<void> {
  isLoading.value = true
  errorMessage.value = ''

  try {
    overview.value = await fetchGoldOverview()
  } catch (error) {
    overview.value = null

    if (error instanceof ApiError && error.status === 404) {
      await router.replace({ name: 'setup' })
      return
    }

    errorMessage.value = getApiErrorMessage(error, '积存金数据加载失败，请重试。')
  } finally {
    isLoading.value = false
  }
}

function openSheet(kind: SheetKind): void {
  expectedVersion.value = overview.value?.version ?? 0
  sheet.value = kind
  arrivingPendingId.value = null
  requestId.value = createRequestId()
  formError.value = ''
  form.note = ''
  form.relatedCashAdjustmentId = null
  form.businessDate = today
  form.quoteDate = overview.value?.quoteDate ?? today
  form.quote = overview.value?.quote ? String(overview.value.quote) : ''
  form.grams = ''
  form.amount = ''
  form.netAmount = ''
  form.cost = ''
  form.settlement = 'received'

  if (kind === 'holding' && overview.value?.isConfigured) {
    form.grams = String(overview.value.grams)
    form.cost = String(overview.value.cost)
  }
}

function openArrivalSheet(pending: PendingGoldArrival): void {
  openSheet('sellArrival')
  arrivingPendingId.value = pending.transactionId
  form.netAmount = String(pending.amount)
  form.note = pending.note ?? ''
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
    if (sheet.value === 'buy') {
      await submitBuy()
    } else if (sheet.value === 'sell') {
      await submitSell()
    } else if (sheet.value === 'sellArrival') {
      await submitSellArrival()
    } else if (sheet.value === 'quote') {
      await submitQuote()
    } else {
      await submitHolding()
    }
  } catch (error) {
    if (error instanceof ApiError && error.status === 409) {
      await loadOverview()
      expectedVersion.value = overview.value?.version ?? expectedVersion.value
      requestId.value = createRequestId()
    }
    formError.value = getApiErrorMessage(error, '保存失败，请重试。')
  } finally {
    isSaving.value = false
  }
}

async function submitBuy(): Promise<void> {
  const grams = parseQuantity(form.grams)
  const amount = parseAmount(form.amount)
  const quote = parsePrice(form.quote)

  if (grams === null || amount === null || amount <= 0 || quote === null) {
    formError.value = '请填写大于 0 的克数、扣款金额和报价。'
    return
  }


  const dateError = validateBusinessDate()

  if (dateError) {
    formError.value = dateError
    return
  }

  const quoteDateError = validateValueDate(form.quoteDate, '报价日期')

  if (quoteDateError) {
    formError.value = quoteDateError
    return
  }

  isSaving.value = true
  const result = await buyGold({
    grams,
    amount,
    quote,
    quoteDate: form.quoteDate,
    businessDate: form.businessDate,
    note: form.note.trim(),
    relatedCashAdjustmentId: form.relatedCashAdjustmentId,
    requestId: requestId.value
  })
  notice.value = `已买入 ${formatQuantity(result.activity.grams)} 克，当前现金 ¥${formatMoney(result.cashBalance)}。`
  sheet.value = null
  await loadOverview()
}

async function submitSell(): Promise<void> {
  const grams = parseQuantity(form.grams)
  const netAmount = parseAmount(form.netAmount)

  if (grams === null || netAmount === null || netAmount <= 0) {
    formError.value = '请填写大于 0 的卖出克数和净回款金额。'
    return
  }


  const dateError = validateBusinessDate()

  if (dateError) {
    formError.value = dateError
    return
  }

  isSaving.value = true
  const result = await sellGold({
    grams,
    netAmount,
    businessDate: form.businessDate,
    note: form.note.trim(),
    relatedCashAdjustmentId: form.settlement === 'received' ? form.relatedCashAdjustmentId : null,
    requestId: requestId.value,
    settlement: form.settlement
  })
  notice.value = form.settlement === 'received'
    ? `已卖出 ${formatQuantity(result.activity.grams)} 克，当前现金 ¥${formatMoney(result.cashBalance)}。`
    : `已记录 ${formatQuantity(result.activity.grams)} 克卖出待到账 ¥${formatMoney(result.activity.amount)}，到账后转入现金。`
  sheet.value = null
  await loadOverview()
}

async function submitSellArrival(): Promise<void> {
  const pending = selectedPendingArrival.value
  const amount = parseAmount(form.netAmount)

  if (!pending) {
    formError.value = '积存金卖出待到账记录不存在，请刷新后重试。'
    return
  }

  if (amount === null || amount <= 0) {
    formError.value = '请填写有效的到账金额。'
    return
  }

  if (amount !== pending.amount) {
    formError.value = '到账金额必须与待到账金额一致；如有差额，请先更正卖出记录。'
    return
  }

  const dateError = validateBusinessDate()

  if (dateError) {
    formError.value = dateError
    return
  }

  if (form.businessDate < pending.businessDate) {
    formError.value = '到账日期不能早于卖出日期。'
    return
  }

  isSaving.value = true
  const result = await arriveGoldSell(pending.transactionId, {
    amount,
    businessDate: form.businessDate,
    note: form.note.trim(),
    requestId: requestId.value
  })
  notice.value = `积存金卖到账 ¥${formatMoney(result.activity.amount)}，当前现金 ¥${formatMoney(result.cashBalance)}。`
  sheet.value = null
  await loadOverview()
}

async function submitQuote(): Promise<void> {
  const quote = parsePrice(form.quote)

  if (quote === null) {
    formError.value = '请输入大于 0 的报价，最多四位小数。'
    return
  }

  const dateError = validateValueDate(form.quoteDate, '报价日期')

  if (dateError) {
    formError.value = dateError
    return
  }

  isSaving.value = true
  await updateGoldQuote({
    quote,
    quoteDate: form.quoteDate,
    requestId: requestId.value
  })
  notice.value = `报价已更新为 ¥${formatMoney(quote)} / 克。`
  sheet.value = null
  await loadOverview()
}

async function submitHolding(): Promise<void> {
  const grams = parseNonNegativeQuantity(form.grams)
  const cost = parseAmount(form.cost)
  const quote = parsePrice(form.quote)

  if (grams === null || cost === null || quote === null) {
    formError.value = '请填写有效的克数、成本和报价。'
    return
  }

  if (grams === 0 && cost !== 0) {
    formError.value = '持有克数为 0 时，持仓成本也应为 0。'
    return
  }

  const dateError = validateValueDate(form.quoteDate, '报价日期')

  if (dateError) {
    formError.value = dateError
    return
  }

  const holdingDateError = validateBusinessDate()
  if (holdingDateError) { formError.value = holdingDateError; return }

  isSaving.value = true
  await saveGoldHolding({
    expectedVersion: expectedVersion.value,
    grams,
    cost,
    quote,
    quoteDate: form.quoteDate,
    note: form.note.trim(),
    holdingDate: form.businessDate,
    requestId: requestId.value
  })
  notice.value = '积存金持仓已保存。'
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

function activityTitle(activity: GoldActivity): string {
  switch (activity.type) {
    case 'buy':
      return '积存金买入'
    case 'sell':
      return '积存金卖出'
    case 'sellPending':
      return '积存金卖出待到账'
    case 'sellArrival':
      return '积存金卖到账'
    case 'quote':
      return '更新报价'
    case 'correction':
      return '核对持仓'
    default:
      return '积存金记录'
  }
}

function activityNote(activity: GoldActivity): string {
  if (activity.note) {
    return activity.note
  }

  switch (activity.type) {
    case 'buy':
      return '现金 → 积存金'
    case 'sell':
      return '积存金 → 现金'
    case 'sellPending':
      return '积存金 → 待到账'
    case 'sellArrival':
      return '待到账 → 现金'
    case 'quote':
      return '仅更新估值'
    default:
      return '已有持仓更正'
  }
}

function activityAmountText(activity: GoldActivity): string {
  if (activity.type === 'buy' || activity.type === 'sell' || activity.type === 'sellArrival') {
    return formatSignedMoney(activity.cashEffect)
  }

  if (activity.type === 'sellPending') {
    return `待到账 ${formatMoney(activity.amount)}`
  }

  if (activity.type === 'quote') {
    return `${formatMoney(activity.quote)} / 克`
  }

  return `成本 ${formatMoney(activity.amount)}`
}

function activityMeta(activity: GoldActivity): string {
  if (activity.type === 'sellArrival') {
    return `到账 ${formatMoney(activity.amount)} · ${formatBusinessDate(activity.businessDate)}`
  }

  if (activity.type === 'buy' || activity.type === 'sell' || activity.type === 'sellPending') {
    return `${formatQuantity(activity.grams)} 克 · ${formatBusinessDate(activity.businessDate)}`
  }

  if (activity.type === 'quote') {
    return `报价日 ${formatBusinessDate(activity.businessDate)}`
  }

  return `${formatQuantity(activity.grams)} 克 · ${formatBusinessDate(activity.businessDate)}`
}

function quoteSourceLabel(source: string | null): string {
  switch (source) {
    case 'sina_shfe_au0':
      return '上期所黄金连续（参考）'
    case 'manual':
      return '手动录入'
    default:
      return source ?? '手动录入'
  }
}

async function refreshQuote(): Promise<void> {
  refreshingQuote.value = true
  formError.value = ''

  try {
    const result = await refreshGoldQuote()
    notice.value = result.updated
      ? `已获取参考报价 ${formatMoney(result.quote)} 元/克（${formatBusinessDate(result.quoteDate)}）。`
      : `参考报价已是最新 ${formatMoney(result.quote)} 元/克（${formatBusinessDate(result.quoteDate)}）。`
    sheet.value = null
    await loadOverview()
  } catch (error) {
    formError.value = getApiErrorMessage(error, '获取参考报价失败，已保留最近有效值。')
  } finally {
    refreshingQuote.value = false
  }
}

function goBack(): void {
  void router.push({ name: 'home' })
}
</script>

<template>
  <main class="page gold-page with-nav">
    <header class="gold-header">
      <button type="button" class="back-link" @click="goBack">‹ 总览</button>
      <h1>平台积存金</h1>
      <RouterLink class="back-link" to="/history">历史核对</RouterLink>
    </header>

    <p v-if="isLoading && !overview" class="state">正在读取积存金…</p>

    <section v-else-if="errorMessage" class="state">
      <p>{{ errorMessage }}</p>
      <button type="button" class="secondary-button" @click="loadOverview">重新加载</button>
    </section>

    <template v-else-if="overview">
      <section class="gold-hero">
        <p class="hero-label">
          积存金资产
          <span class="hero-tag">人民币</span>
        </p>
        <h2 class="hero-balance"><span>¥</span>{{ overview.hasEffectiveQuote ? formatMoney(overview.value) : '行情待补' }}</h2>
        <p class="hero-meta">
          持仓估值 {{ formatQuantity(overview.grams) }} 克 ×
          {{ overview.hasEffectiveQuote ? formatMoney(overview.quote) : '行情待补' }} 元/克
          <template v-if="overview.pendingArrivalAmount > 0"> · 含待到账 ¥{{ formatMoney(overview.pendingArrivalAmount) }}</template>
        </p>
        <div class="hero-metrics">
          <div>
            <span>持有克数</span>
            <b>{{ formatQuantity(overview.grams) }}</b>
          </div>
          <div>
            <span>持仓成本</span>
            <b>{{ formatMoney(overview.cost) }}</b>
          </div>
          <div>
            <span>持有盈亏</span>
            <b :class="{ profit: overview.holdingProfit > 0, loss: overview.holdingProfit < 0 }">
              {{ overview.hasEffectiveQuote ? formatSignedMoney(overview.holdingProfit) : '行情待补' }}
            </b>
          </div>
        </div>
      </section>

      <div class="gold-actions">
        <button type="button" class="action-primary" @click="openSheet('buy')">买入</button>
        <button
          type="button"
          class="action-secondary"
          :disabled="!overview.isConfigured || overview.grams <= 0"
          @click="openSheet('sell')"
        >
          卖出
        </button>
        <button type="button" class="action-secondary" @click="openSheet('quote')">更新报价</button>
        <button type="button" class="action-secondary" @click="openSheet('holding')">录入 / 核对持仓</button>
      </div>

      <p v-if="notice" class="notice" role="status">{{ notice }}</p>

      <template v-if="overview.pendingArrivals.length">
        <div class="section-head">
          <h2>卖出待到账</h2>
          <span class="meta">{{ overview.pendingArrivals.length }} 笔 · ¥{{ formatMoney(overview.pendingArrivalAmount) }}</span>
        </div>
        <section class="pending-list">
          <article v-for="pending in overview.pendingArrivals" :key="pending.transactionId" class="pending-card">
            <div class="pending-copy">
              <strong>{{ formatQuantity(pending.grams) }} 克</strong>
              <small>
                {{ formatBusinessDate(pending.businessDate) }}
                <template v-if="pending.note"> · {{ pending.note }}</template>
              </small>
            </div>
            <div class="pending-side">
              <b>¥{{ formatMoney(pending.amount) }}</b>
              <button type="button" class="confirm-button" @click="openArrivalSheet(pending)">登记到账</button>
            </div>
          </article>
        </section>
      </template>

      <div class="quote-panel">
        <div>
          <span>估值报价</span>
          <b>{{ overview.hasEffectiveQuote ? `¥${formatMoney(overview.quote)} / 克` : '行情待补' }}</b>
          <small v-if="overview.hasEffectiveQuote && overview.quoteDate">{{ formatBusinessDate(overview.quoteDate) }} · {{ quoteSourceLabel(overview.lastQuoteSource) }}</small>
          <small v-else>尚未设置报价</small>
          <small v-if="overview.lastQuoteError" class="quote-error">最近获取失败：{{ overview.lastQuoteError }}</small>
        </div>
        <div>
          <span>平均持仓成本</span>
          <b>{{ overview.averageCost === null ? '—' : `¥${formatMoney(overview.averageCost)} / 克` }}</b>
          <small>已实现盈亏 {{ formatSignedMoney(overview.realizedProfit) }}</small>
        </div>
      </div>
      <p class="summary-note">自动获取的是上期所黄金连续参考价，不是平台积存金报价；可继续手动录入平台报价。</p>

      <p v-if="!overview.isConfigured" class="hint">
        尚未记录积存金持仓。可先录入已有持仓，或直接记录买入。
      </p>

      <p v-if="!overview.historyConfirmed || !overview.hasEffectiveQuote" class="hint">持仓日期或行情依据待核对，当前缓存仅供核对，请到历史核对确认。</p>
      <div class="section-head">
        <h2>交易与持仓记录</h2>
        <span class="meta">按业务日期</span>
      </div>

      <section v-if="overview.activities.length" class="activity-card">
        <div v-for="activity in overview.activities" :key="activity.id" class="activity-row">
          <span class="activity-mark">{{ activity.type === 'buy' ? '买' : activity.type === 'sell' ? '卖' : activity.type === 'sellPending' ? '待' : activity.type === 'sellArrival' ? '到' : activity.type === 'quote' ? '价' : '校' }}</span>
          <span class="activity-copy">
            <strong>{{ activityTitle(activity) }}</strong>
            <small>{{ activityNote(activity) }} · {{ activityMeta(activity) }}</small>
          </span>
          <b class="activity-value" :class="{ income: activity.cashEffect > 0, transfer: activity.cashEffect < 0 }">
            {{ activityAmountText(activity) }}
          </b>
        </div>
      </section>

      <div v-else class="empty-state">
        <p>还没有积存金记录</p>
        <button type="button" class="primary-button" @click="openSheet('holding')">录入已有持仓</button>
      </div>

      <p class="summary-note">
        买入按实际扣款增加克数与成本；卖出按加权平均成本结转，待到账收款后转入现金。报价更新只影响估值。
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
        <div class="summary-line">
          <span>当前持有</span>
          <b>{{ formatQuantity(overview?.grams) }} 克</b>
        </div>
        <div v-if="selectedPendingArrival" class="summary-line">
          <span>卖出待到账</span>
          <b>¥{{ formatMoney(selectedPendingArrival.amount) }}</b>
        </div>
      </div>

      <form class="form-stack" @submit.prevent="submit">
        <label v-if="sheet === 'holding'" class="form-field">
          <span>持仓核对日期</span>
          <input v-model="form.businessDate" type="date" :max="today" required />
          <small>所填份额和成本在这一天成立，独立于行情日期。</small>
        </label>
        <template v-if="sheet === 'buy'">
          <label class="form-field">
            <span>买入克数</span>
            <input
              v-model="form.grams"
              type="text"
              inputmode="decimal"
              autocomplete="off"
              placeholder="0.0000"
              @input="formError = ''"
            />
          </label>
          <label class="form-field">
            <span>实际扣款金额（元，含费用）</span>
            <input
              v-model="form.amount"
              type="text"
              inputmode="decimal"
              autocomplete="off"
              placeholder="0.00"
              @input="formError = ''"
            />
          </label>
          <div class="form-row">
            <label class="form-field">
              <span>当前报价（元/克）</span>
              <input v-model="form.quote" type="text" inputmode="decimal" placeholder="0.0000" />
            </label>
            <label class="form-field">
              <span>报价日期</span>
              <input v-model="form.quoteDate" type="date" :max="today" />
            </label>
          </div>
          <label class="form-field">
            <span>业务日期</span>
            <input v-model="form.businessDate" type="date" :max="today" />
          </label>
        </template>

        <template v-else-if="sheet === 'sell'">
          <label class="form-field">
            <span>卖出克数</span>
            <input
              v-model="form.grams"
              type="text"
              inputmode="decimal"
              autocomplete="off"
              placeholder="0.0000"
              @input="formError = ''"
            />
          </label>
          <label class="form-field">
            <span>净回款金额（元，已扣费用）</span>
            <input
              v-model="form.netAmount"
              type="text"
              inputmode="decimal"
              autocomplete="off"
              placeholder="0.00"
              @input="formError = ''"
            />
          </label>
          <label class="form-field">
            <span>收款状态</span>
            <select v-model="form.settlement">
              <option value="received">已到账</option>
              <option value="pendingArrival">待到账</option>
            </select>
            <small>待到账只减少克数并增加待到账资产，现金不变；到账时再转入现金。</small>
          </label>
          <label class="form-field">
            <span>业务日期</span>
            <input v-model="form.businessDate" type="date" :max="today" />
          </label>
        </template>

        <template v-else-if="sheet === 'sellArrival'">
          <label class="form-field">
            <span>到账金额（元）</span>
            <input
              v-model="form.netAmount"
              type="text"
              inputmode="decimal"
              autocomplete="off"
              placeholder="0.00"
              @input="formError = ''"
            />
            <small>必须与待到账金额一致；如有差额，请先更正卖出记录。</small>
          </label>
          <label class="form-field">
            <span>到账日期</span>
            <input v-model="form.businessDate" type="date" :max="today" />
          </label>
        </template>

        <template v-else-if="sheet === 'quote'">
          <label class="form-field">
            <span>估值报价（元/克）</span>
            <input
              v-model="form.quote"
              type="text"
              inputmode="decimal"
              autocomplete="off"
              placeholder="0.0000"
              @input="formError = ''"
            />
          </label>
          <label class="form-field">
            <span>报价日期</span>
            <input v-model="form.quoteDate" type="date" :max="today" />
          </label>
        </template>

        <template v-else>
          <label class="form-field">
            <span>实际持有克数</span>
            <input
              v-model="form.grams"
              type="text"
              inputmode="decimal"
              autocomplete="off"
              placeholder="0.0000"
              @input="formError = ''"
            />
          </label>
          <label class="form-field">
            <span>对应持仓成本（元）</span>
            <input
              v-model="form.cost"
              type="text"
              inputmode="decimal"
              autocomplete="off"
              placeholder="0.00"
              @input="formError = ''"
            />
          </label>
          <div class="form-row">
            <label class="form-field">
              <span>估值报价（元/克）</span>
              <input v-model="form.quote" type="text" inputmode="decimal" placeholder="0.0000" />
            </label>
            <label class="form-field">
              <span>报价日期</span>
              <input v-model="form.quoteDate" type="date" :max="today" />
            </label>
          </div>
        </template>

        <label class="form-field">
          <span>备注</span>
          <input v-model="form.note" type="text" maxlength="200" placeholder="选填" />
        </label>

        <button
          v-if="sheet === 'quote'"
          type="button"
          class="secondary-button"
          :disabled="isSaving || refreshingQuote"
          @click="refreshQuote"
        >
          {{ refreshingQuote ? '获取中…' : '获取参考报价' }}
        </button>
        <CashAdjustmentLink v-if="sheet === 'buy' || (sheet === 'sell' && form.settlement === 'received')" v-model="form.relatedCashAdjustmentId" :business-date="form.businessDate" :cash-effect="sheet === 'buy' ? -(parseAmount(form.amount) ?? 0) : (parseAmount(form.netAmount) ?? 0)" :disabled="isSaving" />
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
.gold-page {
  padding-bottom: calc(97px + env(safe-area-inset-bottom));
}

.gold-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 10px;
  margin-bottom: 16px;
}

.gold-header h1 {
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

.gold-hero {
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

.gold-actions {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 9px;
  margin-top: 14px;
}

.gold-actions button {
  min-height: 42px;
  border-radius: 9px;
  font-size: 12px;
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

.action-secondary:hover:not(:disabled) {
  background: var(--color-brand-soft);
}

.action-secondary:disabled {
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

.quote-panel {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
  margin-top: 16px;
  padding: 14px;
  border: 1px solid var(--color-border-subtle);
  border-radius: 12px;
  background: var(--color-bg-surface);
}

.quote-panel > div + div {
  padding-left: 12px;
  border-left: 1px solid var(--color-border-subtle);
}

.quote-panel span {
  display: block;
  color: var(--color-text-secondary);
  font-size: 10px;
}

.quote-panel b {
  display: block;
  margin-top: 6px;
  font-size: 14px;
  font-weight: 550;
  font-variant-numeric: tabular-nums;
}

.quote-panel small {
  display: block;
  margin-top: 5px;
  color: var(--color-text-secondary);
  font-size: 9px;
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

.quote-error {
  display: block;
  color: var(--color-finance-loss);
  font-size: 9px;
  line-height: 1.5;
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

.transfer {
  color: var(--color-text-primary);
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

.sheet-summary {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.summary-note {
  text-align: center;
}
</style>
