<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { fetchAssetTrend, fetchMonthChange, updateAssetCoverage } from '../api/ledger'
import { getApiErrorMessage } from '../api/client'
import AppNav from '../components/AppNav.vue'
import AssetDistribution from '../components/AssetDistribution.vue'
import BackButton from '../components/BackButton.vue'
import { useLedgerStore } from '../stores/ledger'
import { formatBusinessDate, formatMoney, todayInBusinessTimeZone } from '../utils/format'
import type { AssetKey, LedgerOverview, MonthChange } from '../types/ledger'

const router = useRouter()
const ledgerStore = useLedgerStore()
const overview = ref<LedgerOverview | null>(null)
const monthChange = ref<MonthChange | null>(null)
const updateDate = ref(todayInBusinessTimeZone())
const isLoading = ref(false)
const errorMessage = ref('')
const notice = ref('')

const recordedCount = computed(() => {
  if (!overview.value) {
    return 0
  }

  return [overview.value.cash, overview.value.providentFund, overview.value.fund, overview.value.gold]
    .filter((asset) => asset.isConfigured && !asset.notHeld)
    .length
})

const missingAssets = computed(() => {
  const ledger = overview.value

  if (!ledger) {
    return []
  }

  return [
    { label: '公积金', route: '/provident', configured: ledger.providentFund.isConfigured, notHeld: ledger.providentFund.notHeld },
    { label: '基金', route: '/funds', configured: ledger.fund.isConfigured, notHeld: ledger.fund.notHeld },
    { label: '积存金', route: '/gold', configured: ledger.gold.isConfigured, notHeld: ledger.gold.notHeld }
  ].filter((asset) => !asset.configured && !asset.notHeld)
})

const primaryMissing = computed(() => missingAssets.value[0] ?? null)

onMounted(load)

async function load(): Promise<void> {
  isLoading.value = true
  errorMessage.value = ''

  try {
    const status = await ledgerStore.load()

    if (status === 'missing') {
      await router.replace({ name: 'setup' })
      return
    }

    if (status === 'error') {
      errorMessage.value = ledgerStore.errorMessage ?? '资产数据加载失败，请重试。'
      return
    }

    overview.value = ledgerStore.overview

    try {
      const trend = await fetchAssetTrend('month')
      updateDate.value = trend.to
    } catch {
      updateDate.value = todayInBusinessTimeZone()
    }

    try {
      monthChange.value = await fetchMonthChange()
    } catch {
      monthChange.value = null
    }
  } finally {
    isLoading.value = false
  }
}

async function toggleNotHeld(key: AssetKey, notHeld: boolean): Promise<void> {
  const ledger = overview.value

  if (!ledger || key === 'cash') {
    return
  }

  errorMessage.value = ''
  notice.value = ''

  try {
    overview.value = await updateAssetCoverage({
      fundNotHeld: key === 'fund' ? notHeld : ledger.fundNotHeld,
      providentFundNotHeld: key === 'providentFund' ? notHeld : ledger.providentFundNotHeld,
      goldNotHeld: key === 'gold' ? notHeld : ledger.goldNotHeld
    })
    notice.value = notHeld ? '已标记为不持有。' : '已恢复为待录入。'
  } catch (error) {
    errorMessage.value = getApiErrorMessage(error, '保存失败，请重试。')
  }
}

function goHome(): void {
  void router.push({ name: 'home' })
}
</script>

