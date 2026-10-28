<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink } from 'vue-router'
import AppIcon from './AppIcon.vue'
import { formatMoney, formatSignedMoney, sumAmounts } from '../utils/format'
import type { AssetChange, AssetKey, LedgerOverview } from '../types/ledger'

interface DistributionRow {
  key: AssetKey
  label: string
  icon: string
  to: string
  amount: number | null
  configured: boolean
  notHeld: boolean
  note: string
  change: AssetChange | null
  percent: number | null
}

const props = defineProps<{
  overview: LedgerOverview
  changes?: AssetChange[]
  comparable?: boolean
  editable?: boolean
  manageTo?: string
}>()

const emit = defineEmits<{ toggleNotHeld: [key: AssetKey, notHeld: boolean] }>()

const rows = computed<DistributionRow[]>(() => {
  const ledger = props.overview
  const changeByKey = new Map((props.changes ?? []).map((item) => [item.assetType, item]))
  const recordedTotal = sumAmounts([ledger.cash, ledger.providentFund, ledger.fund, ledger.gold]
    .filter((asset) => asset.isConfigured && !asset.notHeld && asset.amount !== null)
    .map(asset => asset.amount ?? 0))
  const percentOf = (amount: number | null): number | null =>
    amount === null || recordedTotal <= 0 ? null : amount / recordedTotal * 100

  return [
    {
      key: 'cash',
      label: '现金',
      icon: 'cash',
      to: '/cash',
      amount: ledger.cash.amount,
      configured: true,
      notHeld: false,
      note: ledger.month.hasRecords
        ? `本月结余 ${formatSignedMoney(ledger.month.surplus)}`
        : '统一账户余额',
      change: changeByKey.get('cash') ?? null,
      percent: percentOf(ledger.cash.amount)
    },
    {
      key: 'providentFund',
      label: '公积金',
      icon: 'home',
      to: '/provident',
      amount: ledger.providentFund.amount,
      configured: ledger.providentFund.isConfigured,
      notHeld: ledger.providentFund.notHeld,
      note: ledger.providentFund.note ?? '尚未录入',
      change: changeByKey.get('providentFund') ?? null,
      percent: percentOf(ledger.providentFund.amount)
    },
    {
      key: 'fund',
      label: '基金',
      icon: 'chart',
      to: '/funds',
      amount: ledger.fund.amount,
      configured: ledger.fund.isConfigured,
      notHeld: ledger.fund.notHeld,
      note: ledger.fund.note ?? '尚未录入',
      change: changeByKey.get('fund') ?? null,
      percent: percentOf(ledger.fund.amount)
    },
    {
      key: 'gold',
      label: '积存金',
      icon: 'gold',
      to: '/gold',
      amount: ledger.gold.amount,
      configured: ledger.gold.isConfigured,
      notHeld: ledger.gold.notHeld,
      note: ledger.gold.note ?? '尚未录入',
      change: changeByKey.get('gold') ?? null,
      percent: percentOf(ledger.gold.amount)
    }
  ]
})

const barSegments = computed(() => rows.value
  .filter((row) => row.configured && !row.notHeld && row.amount !== null && row.amount > 0)
  .map((row) => ({ key: row.key, width: row.percent ?? 0 })))

const recordedCount = computed(() => rows.value.filter((row) => row.configured && !row.notHeld).length)

function amountLabel(row: DistributionRow): string {
  if (row.notHeld) {
    return '不持有'
  }

  if (!row.configured) {
    return '暂未录入'
  }

  return row.amount === null ? '依据不足' : `¥${formatMoney(row.amount)}`
}

function changeLabel(row: DistributionRow): string {
  if (row.notHeld) {
    return '不持有'
  }

  if (!row.configured) {
    return ''
  }

  if (row.change?.isNew) {
    return '本月新增'
  }

  if (props.comparable === false || row.change?.change == null) {
    return '较上月 —'
  }

  return `较上月 ${formatSignedMoney(row.change.change)}`
}
</script>

<template>
  <section class="distribution">
    <div class="distribution-head">
      <h2>资产分布</h2>
      <span class="head-meta">
        <span class="recorded-count">已记录 {{ recordedCount }} 类</span>
        <RouterLink v-if="manageTo" :to="manageTo" class="manage-link">管理 ›</RouterLink>
      </span>
    </div>

    <div class="ratio-bar" role="img" aria-label="各类资产占比">
      <span
        v-for="segment in barSegments"
        :key="segment.key"
        class="ratio-segment"
        :class="`type-${segment.key}`"
        :style="{ width: `${segment.width}%` }"
      />
      <span v-if="!barSegments.length" class="ratio-empty" />
    </div>

    <ul class="asset-list">
      <li v-for="row in rows" :key="row.key" class="asset-row" :class="{ muted: row.notHeld }">
        <RouterLink v-if="row.configured && !row.notHeld" :to="row.to" class="asset-link">
          <span class="asset-main">
            <span class="asset-title">
              <i class="dot" :class="`type-${row.key}`" />
              <b>{{ row.label }}</b>
            </span>
            <small>{{ row.note }}</small>
          </span>
          <span class="asset-side">
            <b class="asset-amount">{{ amountLabel(row) }}</b>
            <small>{{ changeLabel(row) }}</small>
          </span>
          <span class="asset-percent">
            <span>{{ row.percent === null ? '—' : `${row.percent.toFixed(1)}%` }}</span>
            <AppIcon class="row-chevron" name="chevron" />
          </span>
        </RouterLink>

        <div v-else class="asset-link static">
          <span class="asset-main">
            <span class="asset-title">
              <i class="dot" :class="`type-${row.key}`" />
              <b>{{ row.label }}</b>
            </span>
            <small>{{ row.notHeld ? '已标记不持有' : '还没有记录' }}</small>
          </span>
          <span class="asset-side">
            <b class="asset-amount muted-text">{{ amountLabel(row) }}</b>
          </span>
          <span v-if="editable" class="row-actions">
            <RouterLink v-if="!row.notHeld" :to="row.to" class="row-action">添加</RouterLink>
            <button
              type="button"
              class="row-action subtle"
              @click="emit('toggleNotHeld', row.key, !row.notHeld)"
            >{{ row.notHeld ? '恢复' : '不持有' }}</button>
          </span>
          <RouterLink v-else-if="!row.notHeld" :to="row.to" class="row-action">添加</RouterLink>
          <span v-else class="asset-percent">—</span>
        </div>
      </li>
    </ul>
  </section>
