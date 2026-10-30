<script setup lang="ts">
import Decimal from 'decimal.js'
import { computed, onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ApiError, getApiErrorMessage } from '../api/client'
import { buyStock, fetchStockOverview, refreshStockQuote, refreshStockQuotes, saveStockHolding, searchStocks, sellStock } from '../api/stocks'
import { fetchLedger, updateAssetCoverage } from '../api/ledger'
import AppNav from '../components/AppNav.vue'
import BackButton from '../components/BackButton.vue'
import BottomSheet from '../components/BottomSheet.vue'
import {
  amountTone,
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
  StockActivity,
  StockMarket,
  StockOverview,
  StockPosition,
  StockSearchResult
} from '../types/stock'

type SheetKind = 'buy' | 'sell' | 'holding' | 'detail'

const router = useRouter()
const today = todayInBusinessTimeZone()
const overview = ref<StockOverview | null>(null)
const isLoading = ref(false)
const isRefreshing = ref(false)
const errorMessage = ref('')
const notice = ref('')
const sheet = ref<SheetKind | null>(null)
const selectedPosition = ref<StockPosition | null>(null)
const selectedStock = ref<StockSearchResult | null>(null)
const searchQuery = ref('')
const searchResults = ref<StockSearchResult[]>([])
const searching = ref(false)
const searched = ref(false)
const formError = ref('')
const isSaving = ref(false)
const requestId = ref(createRequestId())
const expectedVersion = ref(0)
const form = reactive({
  shares: '',
  price: '',
  cost: '',
  businessDate: today,
  priceDate: today,
  note: ''
})

const marketLabels: Record<StockMarket, string> = {
  shanghai: '沪市',
  shenzhen: '深市',
  beijing: '北交所'
}
const marketOrder: StockMarket[] = ['shanghai', 'shenzhen', 'beijing']

type SortKey = 'value' | 'profit' | 'name'

const sortOptions: { value: SortKey; label: string }[] = [
  { value: 'value', label: '市值' },
  { value: 'profit', label: '盈亏' },
  { value: 'name', label: '名称' }
]
const sortKey = ref<SortKey>('value')

// 清仓后的 0 股持仓保留用于历史，但不应该再占用“持有 N 只”和行情刷新。
const activePositions = computed(() => (overview.value?.positions ?? [])
  .filter((position) => position.shares > 0))
const closedPositions = computed(() => (overview.value?.positions ?? [])
  .filter((position) => position.shares <= 0))

const sortedPositions = computed(() => {
  const positions = [...activePositions.value]

  switch (sortKey.value) {
    case 'profit':
      return positions.sort((first, second) =>
        (second.todayProfit ?? Number.NEGATIVE_INFINITY) - (first.todayProfit ?? Number.NEGATIVE_INFINITY))
    case 'name':
      return positions.sort((first, second) => first.name.localeCompare(second.name, 'zh-Hans-CN'))
    default:
      return positions.sort((first, second) => second.value - first.value)
  }
})

const groupedPositions = computed(() => marketOrder
  .map((market) => ({
    market,
    label: marketLabels[market],
    positions: sortedPositions.value.filter((position) => position.market === market),
    value: sortedPositions.value
      .filter((position) => position.market === market)
      .reduce((total, position) => total + position.value, 0),
    todayProfit: sortedPositions.value
      .filter((position) => position.market === market && position.todayProfit !== null)
      .reduce((total, position) => total + (position.todayProfit ?? 0), 0)
  }))
  .filter((group) => group.positions.length > 0))

const detailActivities = computed(() => selectedPosition.value
  ? (overview.value?.activities ?? []).filter((activity) => activity.positionId === selectedPosition.value?.id)
  : [])

const todayProfitLabel = computed(() => {
  const date = overview.value?.todayProfitDate

  return date && date !== today
    ? `最近交易日盈亏 · ${formatBusinessDate(date)}`
    : '今日盈亏'
})

const todayProfitText = computed(() => {
  const data = overview.value

  if (!data || data.todayProfit === null) {
    return '—'
  }

  return `${formatSignedMoney(data.todayProfit)}${data.todayProfitComplete ? '' : '（估算）'}`
})

const totalTodayPercent = computed(() => {
  const data = overview.value

  if (!data || data.todayProfit === null) {
    return null
  }

  const previousValue = data.totalValue - data.todayProfit

  if (previousValue <= 0) {
    return null
  }

  return data.todayProfit / previousValue * 100
})

const shareMax = computed(() => selectedPosition.value?.shares ?? 0)

// 买卖共用：成交金额即现金变动金额，不再单独录入费用。
const tradeGross = computed(() => {
  const shares = parseQuantity(form.shares)
  const price = parsePrice(form.price)

  if (shares === null || price === null) {
    return null
  }

  return new Decimal(shares).times(price).toDecimalPlaces(2, Decimal.ROUND_HALF_UP)
})

// 券商习惯：买入前先看还能买多少，按输入价粗略估算。
const buyMaxShares = computed(() => {
  const data = overview.value
  const price = parsePrice(form.price)

  if (!data || price === null || price <= 0) {
    return null
  }

  const shares = new Decimal(data.cashBalance).div(price).toDecimalPlaces(0, Decimal.ROUND_DOWN)

  return shares.isNegative() ? 0 : shares.toNumber()
})

// 不足一手时不显示满手数，避免「满手约 0 手」这类无用提示。
const buyMaxLots = computed(() =>
  buyMaxShares.value === null ? 0 : Math.floor(buyMaxShares.value / 100))

