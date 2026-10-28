<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { fetchFundReturnDay, fetchFundReturns } from '../api/funds'
import { getApiErrorMessage } from '../api/client'
import BottomSheet from './BottomSheet.vue'
import FundReturnChart from './FundReturnChart.vue'
import { formatBusinessDate, formatMoney, formatSignedMoney, todayInBusinessTimeZone } from '../utils/format'
import type { TrendRange } from '../types/trend'
import type { FundReturn, FundReturnDay, FundReturnPoint } from '../types/fundReturn'

const props = defineProps<{
  groupId: string | null
  ungrouped: boolean
}>()

const rangeOptions: { key: TrendRange; label: string }[] = [
  { key: 'month', label: '本月' },
  { key: 'quarter', label: '近3月' },
  { key: 'year', label: '今年' },
  { key: 'all', label: '全部' }
]
const today = todayInBusinessTimeZone()
const range = ref<TrendRange>('month')
const data = ref<FundReturn | null>(null)
const isLoading = ref(false)
const errorMessage = ref('')
const calendarMonth = ref(today.slice(0, 7))
const daySheetOpen = ref(false)
const selectedDate = ref('')
const day = ref<FundReturnDay | null>(null)
const dayLoading = ref(false)
const dayError = ref('')

const minMonth = computed(() => data.value?.from.slice(0, 7) ?? calendarMonth.value)
const maxMonth = computed(() => data.value?.to.slice(0, 7) ?? calendarMonth.value)
const monthLabel = computed(() => {
  const [year, month] = calendarMonth.value.split('-')
  return `${year}年${Number(month)}月`
})
const pointByDate = computed(() => new Map((data.value?.points ?? []).map((point) => [point.date, point])))
const calendarCells = computed(() => buildCalendar(calendarMonth.value))
const canGoPrev = computed(() => calendarMonth.value > minMonth.value)
const canGoNext = computed(() => calendarMonth.value < maxMonth.value)

onMounted(load)

watch(
  () => [props.groupId, props.ungrouped],
  () => {
    void load()
  }
)

watch(range, () => {
  void load()
})

async function load(): Promise<void> {
  isLoading.value = true
  errorMessage.value = ''

  try {
    data.value = await fetchFundReturns(range.value, props.groupId, props.ungrouped)

    if (!data.value.points.some((point) => point.date.startsWith(calendarMonth.value))) {
      calendarMonth.value = data.value.to.slice(0, 7)
    }
  } catch (error) {
    data.value = null
    errorMessage.value = getApiErrorMessage(error, '投资收益走势暂时无法读取。')
  } finally {
    isLoading.value = false
  }
}

function buildCalendar(month: string): { date: string; day: number; inRange: boolean; point: FundReturnPoint | null }[] {
  const [year, monthIndex] = month.split('-').map(Number)
  const days = new Date(Date.UTC(year!, monthIndex!, 0)).getUTCDate()
  const firstWeekday = new Date(Date.UTC(year!, monthIndex! - 1, 1)).getUTCDay()
  const offset = (firstWeekday + 6) % 7
  const cells: { date: string; day: number; inRange: boolean; point: FundReturnPoint | null }[] = []

  for (let index = 0; index < offset; index++) {
    cells.push({ date: '', day: 0, inRange: false, point: null })
  }

  for (let dayNumber = 1; dayNumber <= days; dayNumber++) {
    const date = `${year}-${String(monthIndex).padStart(2, '0')}-${String(dayNumber).padStart(2, '0')}`
    const inRange = data.value !== null && date >= data.value.from && date <= data.value.to
    cells.push({
      date,
      day: dayNumber,
      inRange,
      point: pointByDate.value.get(date) ?? null
    })
  }

  return cells
}

function shiftMonth(offset: number): void {
  const [year, month] = calendarMonth.value.split('-').map(Number)
  const next = new Date(Date.UTC(year!, month! - 1 + offset, 1))
  const value = `${next.getUTCFullYear()}-${String(next.getUTCMonth() + 1).padStart(2, '0')}`

  if (value >= minMonth.value && value <= maxMonth.value) {
    calendarMonth.value = value
  }
}