<template>
  <main class="page with-nav">
    <header class="page-head">
      <div class="page-head-row">
        <BackButton @back="goHome" />
        <div class="page-head-title">
          <h1>资产</h1>
          <p>维护每类资产的余额与覆盖范围</p>
        </div>
      </div>
    </header>

    <p v-if="isLoading" class="state">正在读取资产…</p>

    <section v-else-if="errorMessage && !overview" class="state">
      <p>{{ errorMessage }}</p>
      <button class="secondary-button" type="button" @click="load">重新加载</button>
    </section>

    <template v-else-if="overview">
      <section class="hero">
        <p class="hero-label">
          {{ overview.coverageComplete ? '总资产' : '已记录资产' }}
          <span class="tag">{{ overview.currency }}</span>
        </p>
        <h1 class="hero-value"><span class="currency">¥</span>{{ formatMoney(overview.totalAssets) }}</h1>
        <p class="hero-meta">已记录 {{ recordedCount }} 类 · 更新于 {{ formatBusinessDate(updateDate) }}</p>
      </section>

      <p v-if="notice" class="notice">{{ notice }}</p>
      <p v-if="errorMessage" class="form-error" role="alert">{{ errorMessage }}</p>

      <section v-if="primaryMissing" class="asset-callout">
        <div>
          <strong>还有 {{ missingAssets.length }} 类资产未录入</strong>
          <p>可以录入余额，或标记为不持有。</p>
        </div>
        <RouterLink :to="primaryMissing.route" class="callout-action">去录入 ›</RouterLink>
      </section>

      <AssetDistribution
        :overview="overview"
        :changes="monthChange?.assetChanges"
        :comparable="monthChange?.hasComparableData ?? false"
        editable
        @toggle-not-held="toggleNotHeld"
      />

      <p class="foot-note">
        点击资产行查看明细；现金始终计入，公积金、基金和积存金可以标记为不持有。标记不持有不会删除已有记录。
      </p>
    </template>

    <AppNav />
  </main>
</template>

<style scoped>
.page {
  padding-top: 0;
}

.page-head {
  margin: 0 calc(var(--page-gutter) * -1) 18px;
  padding: max(14px, env(safe-area-inset-top)) var(--page-gutter) 13px;
  border-bottom: 1px solid var(--color-border-subtle);
  background: var(--color-bg-surface);
}

.page-head-row {
  display: flex;
  align-items: center;
  gap: 11px;
}

.page-head-title {
  min-width: 0;
}

.page-head h1 {
  margin: 0;
  font-size: 17px;
  font-weight: 600;
  letter-spacing: -0.3px;
}

.page-head p {
  margin: 4px 0 0;
  color: var(--color-text-secondary);
  font-size: var(--text-footnote);
}

.hero {
  padding: 0 3px 18px;
}

.hero-label {
  display: flex;
  align-items: center;
  gap: 8px;
  margin: 0;
  color: var(--color-text-secondary);
  font-size: 12px;
}

.tag {
  padding: 2px 5px;
  border-radius: var(--radius-tag);
  background: var(--color-bg-subtle);
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
}

.hero-value {
  margin: 8px 0 8px;
  font-size: 40px;
  font-weight: 500;
  letter-spacing: -1.7px;
  line-height: 1.25;
  font-variant-numeric: tabular-nums;
}

.hero-value .currency {
  margin-right: 5px;
  font-size: 24px;
  font-weight: 400;
  letter-spacing: 0;
}

.hero-meta {
  margin: 0;
  color: var(--color-text-secondary);
  font-size: var(--text-footnote);
}

.notice {
  margin: 0 0 14px;
  padding: 9px 11px;
  border-radius: var(--radius-tag);
  background: var(--color-success-bg);
  color: var(--color-success);
  font-size: var(--text-footnote);
}

.form-error {
  margin: 0 0 14px;
  padding: 9px 11px;
  border-radius: var(--radius-tag);
  background: var(--color-error-bg);
  color: var(--color-error);
  font-size: var(--text-footnote);
}

.asset-callout {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
  margin-bottom: 16px;
  padding: 14px 16px;
  border-radius: var(--radius-card);
  background: var(--color-brand-soft);
}

.asset-callout strong {
  display: block;
  font-size: var(--text-subhead);
  font-weight: 600;
}

.asset-callout p {
  margin: 3px 0 0;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
}

.callout-action {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex: 0 0 auto;
  min-height: var(--touch-target);
  padding: 0 14px;
  border-radius: var(--radius-pill);
  background: var(--color-bg-surface);
  color: var(--color-brand);
  font-size: var(--text-footnote);
  font-weight: 600;
  text-decoration: none;
}

.foot-note {
  margin: 0 0 16px;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
  line-height: 1.7;
}
</style>