const sellEstimatedProfit = computed(() => {
  const position = selectedPosition.value
  const gross = tradeGross.value
  const shares = parseQuantity(form.shares)

  if (!position || !gross || shares === null || position.shares <= 0 || shares > position.shares) {
    return null
  }

  const reducedCost = new Decimal(position.cost)
    .times(shares)
    .div(position.shares)
    .toDecimalPlaces(2, Decimal.ROUND_HALF_UP)

  return gross.minus(reducedCost).toNumber()
})

const sheetTitle = computed(() => {
  switch (sheet.value) {
    case 'buy':
      return selectedPosition.value ? `买入 ${selectedPosition.value.name}` : '买入股票'
    case 'sell':
      return selectedPosition.value ? `卖出 ${selectedPosition.value.name}` : '卖出股票'
    case 'holding':
      return selectedPosition.value ? '核对持仓' : '录入股票持仓'
    case 'detail':
      return selectedPosition.value?.name ?? '股票详情'
    default:
      return ''
  }
})

const sheetSubtitle = computed(() => {
  switch (sheet.value) {
    case 'buy':
      return '从统一现金扣款，成交后增加持股与成本'
    case 'sell':
      return '减少持股，成交金额转入统一现金'
    case 'holding':
      return '用于录入已有持仓或修正数量与成本，不改变现金'
    case 'detail':
      return selectedPosition.value ? `${marketLabels[selectedPosition.value.market]} · ${selectedPosition.value.code}` : ''
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
      return selectedStock.value !== null
        && parseQuantity(form.shares) !== null
        && parsePrice(form.price) !== null
    case 'sell': {
      const shares = parseQuantity(form.shares)
      return selectedPosition.value !== null
        && shares !== null
        && shares <= shareMax.value
        && parsePrice(form.price) !== null
    }
    case 'holding':
      return selectedStock.value !== null
        && parseNonNegativeQuantity(form.shares) !== null
        && parseAmount(form.cost) !== null
        && parsePrice(form.price) !== null
        && form.businessDate.length > 0
        && form.priceDate.length > 0
    default:
      return false
  }
})

onMounted(async () => {
  await loadOverview()
  await refreshOnEnter()
})

async function loadOverview(): Promise<void> {
  isLoading.value = true
  errorMessage.value = ''

  try {
    overview.value = await fetchStockOverview()
  } catch (error) {
    overview.value = null

    if (error instanceof ApiError && error.status === 404) {
      await router.replace({ name: 'setup' })
      return
    }

    errorMessage.value = getApiErrorMessage(error, '股票数据加载失败，请重试。')
  } finally {
    isLoading.value = false
  }
}

async function refreshOnEnter(): Promise<void> {
  if (!overview.value || activePositions.value.length === 0) {
    return
  }

  try {
    await refreshStockQuotes()
  } catch {
    // 失败原因由持仓的行情状态记录，下面重新读取以展示缓存值。
  }

  await loadOverview()
}

async function refreshQuotes(): Promise<void> {
  if (isRefreshing.value) {
    return
  }

  isRefreshing.value = true
  errorMessage.value = ''
  notice.value = ''

  try {
    const result = await refreshStockQuotes()
    notice.value = result.failed > 0
      ? `行情刷新完成：成功 ${result.succeeded} 只，失败 ${result.failed} 只${result.errors[0] ? `；${result.errors[0]}` : ''}`
      : `行情刷新完成：更新 ${result.updated} 只股票。`
    await loadOverview()
  } catch (error) {
    errorMessage.value = getApiErrorMessage(error, '行情刷新失败，请稍后重试。')
  } finally {
    isRefreshing.value = false
  }
}

function openSheet(kind: SheetKind, position: StockPosition | null = null): void {
  selectedPosition.value = position
  selectedStock.value = position
    ? {
        market: position.market,
        code: position.code,
        name: position.name,
        currency: position.currency,
        price: position.price,
        priceDate: position.priceDate
      }
    : null
  sheet.value = kind
  formError.value = ''
  requestId.value = createRequestId()
  expectedVersion.value = position?.version ?? 0
  searchQuery.value = ''
  searchResults.value = []
  searched.value = false
  form.shares = ''
  form.price = position ? String(position.price) : ''
  form.cost = position ? String(position.cost) : ''
  form.businessDate = today
  form.priceDate = position?.priceDate ?? today
  form.note = ''

  if (kind === 'holding' && position) {
    form.shares = String(position.shares)
  }
}

function openDetail(position: StockPosition): void {
  openSheet('detail', position)
}

function closeSheet(): void {
  if (!isSaving.value) {
    sheet.value = null
    selectedPosition.value = null
    selectedStock.value = null
    searchResults.value = []
  }
}

async function runSearch(): Promise<void> {
  const query = searchQuery.value.trim()

  if (!query || searching.value) {
    return
  }

  searching.value = true
  searched.value = true
  searchResults.value = []
  formError.value = ''

  try {
    searchResults.value = await searchStocks(query)
  } catch (error) {
    formError.value = getApiErrorMessage(error, '股票搜索失败，请稍后重试。')
  } finally {
    searching.value = false
  }
}

function selectStock(result: StockSearchResult): void {
  selectedStock.value = result
  searchResults.value = []
  searchQuery.value = ''
  formError.value = ''

  if (!form.price && result.price !== null) {
    form.price = String(result.price)
  }
}

