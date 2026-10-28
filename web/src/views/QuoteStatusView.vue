<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { fetchQuoteStatus, refreshQuotes } from '../api/quotes'
import type { QuoteRefreshScope, QuoteStatus, QuoteStatusItem } from '../api/quotes'
import { getApiErrorMessage } from '../api/client'
import BackButton from '../components/BackButton.vue'
import { formatBusinessDate, formatMoney, formatQuantity } from '../utils/format'

const router = useRouter()
const status = ref<QuoteStatus | null>(null)
const isLoading = ref(false)
const refreshingScope = ref<QuoteRefreshScope | null>(null)
const errorMessage = ref('')
const notice = ref('')

const schedulerText = computed(() => {
  if (!status.value) {
    return ''
  }

  if (!status.value.schedulerEnabled) {
    return '定时刷新未启用'
  }

  const minutes = Math.round(status.value.intervalSeconds / 60)
  return minutes > 0 ? `每 ${minutes} 分钟自动刷新` : `每 ${status.value.intervalSeconds} 秒自动刷新`
})

onMounted(load)

async function load(): Promise<void> {
  isLoading.value = true
  errorMessage.value = ''

  try {
    status.value = await fetchQuoteStatus()
  } catch (error) {
    status.value = null
    errorMessage.value = getApiErrorMessage(error, '行情更新状态暂时无法读取。')
  } finally {
    isLoading.value = false
  }
}

async function refresh(scope: QuoteRefreshScope): Promise<void> {
  if (refreshingScope.value) {
    return
  }

  refreshingScope.value = scope
  notice.value = ''
  errorMessage.value = ''

  try {
    const run = await refreshQuotes(scope)
    notice.value = run.fundFailed + run.goldFailed > 0
      ? `刷新完成：成功 ${run.fundSucceeded + run.goldSucceeded} 项，失败 ${run.fundFailed + run.goldFailed} 项。`
      : `刷新完成：更新 ${run.updated} 项，耗时 ${run.durationMs} 毫秒。`
    await load()
  } catch (error) {
    errorMessage.value = getApiErrorMessage(error, '行情刷新失败，请稍后重试。')
  } finally {
    refreshingScope.value = null
  }
}

function goBack(): void {
  void router.push({ name: 'mine' })
}

function statusLabel(value: string): string {
  switch (value) {
    case 'fresh':
      return '已更新'
    case 'stale':
      return '已过期'
    case 'failed':
      return '刷新失败'
    case 'missingCode':
      return '缺少代码'
    case 'notRecorded':
      return '未录入'
    default:
      return '未自动刷新'
  }
}

function statusClass(value: string): string {
  switch (value) {
    case 'fresh':
      return 'fresh'
    case 'failed':
      return 'failed'
    case 'stale':
      return 'stale'
    default:
      return 'quiet'
  }
}

function itemMeta(item: QuoteStatusItem): string {
  return [item.code, item.platform].filter(Boolean).join(' · ')
}

function timestamp(value: string | null): string {
  if (!value) {
    return '—'
  }

  const date = new Date(value)

  if (Number.isNaN(date.getTime())) {
    return value
  }

  return new Intl.DateTimeFormat('zh-CN', {
    month: 'numeric',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit'
  }).format(date)
}

function priceText(item: QuoteStatusItem): string {
  if (item.price === null) {
    return '—'
  }

  return item.assetType === 'gold'
    ? `¥${formatMoney(item.price)}/克`
    : formatQuantity(item.price)
}
</script>