function dayClass(point: FundReturnPoint | null): string {
  if (!point || point.dailyProfit === null) {
    return 'quiet'
  }

  return point.dailyProfit > 0 ? 'profit' : point.dailyProfit < 0 ? 'loss' : 'quiet'
}

function dayText(point: FundReturnPoint | null): string {
  if (!point || point.dailyProfit === null) {
    return '—'
  }

  return formatSignedMoney(point.dailyProfit)
}

async function openDay(date: string): Promise<void> {
  if (!date) {
    return
  }

  selectedDate.value = date
  daySheetOpen.value = true
  day.value = null
  dayError.value = ''
  dayLoading.value = true

  try {
    day.value = await fetchFundReturnDay(date, props.groupId, props.ungrouped)
  } catch (error) {
    dayError.value = getApiErrorMessage(error, '当日收益暂时无法读取。')
  } finally {
    dayLoading.value = false
  }
}

function statusLabel(status: string): string {
  switch (status) {
    case 'complete':
      return '已披露'
    case 'partial':
      return '部分披露'
    case 'notDisclosed':
      return '未披露'
    default:
      return '依据不足'
  }
}

function rowTitle(shareClass: string | null, platform: string | null): string {
  return [shareClass ? `${shareClass} 类` : null, platform].filter(Boolean).join(' · ')
}
</script>