function clearSelectedStock(): void {
  if (selectedPosition.value) {
    return
  }

  selectedStock.value = null
  form.price = ''
  formError.value = ''
}

async function submit(): Promise<void> {
  if (!sheet.value) {
    return
  }

  formError.value = ''

  try {
    if (sheet.value === 'buy') {
      await submitBuy()
    } else if (sheet.value === 'sell') {
      await submitSell()
    } else if (sheet.value === 'holding') {
      await submitHolding()
    }
  } catch (error) {
    if (error instanceof ApiError && error.status === 409) {
      await loadOverview()
      expectedVersion.value = selectedPosition.value?.version ?? expectedVersion.value
      requestId.value = createRequestId()
    }

    formError.value = getApiErrorMessage(error, '保存失败，请重试。')
  } finally {
    isSaving.value = false
  }
}

async function submitBuy(): Promise<void> {
  const stock = selectedStock.value
  const shares = parseQuantity(form.shares)
  const price = parsePrice(form.price)

  if (!stock || shares === null || price === null) {
    formError.value = '请填写有效的股票、数量和成交价。'
    return
  }

  isSaving.value = true
  const result = await buyStock({
    market: stock.market,
    code: stock.code,
    name: stock.name,
    shares,
    price,
    businessDate: form.businessDate,
    note: form.note.trim(),
    requestId: requestId.value
  })
  notice.value = `已买入 ${formatShares(result.activity.shares)} 股，当前现金 ¥${formatMoney(result.cashBalance)}。`
  sheet.value = null

  // 当天买入还没有行情记录时，今日盈亏会空着；成交成功后顺手拉一次行情。
  try {
    await refreshStockQuote(result.position.id)
  } catch {
    // 行情失败不影响成交结果，持仓仍展示缓存估值。
  }

  await loadOverview()
}

async function submitSell(): Promise<void> {
  const position = selectedPosition.value
  const shares = parseQuantity(form.shares)
  const price = parsePrice(form.price)

  if (!position || shares === null || price === null) {
    formError.value = '请填写有效的卖出数量和成交价。'
    return
  }

  if (shares > position.shares) {
    formError.value = '卖出数量不能超过当前持有数量。'
    return
  }

  isSaving.value = true
  const result = await sellStock({
    positionId: position.id,
    shares,
    price,
    businessDate: form.businessDate,
    note: form.note.trim(),
    requestId: requestId.value
  })
  notice.value = `已卖出 ${formatShares(result.activity.shares)} 股，当前现金 ¥${formatMoney(result.cashBalance)}。`
  sheet.value = null
  await loadOverview()
}

async function submitHolding(): Promise<void> {
  const stock = selectedStock.value
  const shares = parseNonNegativeQuantity(form.shares)
  const cost = parseAmount(form.cost)
  const price = parsePrice(form.price)

  if (!stock || shares === null || cost === null || cost < 0 || price === null) {
    formError.value = '请填写有效的持股数量、成本和现价。'
    return
  }

  if (shares === 0 && cost !== 0) {
    formError.value = '持有数量为 0 时，持仓成本也应为 0。'
    return
  }

  isSaving.value = true
  await saveStockHolding({
    market: stock.market,
    code: stock.code,
    name: stock.name,
    shares,
    cost,
    price,
    priceDate: form.priceDate,
    holdingDate: form.businessDate,
    note: form.note.trim(),
    requestId: requestId.value,
    expectedVersion: expectedVersion.value,
    positionId: selectedPosition.value?.id
  })
  notice.value = '股票持仓已保存。'
  sheet.value = null
  await loadOverview()
}

function formatShares(value: number | null | undefined): string {
  if (value === null || value === undefined) {
    return '—'
  }

  return formatQuantity(value).replace(/\.?0+$/, '') || '0'
}

function positionTodayText(position: StockPosition): string {
  return position.todayProfit === null ? '—' : formatSignedMoney(position.todayProfit)
}

// 券商习惯的当日涨跌幅，以昨收为基准；没有昨收就不造一个数字出来。
function positionChangePercent(position: StockPosition): number | null {
  const previousClose = position.previousClose

  if (previousClose === null || previousClose <= 0) {
    return null
  }

  return (position.price - previousClose) / previousClose * 100
}

function formatPercent(value: number | null): string {
  if (value === null) {
    return ''
  }

  const sign = value > 0 ? '+' : value < 0 ? '−' : ''

  return `${sign}${Math.abs(value).toFixed(2)}%`
}

function positionAverageCostText(position: StockPosition): string {
  return position.averageCost === null ? '—' : `¥${formatMoney(position.averageCost)}`
}

async function markNotHeld(): Promise<void> {
  try {
    isSaving.value = true
    const ledger = await fetchLedger()
    await updateAssetCoverage({
      fundNotHeld: ledger.fundNotHeld,
      providentFundNotHeld: ledger.providentFundNotHeld,
      goldNotHeld: ledger.goldNotHeld,
      stockNotHeld: true
    })
    await router.push({ name: 'assets' })
  } catch (error) {
    errorMessage.value = getApiErrorMessage(error, '标记失败，请稍后重试。')
  } finally {
    isSaving.value = false
  }
}

function positionTodayLabel(position: StockPosition): string {
  const prefix = overview.value?.todayProfitDate && overview.value.todayProfitDate !== today
    ? '最近'
    : '今日'

  return position.todayProfit === null ? `${prefix} —` : `${prefix} ${positionTodayText(position)}`
}

