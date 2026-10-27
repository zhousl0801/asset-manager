<script setup lang="ts">
import { computed } from 'vue'
import { todayInBusinessTimeZone } from '../utils/format'
import type { MarketDay, TradingDay } from '../types/plan'

const props = defineProps<{
  year: number
  month: number
  days: TradingDay[]
  loading?: boolean
  error?: string
}>()

const emit = defineEmits<{ prev: []; next: []; retry: [] }>()

const today = todayInBusinessTimeZone()
const weekdays = ['一', '二', '三', '四', '五', '六', '日']

const monthLabel = computed(() => `${props.year}年${props.month}月`)
const leadingBlanks = computed(() => {
  const first = new Date(Date.UTC(props.year, props.month - 1, 1))
  return (first.getUTCDay() + 6) % 7
})
const cells = computed<Array<TradingDay | null>>(() => {
  const result: Array<TradingDay | null> = Array.from({ length: leadingBlanks.value }, () => null)
  result.push(...props.days)

  while (result.length % 7 !== 0) {
    result.push(null)
  }

  return result
})
const overseasClosedDays = computed(() => props.days.filter(day => overseasClosed(day).length > 0))
const coverage = computed(() => {
  const markets = new Map<string, MarketDay>()

  for (const day of props.days) {
    for (const market of day.markets) {
      markets.set(`${market.market}-${market.year}`, market)
    }
  }

  return [...markets.values()].sort((first, second) => first.market.localeCompare(second.market))
})
const hasWeekdayFallback = computed(() => coverage.value.some(market => market.usesWeekdayFallback))

function marketOf(day: TradingDay, code: string): MarketDay | undefined {
  return day.markets.find(market => market.market === code)
}

function chinaOpen(day: TradingDay): boolean {
  return marketOf(day, 'CN')?.isOpen ?? false
}

function overseasClosed(day: TradingDay): MarketDay[] {
  if (!chinaOpen(day)) {
    return []
  }

  return day.markets.filter(market =>
    (market.market === 'US' || market.market === 'HK') && !market.isOpen)
}

function dayNumber(day: TradingDay): number {
  return Number(day.date.slice(8, 10))
}

function cellClass(day: TradingDay | null): Record<string, boolean> {
  if (!day) {
    return { blank: true }
  }

  return {
    'china-closed': !chinaOpen(day),
    'has-overseas-closure': overseasClosed(day).length > 0,
    today: day.date === today
  }
}

function closureLabel(market: MarketDay): string {
  return market.market === 'US' ? '美' : '港'
}

function closureTitle(market: MarketDay): string {
  return `${market.name}休市`
}
</script>

<template>
  <section class="calendar-card" aria-label="交易日历">
    <header class="calendar-head">
      <div class="calendar-title">
        <strong>交易日历</strong>
        <span class="calendar-tag">中国时间</span>
      </div>
      <div class="calendar-nav">
        <button type="button" aria-label="上个月" @click="emit('prev')">‹</button>
        <b>{{ monthLabel }}</b>
        <button type="button" aria-label="下个月" @click="emit('next')">›</button>
      </div>
    </header>

    <p class="calendar-note">按中国日期展示；浅色为中国休市，美/港标记为海外休市，基金定投会顺延到下一个三地交易日。</p>

    <div class="calendar-legend">
      <span><i class="legend-dot china" />中国休市</span>
      <span><i class="legend-dot us">美</i>美股休市</span>
      <span><i class="legend-dot hk">港</i>港股休市</span>
    </div>

    <div v-if="loading" class="calendar-state">正在读取日历…</div>
    <div v-else-if="error" class="calendar-state error">
      <span>{{ error }}</span>
      <button type="button" class="text-button" @click="emit('retry')">重试</button>
    </div>
    <template v-else>
      <div class="calendar-weekdays">
        <span v-for="weekday in weekdays" :key="weekday">{{ weekday }}</span>
      </div>
      <div class="calendar-grid">
        <div
          v-for="(day, index) in cells"
          :key="day?.date ?? `blank-${index}`"
          class="calendar-cell"
          :class="cellClass(day)"
        >
          <template v-if="day">
            <span class="day-number">{{ dayNumber(day) }}</span>
            <span v-if="overseasClosed(day).length" class="closure-tags">
              <span
                v-for="market in overseasClosed(day)"
                :key="market.market"
                class="closure-tag"
                :class="market.market.toLowerCase()"
                :title="closureTitle(market)"
              >{{ closureLabel(market) }}</span>
            </span>
          </template>
        </div>
      </div>
      <p class="calendar-summary">
        <template v-if="overseasClosedDays.length">
          本月有 {{ overseasClosedDays.length }} 天因海外休市，定投日顺延。
        </template>
        <template v-else>
          本月中国交易日三地均开市。
        </template>
      </p>
    </template>

    <details v-if="coverage.length" class="calendar-sources">
      <summary>日历来源与覆盖</summary>
      <div v-for="market in coverage" :key="`${market.market}-${market.year}`" class="calendar-source">
        <span>{{ market.name }} {{ market.year }} · {{ market.usesWeekdayFallback ? '工作日回退' : '官方日历' }}</span>
        <a v-if="market.sourceUrl" :href="market.sourceUrl" target="_blank" rel="noopener noreferrer">查看来源</a>
        <small v-if="market.publishedOn">发布 {{ market.publishedOn }}</small>
        <small v-else-if="market.verifiedOn">核验 {{ market.verifiedOn }}（来源未标发布日期）</small>
      </div>
      <p v-if="hasWeekdayFallback" class="calendar-footnote">部分年份尚未维护官方日历，暂按周一至周五回退；临时休市需自行核对。</p>
      <p class="calendar-footnote">半日市照常执行；中国、美国、香港均开市时基金定投才会记账。</p>
    </details>
  </section>
