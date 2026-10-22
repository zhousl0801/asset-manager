<script setup lang="ts">
import { storeToRefs } from 'pinia'
import { computed, onMounted, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import { fetchAssetTrend } from '../api/ledger'
import AssetTrendChart from '../components/AssetTrendChart.vue'
import { useLedgerStore } from '../stores/ledger'
import { formatBusinessDate, formatMoney } from '../utils/format'
import type { AssetTrend, TrendRange } from '../types/trend'

interface AssetCard {
  key: string
  label: string
  mark: string
  amount: number | null
  note: string
  isConfigured: boolean
  to: { name: 'cash' | 'provident' | 'funds' | 'gold' }
}

const router = useRouter()
const ledgerStore = useLedgerStore()
const { overview, isLoading, errorMessage } = storeToRefs(ledgerStore)
const trend = ref<AssetTrend | null>(null)
const trendRange = ref<TrendRange>('month')
const trendLoading = ref(false)
const trendError = ref('')
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
      mark: '¥',
      amount: ledger.cash.amount,
      note: `${ledger.cash.note ?? '统一余额'} · 查看 ›`,
      isConfigured: ledger.cash.isConfigured,
      to: { name: 'cash' }
    },
    {
      key: 'fund',
      label: '基金',
      mark: '↗',
      amount: ledger.fund.amount,
      note: `${ledger.fund.note ?? '尚未录入'} · 查看 ›`,
      isConfigured: ledger.fund.isConfigured,
      to: { name: 'funds' }
    },
    {
      key: 'provident',
      label: '公积金',
      mark: '住',
      amount: ledger.providentFund.amount,
      note: `${ledger.providentFund.note ?? '尚未录入'} · 查看 ›`,
      isConfigured: ledger.providentFund.isConfigured,
      to: { name: 'provident' }
    },
    {
      key: 'gold',
      label: '积存金',
      mark: '金',
      amount: ledger.gold.amount,
      note: `${ledger.gold.note ?? '尚未录入'} · 查看 ›`,
      isConfigured: ledger.gold.isConfigured,
      to: { name: 'gold' }
    }
  ]
})

const hasUnconfiguredAssets = computed(() => assetCards.value.some((asset) => !asset.isConfigured))

async function loadLedger(): Promise<void> {
  const status = await ledgerStore.load()

  if (status === 'missing') {
    await router.replace({ name: 'setup' })
    return
  }

  if (status === 'ready') {
    await loadTrend(trendRange.value)
  }
}

async function loadTrend(range: TrendRange): Promise<void> {
  trendRange.value = range
  trendLoading.value = true
  trendError.value = ''

  try {
    trend.value = await fetchAssetTrend(range)
  } catch {
    trend.value = null
    trendError.value = '趋势暂时无法读取，请稍后重试。'
  } finally {
    trendLoading.value = false
  }
}

onMounted(loadLedger)
</script>