function activityTitle(activity: StockActivity): string {
  switch (activity.type) {
    case 'buy':
      return '买入'
    case 'sell':
      return '卖出'
    case 'opening':
      return '录入持仓'
    case 'correction':
      return '核对持仓'
    default:
      return '行情更新'
  }
}

function activityMeta(activity: StockActivity): string {
  const quantity = activity.shares === null ? '' : `${formatShares(activity.shares)} 股`

  if (activity.type === 'buy' || activity.type === 'sell') {
    return [quantity, `成交 ${formatMoney(activity.price ?? 0)}`, formatBusinessDate(activity.businessDate)].filter(Boolean).join(' · ')
  }

  if (activity.type === 'correction' || activity.type === 'opening') {
    return [quantity, `成本 ${formatMoney(activity.amount)}`, formatBusinessDate(activity.businessDate)].filter(Boolean).join(' · ')
  }

  return `${formatBusinessDate(activity.businessDate)} · 仅更新估值`
}

function activityAmount(activity: StockActivity): string {
  if (activity.type === 'buy' || activity.type === 'sell') {
    return formatSignedMoney(activity.cashEffect)
  }

  if (activity.type === 'correction' || activity.type === 'opening') {
    return `¥${formatMoney(activity.amount)}`
  }

  return activity.price === null ? '—' : `¥${formatMoney(activity.price)}`
}

function goBack(): void {
  void router.push({ name: 'home' })
}
</script>

