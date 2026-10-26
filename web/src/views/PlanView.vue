<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  fetchPlanOverview,
  pausePlan,
  previewPlans,
  recordOccurrence,
  resumePlan,
  retryOccurrence,
  runPlans,
  savePlan,
  skipOccurrence
} from '../api/plans'
import { fetchFundOverview } from '../api/funds'
import { ApiError, getApiErrorMessage } from '../api/client'
import AppNav from '../components/AppNav.vue'
import BottomSheet from '../components/BottomSheet.vue'
import { createRequestId } from '../utils/request'
import {
  formatBusinessDate,
  formatMoney,
  parseAmount,
  todayInBusinessTimeZone
} from '../utils/format'
import type { FundPosition } from '../types/fund'
import type {
  AutoPlan,
  OccurrenceStatus,
  PlanCycle,
  PlanOverview,
  PlanPreview,
  PlanType,
  SavePlanInput
} from '../types/plan'

const router = useRouter()
const route = useRoute()
const today = todayInBusinessTimeZone()
const overview = ref<PlanOverview | null>(null)
const positions = ref<FundPosition[]>([])
const positionsLoading = ref(false)
const positionsError = ref('')
const isLoading = ref(false)
const errorMessage = ref('')
const notice = ref('')
const sheetOpen = ref(false)
const editingPlan = ref<AutoPlan | null>(null)
const isSaving = ref(false)
const formError = ref('')
const busyAction = ref('')
const previewDate = ref(today)
const trialCash = ref('')
const preview = ref<PlanPreview | null>(null)
const previewError = ref('')
const isPreviewing = ref(false)

const form = reactive({
  type: 'salaryIncome' as PlanType,
  name: '',
  amount: '',
  cycle: 'monthly' as PlanCycle,
  dayOfMonth: '15',
  dayOfWeek: '1',
  time: '09:00',
  startDate: today,
  fundPositionId: ''
})

const fundChoices = computed(() => {
  const choices = new Map<string, FundPosition & { sourceIds: string[] }>()

  for (const position of positions.value) {
    const normalize = (value: string | null) => (value ?? '').normalize('NFKC').replace(/\s/g, '').toUpperCase()
    const key = `${position.code ? `code:${normalize(position.code)}` : `name:${normalize(position.name)}`}|${normalize(position.shareClass)}`
    const existing = choices.get(key)

    if (existing) existing.sourceIds.push(position.id)
    else choices.set(key, { ...position, sourceIds: [position.id] })
  }

  return [...choices.values()]
})
const calendarCoverage = computed(() => {
  const markets = new Map<string, NonNullable<PlanOverview['calendarDays']>[number]['markets'][number]>()

  for (const day of overview.value?.calendarDays ?? []) {
    for (const market of day.markets) markets.set(`${market.market}-${market.year}`, market)
  }

  return [...markets.values()]
})
const nextTradingDates = computed(() => (overview.value?.calendarDays ?? [])
  .filter(day => day.date > today && day.isTradingDay).slice(0, 10))

const typeOptions: { value: PlanType; label: string }[] = [
  { value: 'salaryIncome', label: '工资到账' },
  { value: 'providentDeposit', label: '公积金缴存' },
  { value: 'providentWithdrawal', label: '公积金提取' },
  { value: 'fundSip', label: '基金定投' }
]

const weekdayLabels = ['一', '二', '三', '四', '五', '六', '日']

const sheetTitle = computed(() => editingPlan.value ? '编辑自动计划' : '新建自动计划')
const actionable = computed(() => overview.value?.pendingOccurrences ?? [])
const canSubmit = computed(() => {
  if (isSaving.value || (parseAmount(form.amount) ?? 0) <= 0) {
    return false
  }

  if (form.type === 'fundSip' && !fundChoices.value.some(item => item.id === form.fundPositionId)) {
    return false
  }

  return form.time !== '' && form.startDate !== ''
})

watch(
  () => form.type,
  (type) => {
    if (type === 'fundSip') {
      void loadPositions()
    }
  }
)