<template>
  <main class="page">
    <header class="app-header">
      <div>
        <p class="app-name">资产管家</p>
        <p class="app-sub">我的财务总览</p>
      </div>
      <span class="currency-tag">{{ overview?.currency ?? 'CNY' }}</span>
    </header>
    <div class="home-links">
      <RouterLink class="back-link history-entry-link" to="/history">历史核对与资产回看 ›</RouterLink>
      <RouterLink class="back-link history-entry-link" to="/plans">自动记账与执行记录 ›</RouterLink>
    </div>

    <p v-if="isLoading" class="state">正在读取账本…</p>

    <section v-else-if="errorMessage" class="state">
      <p>{{ errorMessage }}</p>
      <button class="secondary-button" type="button" @click="loadLedger">重新加载</button>
    </section>

    <template v-else-if="overview">
      <section class="total-panel">
        <p class="total-label">{{ overview.isComplete ? '总资产' : '部分资产合计' }}</p>
        <h1 class="total-amount"><span>¥</span>{{ formatMoney(overview.totalAssets) }}</h1>
        <p class="meta">
          统计截至 {{ formatBusinessDate(overview.asOfDate) }} · 起始 {{ formatBusinessDate(overview.startDate) }}
        </p>
        <p v-if="!overview.isComplete" class="meta">{{ hasUnconfiguredAssets ? '部分资产尚未录入，点击资产卡片补充。' : '持仓日期或行情依据待确认，请前往历史核对。' }}</p>
      </section>

      <section class="trend-panel">
        <div class="section-head">
          <h2>总资产趋势</h2>
          <span class="meta">{{ trend ? `${trend.from} 至 ${trend.to}` : '按日重算' }}</span>
        </div>
        <div class="range-tabs" role="tablist" aria-label="趋势范围">
          <button
            v-for="option in rangeOptions"
            :key="option.value"
            type="button"
            class="range-tab"
            :class="{ active: trendRange === option.value }"
            :disabled="trendLoading"
            @click="loadTrend(option.value)"
          >
            {{ option.label }}
          </button>
        </div>
        <p v-if="trendLoading && !trend" class="hint">正在读取趋势…</p>
        <p v-else-if="trendError" class="hint">{{ trendError }}</p>
        <AssetTrendChart v-else-if="trend && trend.points.length" :trend="trend" />
        <p v-else class="hint">还没有可展示的趋势数据。</p>
        <p v-for="note in trend?.notes ?? []" :key="note" class="hint">{{ note }}</p>
        <p class="hint">完整总资产为空白表示该日依据不足；虚线为已有依据的部分资产合计，不作为完整总额。</p>
      </section>

      <div class="section-head">
        <h2>资产分类</h2>
        <span class="meta">4 类资产</span>
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
            <span class="asset-mark">{{ asset.mark }}</span>
          </div>
          <b>{{ asset.isConfigured ? (asset.amount === null ? '依据不足' : formatMoney(asset.amount)) : '未录入' }}</b>
          <small>{{ asset.note }}</small>
        </RouterLink>
      </div>

      <section class="month-panel">
        <div class="section-head">
          <h2>本月收支</h2>
          <RouterLink class="text-link" :to="{ name: 'ledger' }">查看账本 ›</RouterLink>
        </div>
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
        <p v-if="!overview.month.hasRecords" class="hint">
          本月还没有收支记录。<RouterLink class="text-link" :to="{ name: 'entry' }">去记一笔 ›</RouterLink>
        </p>
      </section>
    </template>
  </main>
</template>

<style scoped>
.home-links {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
}

.total-panel {
  margin-bottom: 26px;
}

.trend-panel {
  margin-bottom: 22px;
}

.range-tabs {
  display: flex;
  gap: 6px;
  margin-bottom: 8px;
}

.range-tab {
  min-height: 30px;
  padding: 0 11px;
  border: 1px solid var(--color-border-control);
  border-radius: 7px;
  background: var(--color-bg-surface);
  color: var(--color-text-secondary);
  font-size: 10px;
}

.range-tab.active {
  border-color: var(--color-brand);
  background: var(--color-brand-soft);
  color: var(--color-brand);
  font-weight: 550;
}

.range-tab:disabled {
  opacity: 0.6;
}

.total-label {
  margin: 0;
  color: var(--color-text-secondary);
  font-size: 12px;
}

.total-amount {
  margin: 8px 0 10px;
  font-size: 42px;
  font-weight: 550;
  letter-spacing: -1.5px;
  line-height: 1.2;
  font-variant-numeric: tabular-nums;
}

.total-amount span {
  margin-right: 5px;
  font-size: 24px;
  font-weight: 400;
  letter-spacing: 0;
}

.meta {
  margin: 0;
  color: var(--color-text-secondary);
  font-size: 11px;
  line-height: 1.7;
}

.month-panel {
  margin-bottom: 25px;
  padding: 16px 14px 13px;
  border: 1px solid var(--color-border-subtle);
  border-radius: 12px;
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
  margin-top: 7px;
  font-size: 18px;
  font-weight: 550;
  font-variant-numeric: tabular-nums;
}

.income {
  color: var(--color-finance-income);
}

.hint {
  margin: 12px 0 0;
  padding-top: 11px;
  border-top: 1px solid var(--color-border-subtle);
  color: var(--color-text-secondary);
  font-size: 10px;
  line-height: 1.7;
}

.asset-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
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
  border-color: var(--color-brand);
  background: var(--color-bg-hover);
}

.asset-head {
  display: flex;
  justify-content: space-between;
  align-items: center;
  color: var(--color-text-secondary);
  font-size: 12px;
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
  font-size: 21px;
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
</style>