<template>
  <main class="page stock-page with-nav">
    <header class="stock-header">
      <BackButton @back="goBack" />
      <div class="stock-title">
        <h1>股票</h1>
        <p>按市场查看持仓与今日盈亏</p>
      </div>
      <button type="button" class="refresh-button" :disabled="isRefreshing" @click="refreshQuotes">
        {{ isRefreshing ? '刷新中…' : '刷新行情' }}
      </button>
    </header>

    <p v-if="isLoading && !overview" class="state">正在读取股票持仓…</p>

    <section v-else-if="errorMessage && !overview" class="state">
      <p>{{ errorMessage }}</p>
      <button type="button" class="secondary-button" @click="loadOverview">重新加载</button>
    </section>

    <template v-else-if="overview">
      <section class="stock-hero">
        <p class="hero-label">
          股票资产
          <span class="hero-tag">沪深北 · 含 ETF / LOF</span>
        </p>
        <h2 class="hero-balance"><span>¥</span>{{ formatMoney(overview.totalValue) }}</h2>
        <p class="hero-meta">
          持有 {{ activePositions.length }} 只
          <template v-if="overview.todayProfitDate"> · 行情 {{ formatBusinessDate(overview.todayProfitDate) }}</template>
          <template v-if="!overview.todayProfitComplete"> · 今日盈亏为估算</template>
          <template v-if="closedPositions.length"> · 已清仓 {{ closedPositions.length }} 只</template>
        </p>
        <div class="hero-metrics">
          <div>
            <span>{{ todayProfitLabel }}</span>
            <b :class="amountTone(overview.todayProfit)">
              {{ todayProfitText }}
              <em v-if="totalTodayPercent !== null" class="metric-percent">{{ formatPercent(totalTodayPercent) }}</em>
            </b>
          </div>
          <div>
            <span>持仓收益</span>
            <b :class="amountTone(overview.holdingProfit)">{{ formatSignedMoney(overview.holdingProfit) }}</b>
          </div>
          <div>
            <span>累计收益</span>
            <b :class="amountTone(overview.holdingProfit + overview.realizedProfit)">
              {{ formatSignedMoney(overview.holdingProfit + overview.realizedProfit) }}
            </b>
          </div>
        </div>
        <p class="hero-foot">可用现金 ¥{{ formatMoney(overview.cashBalance) }}</p>
      </section>

      <div v-if="overview.positions.length > 0" class="stock-actions">
        <button type="button" class="action-primary" @click="openSheet('buy')">买入</button>
        <button type="button" class="action-secondary" @click="openSheet('holding')">录入 / 核对</button>
        <button type="button" class="action-secondary" :disabled="isRefreshing" @click="refreshQuotes">刷新行情</button>
      </div>

      <p v-if="notice" class="notice" role="status">{{ notice }}</p>
      <p v-if="errorMessage" class="form-error" role="alert">{{ errorMessage }}</p>

      <section v-if="overview.positions.length === 0" class="empty-stock">
        <p>还没有股票持仓。可以录入已有持仓，也可以直接搜索买入（A 股、ETF、LOF）。</p>
        <div class="empty-actions">
          <button type="button" class="primary-button" @click="openSheet('buy')">买入第一只股票</button>
          <button type="button" class="secondary-button" @click="openSheet('holding')">录入已有持仓</button>
        </div>
        <button type="button" class="text-button empty-link" :disabled="isSaving" @click="markNotHeld">
          没有股票，标记为不持有
        </button>
      </section>
      <p v-if="!overview.historyConfirmed || !overview.hasEffectiveQuotes" class="hint">
        部分持仓的日期或行情依据不足，当前估值为缓存值。
      </p>

      <div v-if="activePositions.length > 1" class="sort-row" role="group" aria-label="持仓排序">
        <span>排序</span>
        <button
          v-for="option in sortOptions"
          :key="option.value"
          type="button"
          class="sort-chip"
          :class="{ active: sortKey === option.value }"
          :aria-pressed="sortKey === option.value"
          @click="sortKey = option.value"
        >
          {{ option.label }}
        </button>
      </div>

      <section v-for="group in groupedPositions" :key="group.market" class="market-group">
        <div class="section-head">
          <h2>{{ group.label }}</h2>
          <span class="meta">
            {{ group.positions.length }} 只 · ¥{{ formatMoney(group.value) }}
            <em v-if="group.todayProfit !== 0" :class="amountTone(group.todayProfit)">{{ formatSignedMoney(group.todayProfit) }}</em>
          </span>
        </div>
        <div class="position-list">
          <button
            v-for="position in group.positions"
            :key="position.id"
            type="button"
            class="position-row"
            @click="openDetail(position)"
          >
            <span class="position-main">
              <span class="position-name">
                <strong>{{ position.name }}</strong>
                <small>{{ position.code }}</small>
                <small class="position-shares">{{ formatShares(position.shares) }} 股</small>
              </span>
              <small>现价 {{ formatMoney(position.price) }} · 成本 {{ positionAverageCostText(position) }}</small>
            </span>
            <span class="position-side">
              <b>¥{{ formatMoney(position.value) }}</b>
              <small :class="amountTone(position.todayProfit)">
                {{ positionTodayLabel(position) }}
                <em v-if="positionChangePercent(position) !== null" class="position-percent">
                  {{ formatPercent(positionChangePercent(position)) }}
                </em>
              </small>
            </span>
          </button>
        </div>
      </section>

      <details v-if="closedPositions.length" class="closed-block">
        <summary>已清仓 {{ closedPositions.length }} 只</summary>
        <div class="position-list closed-list">
          <button
            v-for="position in closedPositions"
            :key="position.id"
            type="button"
            class="position-row"
            @click="openDetail(position)"
          >
            <span class="position-main">
              <span class="position-name">
                <strong>{{ position.name }}</strong>
                <small>{{ position.code }}</small>
              </span>
              <small>已清仓 · 行情 {{ formatBusinessDate(position.priceDate) }}</small>
            </span>
            <span class="position-side">
              <b :class="amountTone(position.realizedProfit)">{{ formatSignedMoney(position.realizedProfit) }}</b>
              <small>已实现收益</small>
            </span>
          </button>
        </div>
      </details>

      <template v-if="overview.activities.length">
        <div class="section-head activities-head">
          <h2>交易记录</h2>
          <span class="meta">按业务日期</span>
        </div>

        <section class="activity-card">
          <div v-for="activity in overview.activities" :key="activity.id" class="activity-row">
            <span class="activity-mark">{{ activity.type === 'buy' ? '买' : activity.type === 'sell' ? '卖' : activity.type === 'opening' ? '建' : activity.type === 'correction' ? '校' : '价' }}</span>
            <span class="activity-copy">
              <strong>{{ activity.positionName }} · {{ activityTitle(activity) }}</strong>
              <small>{{ activityMeta(activity) }}<template v-if="activity.note"> · {{ activity.note }}</template></small>
            </span>
            <b class="activity-value" :class="activity.type === 'buy' ? 'transfer' : activity.type === 'sell' ? 'income' : ''">
              {{ activityAmount(activity) }}
            </b>
          </div>
        </section>
      </template>

      <p v-else-if="overview.positions.length > 0" class="hint">还没有股票交易记录，买入或核对持仓后会出现在这里。</p>
    </template>

    <BottomSheet
      :open="sheet === 'detail'"
      :title="sheetTitle"
      :subtitle="sheetSubtitle"
      @close="closeSheet"
    >
      <template v-if="selectedPosition">
        <div class="detail-metrics">
          <div>
            <span>市值</span>
            <b>¥{{ formatMoney(selectedPosition.value) }}</b>
          </div>
          <div>
            <span>持仓收益</span>
            <b :class="amountTone(selectedPosition.holdingProfit)">{{ formatSignedMoney(selectedPosition.holdingProfit) }}</b>
          </div>
          <div>
            <span>{{ todayProfitLabel }}</span>
            <b :class="amountTone(selectedPosition.todayProfit)">{{ positionTodayText(selectedPosition) }}</b>
          </div>
          <div>
            <span>累计收益</span>
            <b :class="amountTone(selectedPosition.holdingProfit + selectedPosition.realizedProfit)">
              {{ formatSignedMoney(selectedPosition.holdingProfit + selectedPosition.realizedProfit) }}
            </b>
          </div>
        </div>
        <div class="detail-lines">
          <div><span>持有数量</span><b>{{ formatShares(selectedPosition.shares) }} 股</b></div>
          <div><span>持仓成本</span><b>¥{{ formatMoney(selectedPosition.cost) }}</b></div>
          <div><span>平均成本</span><b>{{ selectedPosition.averageCost === null ? '—' : `¥${formatMoney(selectedPosition.averageCost)}` }}</b></div>
          <div><span>现价 / 昨收</span><b>{{ formatMoney(selectedPosition.price) }} / {{ selectedPosition.previousClose === null ? '—' : formatMoney(selectedPosition.previousClose) }}</b></div>
          <div><span>行情日期</span><b>{{ formatBusinessDate(selectedPosition.priceDate) }}</b></div>
        </div>
        <div class="detail-actions">
          <button type="button" class="action-primary" @click="openSheet('buy', selectedPosition)">买入</button>
          <button type="button" class="action-secondary" :disabled="selectedPosition.shares <= 0" @click="openSheet('sell', selectedPosition)">卖出</button>
          <button type="button" class="action-secondary" @click="openSheet('holding', selectedPosition)">核对持仓</button>
        </div>
        <p v-if="selectedPosition.lastQuoteError" class="hint">最近行情刷新失败：{{ selectedPosition.lastQuoteError }}</p>
        <p v-if="selectedPosition.todayProfit === null && selectedPosition.shares > 0" class="hint">
          当日有建仓或持仓核对，或缺少昨收时，今日盈亏不计入；行情日期按最近交易日展示。
        </p>
        <div class="section-head"><h2>该股票记录</h2></div>
        <section v-if="detailActivities.length" class="activity-card detail-activities">
          <div v-for="activity in detailActivities" :key="activity.id" class="activity-row">
            <span class="activity-mark">{{ activity.type === 'buy' ? '买' : activity.type === 'sell' ? '卖' : activity.type === 'opening' ? '建' : '校' }}</span>
            <span class="activity-copy">
              <strong>{{ activityTitle(activity) }}</strong>
              <small>{{ activityMeta(activity) }}</small>
            </span>
            <b class="activity-value" :class="activity.type === 'sell' ? 'income' : ''">{{ activityAmount(activity) }}</b>
          </div>
        </section>
        <p v-else class="hint">这只股票还没有交易记录。</p>
      </template>
    </BottomSheet>

    <BottomSheet
      :open="sheet === 'buy' || sheet === 'sell' || sheet === 'holding'"
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
          <span>当前持有</span>
          <b>{{ formatShares(selectedPosition.shares) }} 股</b>
        </div>
      </div>

      <form class="form-stack" @submit.prevent="submit">
        <template v-if="!selectedStock">
          <div class="search-block">
            <label class="form-field">
              <span>股票代码 / 名称</span>
              <div class="search-row">
                <input
                  v-model="searchQuery"
                  type="search"
                  autocomplete="off"
                  placeholder="例如 600519 或 贵州茅台"
                  @keyup.enter="runSearch"
                  @input="formError = ''"
                />
                <button type="button" class="search-button" :disabled="searching" @click="runSearch">
                  {{ searching ? '搜索中…' : '搜索' }}
                </button>
              </div>
            </label>
            <p v-if="searching" class="form-note">正在搜索股票 / ETF…</p>
            <div v-else-if="searchResults.length" class="search-results">
              <button
                v-for="result in searchResults"
                :key="`${result.market}-${result.code}`"
                type="button"
                class="search-result"
                @click="selectStock(result)"
              >
                <span>
                  <b>{{ result.name }}</b>
                  <small>{{ result.code }} · {{ marketLabels[result.market] }}</small>
                </span>
                <span class="search-chevron">›</span>
              </button>
            </div>
            <p v-else-if="searched && searchQuery" class="form-note">没有找到匹配的股票或场内基金。</p>
          </div>
        </template>

        <div v-else class="selected-stock">
          <span>
            <b>{{ selectedStock.name }}</b>
            <small>{{ selectedStock.code }} · {{ marketLabels[selectedStock.market] }}</small>
          </span>
          <button v-if="!selectedPosition" type="button" class="text-button" @click="clearSelectedStock">更换</button>
        </div>

        <template v-if="sheet === 'buy'">
          <label class="form-field">
            <span>买入数量（股）</span>
            <input v-model="form.shares" type="text" inputmode="decimal" autocomplete="off" placeholder="100" @input="formError = ''" />
          </label>
          <label class="form-field">
            <span>成交价（元）</span>
            <input v-model="form.price" type="text" inputmode="decimal" autocomplete="off" placeholder="0.00" @input="formError = ''" />
          </label>
          <label class="form-field">
            <span>业务日期</span>
            <input v-model="form.businessDate" type="date" @input="formError = ''" />
          </label>
          <p v-if="buyMaxShares !== null" class="form-note">
            按当前成交价，可用现金最多可买 <b>{{ formatShares(buyMaxShares) }}</b> 股<template v-if="buyMaxLots > 0">，约 {{ buyMaxLots }} 手</template>。
          </p>
          <p v-if="tradeGross" class="form-note preview-line">
            成交金额 <b>¥{{ formatMoney(tradeGross) }}</b>
          </p>
        </template>

        <template v-else-if="sheet === 'sell'">
          <label class="form-field">
            <span>卖出数量（股）</span>
            <input v-model="form.shares" type="text" inputmode="decimal" autocomplete="off" :placeholder="`最多 ${formatShares(shareMax)} 股`" @input="formError = ''" />
          </label>
          <label class="form-field">
            <span>成交价（元）</span>
            <input v-model="form.price" type="text" inputmode="decimal" autocomplete="off" placeholder="0.00" @input="formError = ''" />
          </label>
          <label class="form-field">
            <span>业务日期</span>
            <input v-model="form.businessDate" type="date" @input="formError = ''" />
          </label>
          <p v-if="tradeGross" class="form-note preview-line">
            成交金额 <b>¥{{ formatMoney(tradeGross) }}</b>
          </p>
          <p v-if="sellEstimatedProfit !== null" class="form-note">
            预计收益 <b :class="amountTone(sellEstimatedProfit)">{{ formatSignedMoney(sellEstimatedProfit) }}</b>（成交金额 − 按比例结转成本）
          </p>
        </template>

        <template v-else-if="sheet === 'holding'">
          <label class="form-field">
            <span>持有数量（股）</span>
            <input v-model="form.shares" type="text" inputmode="decimal" autocomplete="off" placeholder="0" @input="formError = ''" />
          </label>
          <label class="form-field">
            <span>持仓成本（元）</span>
            <input v-model="form.cost" type="text" inputmode="decimal" autocomplete="off" placeholder="0.00" @input="formError = ''" />
          </label>
          <label class="form-field">
            <span>现价（元）</span>
            <input v-model="form.price" type="text" inputmode="decimal" autocomplete="off" placeholder="0.00" @input="formError = ''" />
          </label>
          <label class="form-field">
            <span>行情日期</span>
            <input v-model="form.priceDate" type="date" @input="formError = ''" />
          </label>
          <label class="form-field">
            <span>持仓日期</span>
            <input v-model="form.businessDate" type="date" @input="formError = ''" />
          </label>
        </template>

        <label class="form-field">
          <span>备注</span>
          <input v-model="form.note" type="text" maxlength="200" placeholder="选填" />
        </label>

        <p class="form-note">
          {{ sheet === 'buy' ? '按成交金额扣除现金，并计入持仓成本。' : sheet === 'sell' ? '按成交金额增加现金，并按比例结转成本。' : '录入或核对持仓不改变现金；数量为 0 时成本也应为 0。' }}
        </p>
        <p v-if="formError" class="form-error" role="alert">{{ formError }}</p>

        <div class="form-actions">
          <button type="button" class="secondary-button" :disabled="isSaving" @click="closeSheet">取消</button>
          <button type="submit" class="primary-button" :disabled="!canSubmit">{{ isSaving ? '保存中…' : '保存' }}</button>
        </div>
      </form>
    </BottomSheet>

    <AppNav />
  </main>