<template>
  <main class="page quote-status-page">
    <header class="quote-header">
      <BackButton @back="goBack" />
      <h1>行情更新状态</h1>
      <span class="header-spacer" />
    </header>

    <section class="quote-card scheduler-card">
      <header>
        <h2>行情定时刷新</h2>
        <span class="scheduler-state">{{ schedulerText }}</span>
      </header>
      <p v-if="status" class="scheduler-meta">
        下次运行 {{ timestamp(status.nextRunAt) }} · 检查于 {{ timestamp(status.checkedAt) }}
        <template v-if="status.isRunning"> · 正在刷新</template>
      </p>
      <div class="refresh-actions">
        <button
          type="button"
          class="refresh-primary"
          :disabled="refreshingScope !== null"
          @click="refresh('all')"
        >
          {{ refreshingScope === 'all' ? '刷新中…' : '刷新全部' }}
        </button>
        <button
          type="button"
          class="refresh-secondary"
          :disabled="refreshingScope !== null"
          @click="refresh('funds')"
        >
          {{ refreshingScope === 'funds' ? '刷新中…' : '刷新基金' }}
        </button>
        <button
          type="button"
          class="refresh-secondary"
          :disabled="refreshingScope !== null"
          @click="refresh('gold')"
        >
          {{ refreshingScope === 'gold' ? '刷新中…' : '刷新积存金报价' }}
        </button>
      </div>
    </section>

    <p v-if="notice" class="notice" role="status">{{ notice }}</p>
    <p v-if="errorMessage" class="form-error" role="alert">{{ errorMessage }}</p>

    <p v-if="isLoading && !status" class="state">正在读取行情状态…</p>

    <template v-else-if="status">
      <section v-if="status.lastRun" class="quote-card last-run-card">
        <header>
          <h2>最近一次刷新</h2>
          <span>{{ status.lastRun.trigger === 'scheduled' ? '定时任务' : '手动刷新' }} · {{ timestamp(status.lastRun.completedAt) }}</span>
        </header>
        <div class="run-metrics">
          <div>
            <span>基金</span>
            <b>{{ status.lastRun.fundSucceeded }}/{{ status.lastRun.fundAttempted }}</b>
          </div>
          <div>
            <span>积存金</span>
            <b>{{ status.lastRun.goldSucceeded }}/{{ status.lastRun.goldAttempted }}</b>
          </div>
          <div>
            <span>更新</span>
            <b>{{ status.lastRun.updated }}</b>
          </div>
          <div>
            <span>耗时</span>
            <b>{{ status.lastRun.durationMs }}ms</b>
          </div>
        </div>
        <ul class="last-run-items">
          <li v-for="item in status.lastRun.items" :key="`${item.assetType}-${item.positionId ?? 'gold'}`">
            <span>{{ item.name }}</span>
            <span>{{ item.success ? '成功' : '失败' }} · 尝试 {{ item.attempts }} 次<template v-if="item.updated"> · 已更新</template></span>
          </li>
        </ul>
        <ul v-if="status.lastRun.errors.length" class="run-errors">
          <li v-for="error in status.lastRun.errors" :key="error">{{ error }}</li>
        </ul>
      </section>

      <section class="quote-card">
        <header>
          <h2>刷新明细</h2>
          <span>{{ status.items.length }} 项</span>
        </header>

        <div v-if="status.items.length" class="quote-item-list">
          <article v-for="item in status.items" :key="`${item.assetType}-${item.positionId ?? 'gold'}`" class="quote-item">
            <div class="quote-item-main">
              <div class="quote-item-title">
                <strong>{{ item.name }}</strong>
                <span class="quote-tag" :class="statusClass(item.status)">{{ statusLabel(item.status) }}</span>
              </div>
              <small>{{ itemMeta(item) || (item.assetType === 'gold' ? '积存金报价' : '未填写代码或平台') }}</small>
            </div>
            <div class="quote-item-value">
              <b>{{ priceText(item) }}</b>
              <small>{{ item.priceDate ? formatBusinessDate(item.priceDate) : '无有效日期' }}</small>
            </div>
            <div class="quote-item-meta">
              <span>上次尝试 {{ timestamp(item.lastAttemptAt) }}</span>
              <span v-if="item.lastSource">{{ item.assetType === 'gold' ? '自动行情' : `来源 ${item.lastSource}` }}</span>
              <span v-if="item.lastError" class="quote-error">{{ item.lastError }}</span>
            </div>
          </article>
        </div>

        <p v-else class="summary-note">还没有基金或积存金持仓。</p>
      </section>

      <p class="page-footnote">
        未披露、刷新失败或过期时保留最近有效报价和日期，不使用 0 或推算值。定时任务失败会按配置重试并写入运行日志。
      </p>
    </template>
  </main>
</template>

<style scoped>
.quote-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
}

.header-spacer {
  width: 36px;
  flex-shrink: 0;
}

.quote-header h1 {
  margin: 0;
  font-size: 1.125rem;
  font-weight: 600;
  letter-spacing: -0.4px;
}

.quote-card {
  margin-top: 14px;
  padding: 14px;
  border: 1px solid var(--color-border-subtle);
  border-radius: var(--radius-card);
  background: var(--color-bg-surface);
}