onMounted(() => {
  if (route.query.new === 'providentDeposit' || route.query.new === 'salaryIncome') {
    openNew(route.query.new)
  }
  void load()
})

async function load(): Promise<void> {
  isLoading.value = true
  errorMessage.value = ''

  try {
    overview.value = await fetchPlanOverview()
  } catch (error) {
    overview.value = null

    if (error instanceof ApiError && error.status === 404) {
      await router.replace({ name: 'setup' })
      return
    }

    errorMessage.value = getApiErrorMessage(error, '自动记账计划加载失败，请重试。')
  } finally {
    isLoading.value = false
  }
}

async function loadPositions(): Promise<void> {
  positionsLoading.value = true
  positionsError.value = ''
  try {
    positions.value = (await fetchFundOverview()).positions
    const choice = fundChoices.value.find(item => item.sourceIds.includes(form.fundPositionId))
    if (choice) form.fundPositionId = choice.id
  } catch {
    positions.value = []
    positionsError.value = '基金类别加载失败，请重试。'
  } finally {
    positionsLoading.value = false
  }
}

function openNew(type: PlanType = 'salaryIncome'): void {
  editingPlan.value = null
  formError.value = ''
  form.type = type
  form.name = ''
  form.amount = ''
  form.cycle = 'monthly'
  form.dayOfMonth = '15'
  form.dayOfWeek = '1'
  form.time = '09:00'
  form.startDate = today
  form.fundPositionId = ''
  sheetOpen.value = true

  if (type === 'fundSip') {
    void loadPositions()
  }
}

function openEdit(plan: AutoPlan): void {
  editingPlan.value = plan
  formError.value = ''
  form.type = plan.type
  form.name = plan.name
  form.amount = String(plan.amount)
  form.cycle = plan.cycle
  form.dayOfMonth = plan.dayOfMonth ? String(plan.dayOfMonth) : '15'
  form.dayOfWeek = plan.dayOfWeek ? String(plan.dayOfWeek) : '1'
  form.time = plan.time.slice(0, 5)
  form.startDate = plan.startDate
  form.fundPositionId = plan.fundPositionId ?? ''
  sheetOpen.value = true

  if (plan.type === 'fundSip') {
    void loadPositions()
  }
}

function closeSheet(): void {
  if (!isSaving.value) {
    sheetOpen.value = false
  }
}

async function submit(): Promise<void> {
  const amount = parseAmount(form.amount)

  if (amount === null || amount <= 0) {
    formError.value = '请填写有效的计划金额。'
    return
  }

  const target = fundChoices.value.find(item => item.id === form.fundPositionId)

  if (form.type === 'fundSip' && !target) {
    formError.value = positionsError.value || '请选择基金及份额类别。'
    return
  }

  const input: SavePlanInput = {
    id: editingPlan.value?.id ?? null,
    type: form.type,
    name: form.name.trim(),
    amount,
    cycle: form.type === 'fundSip' ? form.cycle : 'monthly',
    dayOfMonth: (form.type === 'fundSip' && form.cycle !== 'monthly') ? null : Number(form.dayOfMonth),
    dayOfWeek: (form.type === 'fundSip' && form.cycle === 'weekly') ? Number(form.dayOfWeek) : null,
    time: form.time.length === 5 ? `${form.time}:00` : form.time,
    startDate: form.startDate,
    fundPositionId: null,
    fundTarget: form.type === 'fundSip' && target
      ? { name: target.name, code: target.code, shareClass: target.shareClass } : null,
    requestId: createRequestId(),
    expectedVersion: editingPlan.value?.version ?? null
  }

  isSaving.value = true
  formError.value = ''

  try {
    await savePlan(input)
    notice.value = editingPlan.value ? '计划已更新，后续期次按新配置生成。' : '计划已保存。'
    sheetOpen.value = false
    await load()
  } catch (error) {
    formError.value = getApiErrorMessage(error, '保存计划失败，请重试。')
  } finally {
    isSaving.value = false
  }
}