</template>

<style scoped>
.stock-page {
  padding-bottom: calc(97px + env(safe-area-inset-bottom));
}

.stock-header {
  display: flex;
  align-items: center;
  gap: 11px;
  margin-bottom: 16px;
}

.stock-title {
  min-width: 0;
}

.stock-title h1 {
  margin: 0;
  font-size: 1.0625rem;
  font-weight: 600;
  letter-spacing: -0.3px;
}

.stock-title p {
  margin: 3px 0 0;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
}

.refresh-button {
  flex: 0 0 auto;
  min-height: var(--touch-target);
  margin-left: auto;
  padding: 0 11px;
  border: 1px solid var(--color-border-subtle);
  border-radius: var(--radius-pill);
  background: var(--color-bg-surface);
  color: var(--color-brand);
  font-size: var(--text-footnote);
  font-weight: 600;
}

.refresh-button:disabled {
  color: var(--color-text-disabled);
}

.stock-hero {
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
  min-height: 2.4em;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
  line-height: 1.35;
}

.hero-metrics b {
  display: block;
  margin-top: 7px;
  font-size: 0.9375rem;
  font-weight: 500;
  letter-spacing: -0.3px;
  font-variant-numeric: tabular-nums;
}

.metric-percent {
  margin-left: 4px;
  font-size: var(--text-caption);
  font-style: normal;
  font-weight: 500;
  opacity: 0.85;
}