<template>
  <section class="return-panel">
    <div class="return-head">
      <div>
        <h2>投资收益走势</h2>
        <p>按净值披露日归属，未披露日期不计收益</p>
      </div>
      <div class="range-tabs" role="tablist" aria-label="收益区间">
        <button
          v-for="option in rangeOptions"
          :key="option.key"
          type="button"
          :class="{ active: range === option.key }"
          role="tab"
          :aria-selected="range === option.key"
          @click="range = option.key"
        >
          {{ option.label }}
        </button>
      </div>
    </div>

    <p v-if="isLoading && !data" class="state">正在读取投资收益…</p>
    <section v-else-if="errorMessage" class="state">
      <p>{{ errorMessage }}</p>
      <button type="button" class="secondary-button" @click="load">重新加载</button>
    </section>

    <template v-else-if="data">
      <div class="return-summary">
        <div>
          <span>区间盈亏</span>
          <b :class="{ profit: (data.intervalProfit ?? 0) > 0, loss: (data.intervalProfit ?? 0) < 0 }">
            {{ data.intervalProfit === null ? '—' : formatSignedMoney(data.intervalProfit) }}
          </b>
          <small>自 {{ formatBusinessDate(data.intervalFrom) }} 起，含在途</small>
        </div>
        <div>
          <span>持有收益</span>
          <b :class="{ profit: (data.holdingProfit ?? 0) > 0, loss: (data.holdingProfit ?? 0) < 0 }">
            {{ data.holdingProfit === null ? '—' : formatSignedMoney(data.holdingProfit) }}
          </b>
          <small>当前已确认持仓估值 − 成本</small>
        </div>
        <div>
          <span>区间收益率</span>
          <b>{{ data.intervalRate === null ? '—' : `${formatSignedMoney(data.intervalRate)}%` }}</b>
          <small>以区间起始资产为分母，非年化</small>
        </div>
      </div>

      <p class="return-meta">
        {{ data.points.length }} 天 · 净值披露 {{ data.disclosureDays }} 天 · 区间
        {{ formatBusinessDate(data.from) }} — {{ formatBusinessDate(data.to) }}
      </p>

      <div v-if="data.points.length" class="chart-card">
        <FundReturnChart :points="data.points" :range="data.range" />
      </div>

      <div class="calendar-card">
        <header class="calendar-head">
          <button type="button" :disabled="!canGoPrev" aria-label="上个月" @click="shiftMonth(-1)">‹</button>
          <strong>{{ monthLabel }}</strong>
          <button type="button" :disabled="!canGoNext" aria-label="下个月" @click="shiftMonth(1)">›</button>
        </header>
        <div class="calendar-weekdays" aria-hidden="true">
          <span>一</span><span>二</span><span>三</span><span>四</span><span>五</span><span>六</span><span>日</span>
        </div>
        <div class="calendar-grid">
          <button
            v-for="(cell, index) in calendarCells"
            :key="`${cell.date}-${index}`"
            type="button"
            class="calendar-day"
            :class="[dayClass(cell.point), { empty: !cell.date, outside: cell.date && !cell.inRange }]"
            :disabled="!cell.date || !cell.inRange"
            @click="openDay(cell.date)"
          >
            <template v-if="cell.date">
              <span>{{ cell.day }}</span>
              <small v-if="cell.inRange">{{ dayText(cell.point) }}</small>
            </template>
          </button>
        </div>
        <p class="calendar-note">“—”表示当天没有新净值披露或依据不足，不代表收益为 0。点击日期查看各基金明细。</p>
      </div>

      <ul v-if="data.notes.length" class="return-notes">
        <li v-for="note in data.notes" :key="note">{{ note }}</li>
      </ul>
    </template>

    <BottomSheet
      :open="daySheetOpen"
      :title="selectedDate ? `${formatBusinessDate(selectedDate)}收益` : '当日收益'"
      subtitle="按净值披露日归属"
      @close="daySheetOpen = false"
    >
      <p v-if="dayLoading" class="state">正在读取当日收益…</p>
      <section v-else-if="dayError" class="state">
        <p>{{ dayError }}</p>
      </section>
      <template v-else-if="day">
        <div class="day-summary">
          <div>
            <span>当日合计</span>
            <b :class="{ profit: (day.totalProfit ?? 0) > 0, loss: (day.totalProfit ?? 0) < 0 }">
              {{ day.totalProfit === null ? '—' : formatSignedMoney(day.totalProfit) }}
            </b>
          </div>
          <p>{{ statusLabel(day.status) }} · {{ day.coveredPositions }}/{{ day.totalPositions }} 个持仓可计算</p>
        </div>

        <section v-if="day.positions.length" class="day-position-list">
          <article v-for="row in day.positions" :key="row.positionId" class="day-position">
            <div>
              <strong>{{ row.name }}</strong>
              <small>{{ rowTitle(row.shareClass, row.platform) || '未标类别' }}</small>
            </div>
            <div class="day-position-value">
              <b :class="{ profit: (row.dailyProfit ?? 0) > 0, loss: (row.dailyProfit ?? 0) < 0 }">
                {{ row.dailyProfit === null ? '—' : formatSignedMoney(row.dailyProfit) }}
              </b>
              <small>
                估值 {{ row.value === null ? '—' : `¥${formatMoney(row.value)}` }}
                · 净值 {{ row.netValue === null ? '—' : row.netValue }}
              </small>
              <small>{{ statusLabel(row.status) }}<template v-if="row.note"> · {{ row.note }}</template></small>
            </div>
          </article>
        </section>

        <ul v-if="day.notes.length" class="return-notes">
          <li v-for="note in day.notes" :key="note">{{ note }}</li>
        </ul>
      </template>
    </BottomSheet>
  </section>
</template>

<style scoped>
.return-panel {
  margin-top: 18px;
}

.return-head {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 10px;
}

.return-head h2 {
  margin: 0;
  font-size: 0.875rem;
  font-weight: 600;
}

.return-head p {
  margin: 4px 0 0;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
}

.range-tabs {
  display: flex;
  flex: 0 0 auto;
  gap: 3px;
  padding: 3px;
  border-radius: var(--radius-control);
  background: var(--color-brand-soft);
}

.range-tabs button {
  min-height: var(--touch-target);
  padding: 0 7px;
  border: 0;
  border-radius: var(--radius-tag);
  background: transparent;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
}

.range-tabs button.active {
  background: var(--color-bg-surface);
  color: var(--color-brand);
  font-weight: 600;
}

.return-summary {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 8px;
  margin-top: 12px;
}