async function togglePlan(plan: AutoPlan): Promise<void> {
  busyAction.value = plan.id

  try {
    if (plan.isActive) {
      await pausePlan(plan.id, createRequestId())
      notice.value = '计划已暂停；已有期次保留，暂停期间不追补。'
    } else {
      await resumePlan(plan.id, createRequestId())
      notice.value = '计划已恢复，从下一期继续。'
    }

    await load()
  } catch (error) {
    notice.value = getApiErrorMessage(error, '操作失败，请重试。')
  } finally {
    busyAction.value = ''
  }
}

async function handleOccurrence(occurrenceId: string, action: 'retry' | 'skip'): Promise<void> {
  busyAction.value = occurrenceId

  try {
    if (action === 'retry') {
      await retryOccurrence(occurrenceId, createRequestId())
      notice.value = '已重新排队；余额补足后点击“立即执行”或等待后台任务。'
    } else {
      await skipOccurrence(occurrenceId, createRequestId())
      notice.value = '本期已跳过，不会再次生成。'
    }

    await load()
  } catch (error) {
    notice.value = getApiErrorMessage(error, '操作失败，请重试。')
  } finally {
    busyAction.value = ''
  }
}

async function recordCurrentOccurrence(occurrenceId: string): Promise<void> {
  busyAction.value = occurrenceId

  try {
    const result = await recordOccurrence(occurrenceId, createRequestId())
    notice.value = result.recorded
      ? '本期期次已补记到账本。'
      : result.error ?? '本期仍待处理，请检查账本余额和关联资料。'
    await load()
  } catch (error) {
    notice.value = getApiErrorMessage(error, '补记本期失败，请重试。')
  } finally {
    busyAction.value = ''
  }
}

async function runNow(): Promise<void> {
  busyAction.value = 'run'

  try {
    const result = await runPlans()
    notice.value = `执行完成：新建 ${result.createdOccurrences}，已记账 ${result.completed}，待处理 ${result.waiting}，失败 ${result.failed}。`
    await load()
  } catch (error) {
    notice.value = getApiErrorMessage(error, '立即执行失败，请重试。')
  } finally {
    busyAction.value = ''
  }
}

async function runPreview(): Promise<void> {
  isPreviewing.value = true
  previewError.value = ''

  try {
    const cash = trialCash.value.trim() === '' ? null : parseAmount(trialCash.value)

    if (cash === null && trialCash.value.trim() !== '') {
      previewError.value = '试算现金格式无效。'
      return
    }

    preview.value = await previewPlans(previewDate.value, cash)
  } catch (error) {
    preview.value = null
    previewError.value = getApiErrorMessage(error, '试算失败，请重试。')
  } finally {
    isPreviewing.value = false
  }
}

function typeLabel(type: PlanType): string {
  return typeOptions.find((option) => option.value === type)?.label ?? type
}

function scheduleText(plan: AutoPlan): string {
  const time = plan.time.slice(0, 5)

  if (plan.cycle === 'tradingDay') {
    return `三地交易日 ${time}（中国时间）`
  }

  if (plan.cycle === 'weekly') {
    return `每周${weekdayLabels[(plan.dayOfWeek ?? 1) - 1]} ${time}`
  }

  return `每月 ${plan.dayOfMonth} 日 ${time}`
}

function statusLabel(status: OccurrenceStatus): string {
  switch (status) {
    case 'completed':
      return '已记账'
    case 'waiting':
      return '待处理'
    case 'failed':
      return '执行失败'
    case 'skipped':
      return '已跳过'
    default:
      return '待执行'
  }
}

function statusClass(status: OccurrenceStatus): string {
  return `status-${status}`
}

function goBack(): void {
  void router.push({ name: 'home' })
}
</script>