.hero-foot {
  margin: 14px 0 0;
  padding-top: 10px;
  border-top: 1px solid var(--color-border-subtle);
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
  font-variant-numeric: tabular-nums;
}

.stock-actions {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 8px;
  margin-top: 14px;
}

.stock-actions button,
.detail-actions button {
  min-height: 42px;
  border-radius: var(--radius-control);
  font-size: 0.75rem;
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
  border-radius: var(--radius-tag);
  background: var(--color-success-bg);
  color: var(--color-success);
  font-size: 0.75rem;
  line-height: 1.7;
}

.form-error {
  margin: 12px 0 0;
  padding: 9px 11px;
  border-radius: var(--radius-tag);
  background: var(--color-error-bg);
  color: var(--color-error);
  font-size: var(--text-caption);
  line-height: 1.6;
}

.market-group {
  margin-top: 2px;
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

.activities-head {
  margin-top: 26px;
}

.position-list {
  overflow: hidden;
  border: 1px solid var(--color-border-subtle);
  border-radius: var(--radius-card);
  background: var(--color-bg-surface);
}

.position-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
  width: 100%;
  padding: 13px;
  border: 0;
  border-bottom: 1px solid var(--color-border-subtle);
  background: transparent;
  color: inherit;
  text-align: left;
}

.position-row:last-child {
  border-bottom: 0;
}

.position-row:hover {
  background: var(--color-bg-subtle);
}

.position-main,
.position-side {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}

.position-name {
  display: flex;
  align-items: baseline;
  gap: 7px;
  min-width: 0;
}

