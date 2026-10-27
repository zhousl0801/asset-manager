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

      <AssetDistribution
        :overview="overview"
        :changes="monthChange?.assetChanges"
        :comparable="monthChange?.hasComparableData ?? false"
        editable
        @toggle-not-held="toggleNotHeld"
      />

      <p class="foot-note">
        现金始终计入；公积金、基金和积存金可以标记为不持有。标记不持有不会删除已有记录。
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
  margin: 0 -19px 18px;
  padding: max(14px, env(safe-area-inset-top)) 19px 13px;
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
  font-size: 11px;
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
  border-radius: 4px;
  background: var(--color-bg-subtle);
  color: var(--color-text-secondary);
  font-size: 10px;
}

.hero-value {
  margin: 8px 0 8px;
  font-size: 40px;
  font-weight: 550;
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
  font-size: 11px;
}

.notice {
  margin: 0 0 14px;
  padding: 9px 11px;
  border-radius: 8px;
  background: var(--color-success-bg);
  color: var(--color-success);
  font-size: 11px;
}

.form-error {
  margin: 0 0 14px;
  padding: 9px 11px;
  border-radius: 8px;
  background: var(--color-error-bg);
  color: var(--color-error);
  font-size: 11px;
}

.foot-note {
  margin: 0 0 16px;
  color: var(--color-text-secondary);
  font-size: 10px;
  line-height: 1.7;
}
</style>