.return-summary > div {
  min-width: 0;
  padding: 11px 9px;
  border: 1px solid var(--color-border-subtle);
  border-radius: var(--radius-control);
  background: var(--color-bg-surface);
}

.return-summary span {
  display: block;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
}

.return-summary b {
  display: block;
  margin-top: 5px;
  overflow: hidden;
  font-size: 0.8125rem;
  font-weight: 500;
  font-variant-numeric: tabular-nums;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.return-summary small {
  display: block;
  margin-top: 4px;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
  line-height: 1.45;
}

.profit {
  color: var(--color-finance-gain);
}

.loss {
  color: var(--color-finance-loss);
}

.return-meta {
  margin: 9px 0 0;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
}

.chart-card {
  margin-top: 10px;
  padding: 8px 4px 2px;
  border: 1px solid var(--color-border-subtle);
  border-radius: var(--radius-card);
  background: var(--color-bg-surface);
}

.calendar-card {
  margin-top: 12px;
  padding: 12px;
  border: 1px solid var(--color-border-subtle);
  border-radius: var(--radius-card);
  background: var(--color-bg-surface);
}

.calendar-head {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.calendar-head strong {
  font-size: 0.75rem;
  font-weight: 600;
}

.calendar-head button {
  width: 30px;
  min-height: var(--touch-target);
  border: 0;
  border-radius: var(--radius-tag);
  background: transparent;
  color: var(--color-brand);
  font-size: 1rem;
}

.calendar-head button:disabled {
  color: var(--color-text-disabled);
}

.calendar-weekdays,
.calendar-grid {
  display: grid;
  grid-template-columns: repeat(7, minmax(0, 1fr));
  gap: 3px;
}

.calendar-weekdays {
  margin-top: 8px;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
  text-align: center;
}

.calendar-grid {
  margin-top: 3px;
}

.calendar-day {
  display: flex;
  flex-direction: column;
  justify-content: center;
  align-items: center;
  gap: 2px;
  min-height: 42px;
  padding: 2px 1px;
  border: 1px solid transparent;
  border-radius: var(--radius-tag);
  background: transparent;
}

.calendar-day span {
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
}

.calendar-day small {
  font-size: var(--text-caption);
  font-variant-numeric: tabular-nums;
}

.calendar-day.profit {
  background: var(--color-finance-gain-bg);
}

.calendar-day.loss {
  background: var(--color-finance-loss-bg);
}

.calendar-day.quiet {
  background: var(--color-bg-subtle);
}

.calendar-day.empty,
.calendar-day.outside {
  background: transparent;
}

.calendar-day:disabled {
  opacity: 0.55;
}

.calendar-day:not(:disabled):active {
  border-color: var(--color-brand);
}

.calendar-note {
  margin: 9px 0 0;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
  line-height: 1.6;
}

.return-notes {
  margin: 10px 0 0;
  padding-left: 16px;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
  line-height: 1.7;
}

.day-summary {
  display: flex;
  justify-content: space-between;
  align-items: flex-end;
  gap: 10px;
  padding-bottom: 12px;
  border-bottom: 1px solid var(--color-border-subtle);
}

.day-summary span {
  display: block;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
}

.day-summary b {
  display: block;
  margin-top: 5px;
  font-size: 1.25rem;
  font-weight: 500;
  font-variant-numeric: tabular-nums;
}

.day-summary p {
  margin: 0;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
  text-align: right;
}

.day-position-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin-top: 12px;
}

.day-position {
  display: flex;
  justify-content: space-between;
  gap: 10px;
  padding: 10px;
  border: 1px solid var(--color-border-subtle);
  border-radius: var(--radius-control);
}

.day-position strong {
  display: block;
  font-size: var(--text-footnote);
  font-weight: 600;
}

.day-position small {
  display: block;
  margin-top: 3px;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
  line-height: 1.5;
}

.day-position-value {
  flex: 0 0 auto;
  text-align: right;
}

.day-position-value b {
  font-size: 0.75rem;
  font-weight: 500;
  font-variant-numeric: tabular-nums;
}
</style>