<template>
  <main class="page plan-page with-nav">
    <header class="plan-header">
      <button type="button" class="back-link" @click="goBack">‹ 总览</button>
      <h1>自动记账</h1>
    </header>

    <p v-if="isLoading && !overview" class="state">正在读取自动计划…</p>

    <section v-else-if="errorMessage" class="state">
      <p>{{ errorMessage }}</p>
      <button type="button" class="secondary-button" @click="load">重新加载</button>
    </section>

    <template v-else-if="overview">
      <section class="plan-hero">
        <p class="hero-label">自动记账计划</p>
        <h2>{{ overview.activeCount }} 个启用中</h2>
        <p class="hero-meta">
          下一期 {{ overview.nextDueDate ? formatBusinessDate(overview.nextDueDate) : '暂无' }} ·
          可用现金 ¥{{ formatMoney(overview.cashBalance) }} · 公积金 ¥{{ formatMoney(overview.providentBalance) }}
        </p>
      </section>

      <section v-if="calendarCoverage.length" class="preview-card calendar-card" aria-label="三地交易日历">
        <strong>三地交易日历</strong>
        <p class="plan-meta">中国、美国、香港在同一日期均开市才执行，半日市照常。以下为创建新计划可选的近期交易日。</p>
        <div class="calendar-dates">
          <span v-for="day in nextTradingDates" :key="day.date">{{ formatBusinessDate(day.date) }}</span>
        </div>
        <div v-for="market in calendarCoverage" :key="`${market.market}-${market.year}`" class="calendar-source">
          <span>{{ market.name }} {{ market.year }} · {{ market.usesWeekdayFallback ? '工作日回退' : '官方日历' }}</span>
          <a v-if="market.sourceUrl" :href="market.sourceUrl" target="_blank" rel="noopener noreferrer">查看来源</a>
          <small v-if="market.publishedOn">发布 {{ market.publishedOn }}</small>
          <small v-else-if="market.verifiedOn">核验 {{ market.verifiedOn }}（来源未标发布日期）</small>
        </div>
        <p class="plan-meta">未维护年份按各市场周一至周五回退；日历补齐仅影响未来日期。基金临时暂停申购需自行核对。</p>
      </section>

      <div class="plan-actions">
        <button type="button" class="action-primary" @click="openNew('salaryIncome')">新建计划</button>
        <button type="button" class="action-secondary" :disabled="busyAction === 'run'" @click="runNow">
          {{ busyAction === 'run' ? '执行中…' : '立即执行' }}
        </button>
      </div>

      <p v-if="notice" class="notice" role="status">{{ notice }}</p>

      <div class="section-head">
        <h2>计划</h2>
        <span class="meta">{{ overview.plans.length }} 个</span>
      </div>

      <section v-if="overview.plans.length" class="plan-list">
        <article v-for="plan in overview.plans" :key="plan.id" class="plan-card">
          <header>
            <div>
              <strong>{{ plan.name }}</strong>
              <small>{{ typeLabel(plan.type) }} · {{ scheduleText(plan) }}</small>
            </div>
            <span class="plan-amount">¥{{ formatMoney(plan.amount) }}</span>
          </header>
          <p class="plan-meta">
            开始 {{ formatBusinessDate(plan.startDate) }} ·
            {{ plan.isActive ? `下一期 ${plan.nextDueDate ? formatBusinessDate(plan.nextDueDate) : '待生成'}` : '已暂停' }}
            <template v-if="plan.fundPositionName"> · {{ plan.fundPositionName }}</template>
          </p>
          <p v-if="plan.cycle === 'tradingDay' && plan.upcomingDates?.length" class="plan-meta">
            近期排期：{{ plan.upcomingDates.map(date => date.slice(5)).join(' · ') }}
          </p>
          <footer>
            <button type="button" class="small-button" @click="openEdit(plan)">编辑</button>
            <button
              type="button"
              class="small-button"
              :disabled="busyAction === plan.id"
              @click="togglePlan(plan)"
            >
              {{ plan.isActive ? '暂停' : '恢复' }}
            </button>
          </footer>
        </article>
      </section>

      <div v-else class="empty-state">
        <p>还没有自动记账计划</p>
        <button type="button" class="primary-button" @click="openNew('salaryIncome')">新建计划</button>
      </div>

      <template v-if="actionable.length">
        <div class="section-head">
          <h2>待处理期次</h2>
          <span class="meta">{{ actionable.length }} 笔</span>
        </div>
        <section class="occurrence-list">
          <article v-for="occurrence in actionable" :key="occurrence.id" class="occurrence-card">
            <div>
              <strong>{{ occurrence.planName }}</strong>
              <small>
                {{ occurrence.dueDate }} · ¥{{ formatMoney(occurrence.amount) }} ·
                <span :class="statusClass(occurrence.status)">{{ statusLabel(occurrence.status) }}</span>
              </small>
              <small v-if="occurrence.errorMessage" class="occurrence-error">{{ occurrence.errorMessage }}</small>
            </div>
            <div class="occurrence-actions">
              <button
                type="button"
                class="small-button"
                :disabled="busyAction === occurrence.id"
                @click="recordCurrentOccurrence(occurrence.id)"
              >
                补记本期
              </button>
              <button
                type="button"
                class="small-button"
                :disabled="busyAction === occurrence.id"
                @click="handleOccurrence(occurrence.id, 'retry')"
              >
                重新排队
              </button>
              <button
                type="button"
                class="small-button"
                :disabled="busyAction === occurrence.id"
                @click="handleOccurrence(occurrence.id, 'skip')"
              >
                跳过
              </button>
            </div>
          </article>
        </section>
      </template>

      <div class="section-head">
        <h2>执行预览</h2>
        <span class="meta">只试算所选日</span>
      </div>
      <section class="preview-card">
        <div class="form-row">
          <label class="form-field">
            <span>试算日期</span>
            <input v-model="previewDate" type="date" :min="today" />
          </label>
          <label class="form-field">
            <span>试算现金（选填）</span>
            <input v-model="trialCash" type="text" inputmode="decimal" placeholder="默认当前现金" />
          </label>
        </div>
        <button type="button" class="secondary-button" :disabled="isPreviewing" @click="runPreview">
          {{ isPreviewing ? '试算中…' : '试算本期' }}
        </button>
        <p v-if="previewError" class="form-error" role="alert">{{ previewError }}</p>
        <template v-if="preview">
          <p v-if="preview.calendarDay" class="plan-meta">
            {{ preview.calendarDay.isTradingDay ? '三地均开市' : '交易日定投跳过此日' }} ·
            {{ preview.calendarDay.markets.map(market => `${market.name}${market.usesWeekdayFallback ? '（工作日回退）' : ''}：${market.isOpen ? (market.isHalfDay ? '半日市' : '开市') : '休市'}`).join(' · ') }}
          </p>
          <p class="plan-meta">
            现金 {{ formatMoney(preview.trialCashStart) }} → {{ formatMoney(preview.trialCashEnd) }} ·
            公积金 {{ formatMoney(preview.providentStart) }} → {{ formatMoney(preview.providentEnd) }}
          </p>
          <div v-if="preview.rows.length" class="preview-rows">
            <div v-for="(row, index) in preview.rows" :key="`${row.planId ?? index}`" class="preview-row">
              <span>{{ row.planName }}</span>
              <span>{{ row.status }}</span>
              <span>现金 ¥{{ formatMoney(row.cashAfter) }}</span>
            </div>
          </div>
          <p v-else class="plan-meta">该日没有到期计划。</p>
          <p v-for="note in preview.notes" :key="note" class="preview-note">{{ note }}</p>
        </template>
      </section>

      <div class="section-head">
        <h2>执行记录</h2>
        <span class="meta">最近 100 条</span>
      </div>
      <section v-if="overview.recentOccurrences.length" class="occurrence-list">
        <article v-for="occurrence in overview.recentOccurrences" :key="occurrence.id" class="occurrence-card">
          <div>
            <strong>{{ occurrence.planName }}</strong>
            <small>
              {{ occurrence.dueDate }} {{ occurrence.dueTime.slice(0, 5) }} · ¥{{ formatMoney(occurrence.amount) }} ·
              <span :class="statusClass(occurrence.status)">{{ statusLabel(occurrence.status) }}</span>
            </small>
            <small v-if="occurrence.errorMessage" class="occurrence-error">{{ occurrence.errorMessage }}</small>
          </div>
        </article>
      </section>
      <p v-else class="summary-note">还没有执行记录。</p>

      <p class="summary-note">
        计划只生成账本记录，不验证银行或平台的实际资金操作；余额不足时整笔待处理，不部分扣款。
      </p>
    </template>

    <BottomSheet
      :open="sheetOpen"
      :title="sheetTitle"
      subtitle="保存只影响尚未发生的期次"
      :close-disabled="isSaving"
      @close="closeSheet"
    >
      <form class="form-stack" @submit.prevent="submit">
        <label class="form-field">
          <span>计划类型</span>
          <select v-model="form.type" :disabled="editingPlan !== null">
            <option v-for="option in typeOptions" :key="option.value" :value="option.value">{{ option.label }}</option>
          </select>
        </label>
        <label class="form-field">
          <span>名称（选填）</span>
          <input v-model="form.name" type="text" maxlength="50" placeholder="留空使用默认名称" />
        </label>
        <label class="form-field">
          <span>每期金额（元）</span>
          <input v-model="form.amount" type="text" inputmode="decimal" placeholder="0.00" />
        </label>

        <label v-if="form.type === 'fundSip'" class="form-field">
          <span>基金及份额类别</span>
          <select v-model="form.fundPositionId" :disabled="positionsLoading">
            <option value="">请选择</option>
            <option v-for="position in fundChoices" :key="position.id" :value="position.id">
              {{ position.name }} · {{ position.shareClass || '未标类别' }} · {{ position.code || '按名称匹配' }}
            </option>
          </select>
        </label>

        <p v-if="form.type === 'fundSip' && positionsError" class="form-error" role="alert">
          {{ positionsError }} <button type="button" class="small-button" @click="loadPositions">重新加载基金</button>
        </p>
        <label v-if="form.type === 'fundSip'" class="form-field">
          <span>周期</span>
          <select v-model="form.cycle">
            <option value="monthly">每月</option>
            <option value="weekly">每周</option>
            <option value="tradingDay">交易日（中国、美国、香港均开市）</option>
          </select>
        </label>

        <label v-if="form.type !== 'fundSip' || form.cycle === 'monthly'" class="form-field">
          <span>每月日期</span>
          <input v-model="form.dayOfMonth" type="number" min="1" max="31" />
          <small>当月没有该日期时按当月最后一天执行。</small>
        </label>

        <label v-else-if="form.cycle === 'weekly'" class="form-field">
          <span>星期</span>
          <select v-model="form.dayOfWeek">
            <option v-for="(label, index) in weekdayLabels" :key="label" :value="String(index + 1)">周{{ label }}</option>
          </select>
        </label>

        <p v-if="form.type === 'fundSip' && form.cycle === 'tradingDay'" class="form-note">
          新计划最早从创建后的下一个合格交易日开始，不补做创建当天；所选开始日期休市时顺延。
        </p>
        <div class="form-row">
          <label class="form-field">
            <span>执行时间（中国时区）</span>
            <input v-model="form.time" type="time" />
          </label>
          <label class="form-field">
            <span>开始日期</span>
            <input v-model="form.startDate" type="date" />
          </label>
        </div>

        <p class="form-note">
          保存不会立即扣款。基金定投到期时扣减账本现金并生成待确认买入，需自行核对实际份额和净值，不向销售平台下单。
        </p>
        <p v-if="formError" class="form-error" role="alert">{{ formError }}</p>
        <div class="form-actions">
          <button type="button" class="secondary-button" :disabled="isSaving" @click="closeSheet">取消</button>
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
.plan-page {
  padding-bottom: calc(97px + env(safe-area-inset-bottom));
}