.quote-card > header {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 10px;
}

.quote-card h2 {
  margin: 0;
  font-size: 0.875rem;
  font-weight: 600;
}

.quote-card header span {
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
}

.quote-card header .scheduler-state {
  color: var(--color-brand);
}

.scheduler-meta {
  margin: 9px 0 0;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
}

.refresh-actions {
  display: grid;
  grid-template-columns: 1.2fr 1fr 1.2fr;
  gap: 7px;
  margin-top: 12px;
}

.refresh-actions button {
  min-height: var(--touch-target);
  border-radius: var(--radius-tag);
  font-size: var(--text-caption);
}

.refresh-primary {
  border: 1px solid var(--color-brand);
  background: var(--color-brand);
  color: var(--color-on-brand);
}

.refresh-secondary {
  border: 1px solid var(--color-border-control);
  background: var(--color-bg-surface);
  color: var(--color-brand);
}

.refresh-actions button:disabled {
  border-color: var(--color-border-disabled);
  background: var(--color-bg-disabled);
  color: var(--color-text-disabled);
}

.notice {
  margin: 12px 0 0;
  padding: 10px 12px;
  border-radius: var(--radius-tag);
  background: var(--color-success-bg);
  color: var(--color-success);
  font-size: var(--text-footnote);
  line-height: 1.7;
}

.form-error {
  margin: 12px 0 0;
  padding: 10px 12px;
  border-radius: var(--radius-tag);
  background: var(--color-error-bg);
  color: var(--color-finance-loss);
  font-size: var(--text-footnote);
  line-height: 1.7;
}

.run-metrics {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 8px;
  margin-top: 12px;
}

.run-metrics > div {
  padding: 8px;
  border-radius: var(--radius-tag);
  background: var(--color-bg-subtle);
}

.run-metrics span {
  display: block;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
}

.run-metrics b {
  display: block;
  margin-top: 3px;
  font-size: 0.75rem;
  font-weight: 500;
  font-variant-numeric: tabular-nums;
}

.last-run-items {
  margin: 10px 0 0;
  padding: 0;
  list-style: none;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
  line-height: 1.7;
}

.last-run-items li {
  display: flex;
  justify-content: space-between;
  gap: 8px;
  padding: 3px 0;
  border-top: 1px dashed var(--color-border-subtle);
}

.last-run-items li span:last-child {
  flex: 0 0 auto;
}

.run-errors {
  margin: 10px 0 0;
  padding-left: 16px;
  color: var(--color-finance-loss);
  font-size: var(--text-caption);
  line-height: 1.7;
}

.quote-item-list {
  display: flex;
  flex-direction: column;
  gap: 9px;
  margin-top: 12px;
}

.quote-item {
  display: grid;
  grid-template-columns: 1fr auto;
  gap: 4px 10px;
  padding: 11px;
  border: 1px solid var(--color-border-subtle);
  border-radius: var(--radius-control);
}

.quote-item-title {
  display: flex;
  align-items: center;
  gap: 6px;
}

.quote-item-title strong {
  font-size: var(--text-footnote);
  font-weight: 600;
}

.quote-item-main small,
.quote-item-value small {
  display: block;
  margin-top: 3px;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
}

.quote-item-value {
  text-align: right;
}

.quote-item-value b {
  font-size: 0.75rem;
  font-weight: 500;
  font-variant-numeric: tabular-nums;
}

.quote-item-meta {
  grid-column: 1 / -1;
  display: flex;
  flex-wrap: wrap;
  gap: 4px 9px;
  margin-top: 4px;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
}

.quote-error {
  color: var(--color-finance-loss);
}

.quote-tag {
  padding: 2px 5px;
  border-radius: var(--radius-pill);
  font-size: var(--text-caption);
}

.quote-tag.fresh {
  background: var(--color-success-bg);
  color: var(--color-success);
}

.quote-tag.failed {
  background: var(--color-error-bg);
  color: var(--color-finance-loss);
}

.quote-tag.stale {
  background: var(--color-warning-bg);
  color: var(--color-warning);
}

.quote-tag.quiet {
  background: var(--color-bg-subtle);
  color: var(--color-text-secondary);
}

.summary-note,
.page-footnote {
  margin: 10px 0 0;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
  line-height: 1.7;
}

.page-footnote {
  margin-top: 18px;
}
</style>