</template>

<style scoped>
.calendar-card {
  margin-top: 14px;
  padding: 13px;
  border: 1px solid var(--color-border-subtle);
  border-radius: 13px;
  background: var(--color-bg-surface);
}

.calendar-head {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 10px;
}

.calendar-title {
  display: flex;
  align-items: center;
  gap: 7px;
  min-width: 0;
}

.calendar-title strong {
  font-size: 13px;
  font-weight: 600;
}

.calendar-tag {
  padding: 2px 5px;
  border-radius: 4px;
  background: var(--color-brand-soft);
  color: var(--color-brand);
  font-size: 9px;
  font-weight: 550;
  white-space: nowrap;
}

.calendar-nav {
  display: flex;
  align-items: center;
  gap: 2px;
}

.calendar-nav button {
  width: 28px;
  height: 28px;
  padding: 0;
  border: 0;
  border-radius: 6px;
  background: transparent;
  color: var(--color-brand);
  font-size: 18px;
  line-height: 1;
}

.calendar-nav button:hover {
  background: var(--color-brand-soft);
}

.calendar-nav b {
  min-width: 86px;
  color: var(--color-text-primary);
  font-size: 12px;
  font-weight: 600;
  text-align: center;
  white-space: nowrap;
}

.calendar-note {
  margin: 8px 0 0;
  color: var(--color-text-secondary);
  font-size: 9px;
  line-height: 1.6;
}

.calendar-legend {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px 12px;
  margin-top: 9px;
  color: var(--color-text-secondary);
  font-size: 9px;
}

.calendar-legend > span {
  display: inline-flex;
  align-items: center;
  gap: 4px;
}

.legend-dot {
  display: inline-grid;
  place-items: center;
  width: 14px;
  height: 14px;
  border-radius: 4px;
  font-size: 7px;
  font-style: normal;
  font-weight: 600;
}

.legend-dot.china {
  background: var(--color-bg-subtle);
}

.legend-dot.us {
  background: var(--color-warning-bg);
  color: var(--color-warning);
}

.legend-dot.hk {
  background: var(--color-brand-soft);
  color: var(--color-brand);
}

.calendar-state {
  padding: 22px 0 18px;
  color: var(--color-text-secondary);
  font-size: 11px;
  text-align: center;
}

.calendar-state.error {
  display: flex;
  justify-content: center;
  align-items: center;
  gap: 8px;
  color: var(--color-error);
}

.text-button {
  padding: 0;
  border: 0;
  background: transparent;
  color: var(--color-brand);
  font-size: 11px;
}

.calendar-weekdays {
  display: grid;
  grid-template-columns: repeat(7, minmax(0, 1fr));
  margin-top: 10px;
  padding-bottom: 5px;
  color: var(--color-text-secondary);
  font-size: 9px;
  text-align: center;
}

.calendar-grid {
  display: grid;
  grid-template-columns: repeat(7, minmax(0, 1fr));
  gap: 2px;
}

.calendar-cell {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: flex-start;
  gap: 2px;
  box-sizing: border-box;
  min-height: 42px;
  padding: 5px 1px 3px;
  border: 1px solid transparent;
  border-radius: 7px;
}

.calendar-cell.blank {
  background: transparent;
}

.calendar-cell.china-closed {
  background: var(--color-bg-subtle);
  color: var(--color-text-disabled);
}

.calendar-cell.has-overseas-closure {
  border-color: color-mix(in srgb, var(--color-warning) 16%, transparent);
  background: color-mix(in srgb, var(--color-warning-bg) 55%, var(--color-bg-surface));
}

.calendar-cell.today {
  border-color: var(--color-brand);
  background: var(--color-brand-soft);
}

.day-number {
  font-size: 11px;
  font-weight: 500;
  font-variant-numeric: tabular-nums;
  line-height: 1.2;
}

.calendar-cell.today .day-number {
  color: var(--color-brand);
  font-weight: 650;
}

.closure-tags {
  display: inline-flex;
  align-items: center;
  gap: 2px;
}

.closure-tag {
  display: inline-grid;
  place-items: center;
  min-width: 13px;
  height: 13px;
  padding: 0 2px;
  border-radius: 3px;
  background: var(--color-warning-bg);
  color: var(--color-warning);
  font-size: 8px;
  font-weight: 600;
  line-height: 1;
}

.closure-tag.hk {
  background: var(--color-brand-soft);
  color: var(--color-brand);
}

.calendar-summary {
  margin: 9px 0 0;
  color: var(--color-text-secondary);
  font-size: 9px;
  line-height: 1.6;
}

.calendar-sources {
  margin-top: 5px;
  border-top: 1px solid var(--color-border-subtle);
}

.calendar-sources > summary {
  padding: 9px 0 2px;
  color: var(--color-text-secondary);
  font-size: 9px;
  cursor: pointer;
  list-style: none;
}

.calendar-sources > summary::-webkit-details-marker {
  display: none;
}

.calendar-sources > summary::after {
  content: '⌄';
  margin-left: 4px;
  color: var(--color-brand);
}

.calendar-sources[open] > summary::after {
  display: inline-block;
  transform: rotate(180deg);
}

.calendar-source {
  display: flex;
  flex-wrap: wrap;
  gap: 3px 10px;
  margin-top: 7px;
  color: var(--color-text-secondary);
  font-size: 9px;
  line-height: 1.6;
}

.calendar-source a {
  color: var(--color-brand);
  text-decoration: none;
}

.calendar-source small {
  flex-basis: 100%;
}

.calendar-footnote {
  margin: 7px 0 0;
  color: var(--color-text-secondary);
  font-size: 9px;
  line-height: 1.6;
}
</style>