.plan-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 10px;
  margin-bottom: 16px;
}

.plan-header h1 {
  margin: 0;
  font-size: 19px;
  font-weight: 600;
}

.back-link {
  min-height: 36px;
  padding: 0 4px;
  border: 0;
  background: transparent;
  color: var(--color-brand);
  font-size: 12px;
}

.plan-hero {
  padding: 19px 17px 17px;
  border-radius: 14px;
  background: var(--color-brand-soft);
}

.hero-label {
  margin: 0;
  color: var(--color-text-secondary);
  font-size: 11px;
}

.plan-hero h2 {
  margin: 8px 0 7px;
  font-size: 30px;
  font-weight: 600;
}

.hero-meta {
  margin: 0;
  color: var(--color-text-secondary);
  font-size: 10px;
  line-height: 1.6;
}

.calendar-card { margin-top: 14px; }
.calendar-dates { display: flex; flex-wrap: wrap; gap: 6px; }
.calendar-dates span { padding: 5px 8px; border-radius: 6px; background: var(--color-brand-soft); font-size: 11px; }
.calendar-source { display: flex; flex-wrap: wrap; gap: 6px 12px; font-size: 11px; color: var(--color-text-secondary); }
.calendar-source a { color: var(--color-brand); }
.calendar-source small { flex-basis: 100%; }