</template>

<style scoped>
.distribution {
  margin-bottom: 24px;
}

.distribution-head {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 12px;
  margin-bottom: 10px;
}

.distribution-head h2 {
  margin: 0;
  font-size: var(--text-headline);
  font-weight: 600;
  letter-spacing: -0.2px;
}

.recorded-count {
  color: var(--color-text-secondary);
  font-size: var(--text-footnote);
}

.head-meta {
  display: inline-flex;
  align-items: baseline;
  gap: 10px;
}

.manage-link {
  color: var(--color-brand);
  font-size: var(--text-footnote);
  text-decoration: none;
}

.manage-link:hover {
  text-decoration: underline;
}

.ratio-bar {
  display: flex;
  overflow: hidden;
  height: 8px;
  margin-bottom: 12px;
  border-radius: var(--radius-pill);
  background: var(--color-bg-subtle);
}

.ratio-segment {
  height: 100%;
}

.ratio-segment + .ratio-segment {
  box-shadow: 0 0 0 1px var(--color-bg-surface);
}

.ratio-empty {
  width: 100%;
}

.dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
}

.type-cash {
  background: var(--color-chart-cash);
}

.type-providentFund {
  background: var(--color-chart-property);
}

.type-fund {
  background: var(--color-chart-investment);
}

.type-gold {
  background: var(--color-chart-receivable);
}

.asset-list {
  margin: 0;
  padding: 0;
  border: 1px solid var(--color-border-subtle);
  border-radius: var(--radius-card);
  background: var(--color-bg-surface);
  list-style: none;
}

.asset-row + .asset-row {
  border-top: 1px solid var(--color-border-subtle);
}

.asset-link {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto 58px;
  align-items: center;
  gap: 10px;
  padding: 13px 13px;
  color: inherit;
  text-decoration: none;
}

.asset-link.static {
  grid-template-columns: minmax(0, 1fr) auto auto;
  column-gap: 12px;
}

.asset-link.static .asset-amount {
  font-size: 0.75rem;
  font-weight: 500;
}

.asset-main {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}

.asset-title {
  display: flex;
  align-items: center;
  gap: 7px;
  font-size: 0.8125rem;
}

.asset-title b {
  font-weight: 500;
}

.asset-main small,
.asset-side small {
  overflow: hidden;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.asset-side {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 4px;
}

.asset-amount {
  font-size: 0.8125rem;
  font-weight: 500;
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}

.asset-percent {
  color: var(--color-text-secondary);
  font-size: var(--text-footnote);
  text-align: right;
  font-variant-numeric: tabular-nums;
}

.muted-text {
  color: var(--color-text-secondary);
  font-weight: 400;
}

.asset-row.muted .asset-title {
  color: var(--color-text-secondary);
}

.row-actions {
  display: inline-flex;
  align-items: center;
  gap: 4px;
}

.row-action {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  box-sizing: border-box;
  min-height: var(--touch-target);
  padding: 0 12px;
  border: 0;
  border-radius: var(--radius-pill);
  background: var(--color-brand-soft);
  color: var(--color-brand);
  font-size: 0.75rem;
  font-weight: 600;
  line-height: 1;
  text-decoration: none;
  white-space: nowrap;
  transition: background .15s ease, color .15s ease;
}

.row-action:hover {
  background: var(--color-brand-soft-hover);
}

.row-action.subtle {
  padding: 0 10px;
  background: transparent;
  color: var(--color-text-secondary);
  font-weight: 500;
}

.row-action.subtle:hover {
  background: var(--color-brand-soft);
  color: var(--color-brand);
}

.row-action:focus-visible {
  outline: 2px solid var(--color-brand);
  outline-offset: 2px;
}

@media (max-width: 360px) {
  .asset-link,
  .asset-link.static {
    grid-template-columns: minmax(0, 1fr) auto;
  }

  .asset-percent {
    display: none;
  }

  .asset-link.static .row-actions {
    grid-column: 1 / -1;
    justify-self: end;
    margin-top: 2px;
  }
}
</style>