.position-name strong {
  overflow: hidden;
  font-size: 0.8125rem;
  font-weight: 600;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.position-name small {
  flex: 0 0 auto;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
}

.position-shares {
  margin-left: auto;
  padding: 1px 6px;
  border-radius: var(--radius-pill);
  background: var(--color-bg-subtle);
  color: var(--color-text-secondary);
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}

.position-main > small,
.position-side small {
  overflow: hidden;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.position-side {
  align-items: flex-end;
  flex: 0 0 auto;
}

.position-side b {
  font-size: 0.8125rem;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}

.position-percent {
  margin-left: 4px;
  font-style: normal;
  font-weight: 500;
  opacity: 0.85;
}

.sort-row {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-top: 16px;
}

.sort-row > span {
  margin-right: 2px;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
}

.sort-chip {
  min-height: 30px;
  padding: 0 12px;
  border: 1px solid var(--color-border-subtle);
  border-radius: var(--radius-pill);
  background: var(--color-bg-surface);
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
  font-weight: 500;
}

.sort-chip.active {
  border-color: var(--color-brand);
  background: var(--color-brand-soft);
  color: var(--color-brand);
}

.closed-block {
  margin-top: 20px;
}

.closed-block summary {
  min-height: 32px;
  padding: 6px 0;
  color: var(--color-text-secondary);
  font-size: var(--text-footnote);
  font-weight: 500;
  cursor: pointer;
}

.closed-list {
  margin-top: 6px;
}

.closed-list .position-side b {
  font-size: 0.8125rem;
}

.section-head .meta em {
  margin-left: 6px;
  font-style: normal;
  font-variant-numeric: tabular-nums;
}

.empty-stock {
  margin-top: 20px;
  padding: 24px 16px;
  border: 1px dashed var(--color-border-default);
  border-radius: var(--radius-card);
  text-align: center;
}

.empty-stock p {
  margin: 0 0 14px;
  color: var(--color-text-secondary);
  font-size: var(--text-footnote);
  line-height: 1.6;
}

.empty-actions {
  display: grid;
  gap: 8px;
}

.empty-actions button {
  min-height: var(--touch-target);
  border-radius: var(--radius-control);
  font-size: var(--text-footnote);
  font-weight: 500;
}

.empty-stock .empty-link {
  margin-top: 12px;
  min-height: 32px;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
}

.hint {
  margin: 14px 0 0;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
  line-height: 1.7;
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
  overflow: hidden;
  font-size: 0.8125rem;
  font-weight: 500;
  text-overflow: ellipsis;
  white-space: nowrap;
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
  font-size: 0.8125rem;
  font-weight: 500;
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
  border-radius: var(--radius-card);
  text-align: center;
}

.empty-state p {
  margin: 0 0 14px;
  color: var(--color-text-secondary);
  font-size: 0.75rem;
}

.empty-state .primary-button {
  width: 100%;
}

.detail-metrics {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
  padding: 14px;
  border-radius: var(--radius-card);
  background: var(--color-brand-soft);
}

.detail-metrics span,
.detail-lines span {
  display: block;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
}

.detail-metrics b {
  display: block;
  margin-top: 5px;
  font-size: 0.9375rem;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
}

.detail-lines {
  display: flex;
  flex-direction: column;
  gap: 10px;
  margin-top: 14px;
}

.detail-lines > div {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 12px;
  padding-bottom: 9px;
  border-bottom: 1px solid var(--color-border-subtle);
}

.detail-lines > div:last-child {
  border-bottom: 0;
  padding-bottom: 0;
}

.detail-lines b {
  font-size: 0.8125rem;
  font-weight: 500;
  font-variant-numeric: tabular-nums;
  text-align: right;
}

.detail-actions {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 8px;
  margin-top: 16px;
}

.detail-activities {
  margin-top: 2px;
}

.sheet-summary {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.summary-line {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 12px;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
}

.summary-line b {
  color: var(--color-text-primary);
  font-size: 0.8125rem;
  font-weight: 500;
  font-variant-numeric: tabular-nums;
}

.search-block {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.search-row {
  display: flex;
  gap: 8px;
}

.search-row input {
  flex: 1;
  min-width: 0;
}

.search-button {
  flex: 0 0 auto;
  min-height: var(--touch-target);
  padding: 0 13px;
  border: 1px solid var(--color-brand);
  border-radius: var(--radius-control);
  background: var(--color-brand);
  color: var(--color-on-brand);
  font-size: var(--text-footnote);
  font-weight: 500;
}

.search-button:disabled {
  opacity: 0.6;
}

.search-results {
  overflow: hidden;
  border: 1px solid var(--color-border-subtle);
  border-radius: var(--radius-card);
}

.search-result {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
  width: 100%;
  padding: 11px 12px;
  border: 0;
  border-bottom: 1px solid var(--color-border-subtle);
  background: var(--color-bg-surface);
  color: inherit;
  text-align: left;
}

.search-result:last-child {
  border-bottom: 0;
}

.search-result:hover {
  background: var(--color-bg-subtle);
}

.search-result span {
  display: flex;
  flex-direction: column;
  gap: 3px;
  min-width: 0;
}

.search-result b {
  overflow: hidden;
  font-size: 0.8125rem;
  font-weight: 500;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.search-result small {
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
}

.search-chevron {
  flex: 0 0 auto;
  color: var(--color-text-secondary);
  font-size: 1.125rem;
}

.selected-stock {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
  padding: 12px;
  border-radius: var(--radius-card);
  background: var(--color-brand-soft);
}

.selected-stock span {
  display: flex;
  flex-direction: column;
  gap: 3px;
  min-width: 0;
}

.selected-stock b {
  font-size: 0.8125rem;
  font-weight: 600;
}

.selected-stock small {
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
}

.text-button {
  flex: 0 0 auto;
  min-height: var(--touch-target);
  padding: 0 4px;
  border: 0;
  background: transparent;
  color: var(--color-brand);
  font-size: var(--text-footnote);
  font-weight: 500;
}

.preview-line {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 8px;
  padding: 0;
  background: transparent;
}

.preview-line b {
  color: var(--color-brand);
  font-size: 0.9375rem;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
}

.preview-line span {
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
}

@media (max-width: 360px) {
  .stock-actions,
  .detail-actions {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .stock-actions button:last-child {
    grid-column: 1 / -1;
  }

  .hero-metrics {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .hero-metrics > div:nth-child(3) {
    grid-column: 1 / -1;
    padding-left: 0;
    border-left: 0;
  }
}
</style>