.plan-actions {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px;
  margin-top: 14px;
}

.plan-actions button {
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

.action-secondary {
  border: 1px solid var(--color-border-control);
  background: var(--color-bg-surface);
  color: var(--color-brand);
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
}

.meta {
  color: var(--color-text-secondary);
  font-size: 10px;
}

.plan-list,
.occurrence-list {
  display: flex;
  flex-direction: column;
  gap: 9px;
}

.plan-card,
.occurrence-card,
.preview-card {
  padding: 14px;
  border: 1px solid var(--color-border-subtle);
  border-radius: 12px;
  background: var(--color-bg-surface);
}

.plan-card header {
  display: flex;
  justify-content: space-between;
  gap: 12px;
}

.plan-card header strong {
  display: block;
  font-size: 13px;
  font-weight: 600;
}

.plan-card header small {
  display: block;
  margin-top: 4px;
  color: var(--color-text-secondary);
  font-size: 10px;
}

.plan-amount {
  font-size: 16px;
  font-weight: 550;
  font-variant-numeric: tabular-nums;
}

.plan-meta {
  margin: 10px 0 0;
  color: var(--color-text-secondary);
  font-size: 10px;
  line-height: 1.6;
}

.plan-card footer {
  display: flex;
  gap: 8px;
  margin-top: 11px;
}

.small-button {
  min-height: 30px;
  padding: 0 11px;
  border: 1px solid var(--color-border-control);
  border-radius: 7px;
  background: var(--color-bg-surface);
  color: var(--color-brand);
  font-size: 10px;
}

.occurrence-card {
  display: flex;
  justify-content: space-between;
  gap: 10px;
  align-items: flex-start;
}

.occurrence-card strong {
  display: block;
  font-size: 12px;
  font-weight: 600;
}

.occurrence-card small {
  display: block;
  margin-top: 4px;
  color: var(--color-text-secondary);
  font-size: 10px;
  line-height: 1.5;
}

.occurrence-error {
  color: var(--color-finance-loss) !important;
}

.occurrence-actions {
  display: flex;
  flex: 0 0 auto;
  gap: 6px;
}

.status-completed {
  color: var(--color-success);
}

.status-waiting,
.status-failed {
  color: var(--color-finance-loss);
}

.status-skipped {
  color: var(--color-text-secondary);
}

.preview-card {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.preview-rows {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding-top: 8px;
  border-top: 1px solid var(--color-border-subtle);
}

.preview-row {
  display: grid;
  grid-template-columns: 1.4fr 0.8fr 1fr;
  gap: 8px;
  font-size: 10px;
  line-height: 1.5;
}

.preview-note {
  margin: 0;
  color: var(--color-text-secondary);
  font-size: 9px;
}

.summary-note {
  margin: 14px 0 0;
  color: var(--color-text-secondary);
  font-size: 10px;
  line-height: 1.7;
  text-align: center;
}

.empty-state {
  padding: 30px 16px;
  border: 1px dashed var(--color-border-default);
  border-radius: 12px;
  text-align: center;
}

.empty-state p {
  margin: 0 0 12px;
  color: var(--color-text-secondary);
  font-size: 12px;
}

.empty-state .primary-button {
  width: 100%;
}
</style>
