<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  fetchPlanCalendar,
  fetchPlanOverview,
  pausePlan,
  recordOccurrence,
  resumePlan,
  retryOccurrence,
  runPlans,
  savePlan,
  skipOccurrence
} from '../api/plans'
import { fetchFundOverview } from '../api/funds'
import { ApiError, getApiErrorMessage } from '../api/client'
import AppIcon from '../components/AppIcon.vue'
import AppNav from '../components/AppNav.vue'
import BackButton from '../components/BackButton.vue'
import BottomSheet from '../components/BottomSheet.vue'
import PlanCalendar from '../components/PlanCalendar.vue'
import { createRequestId } from '../utils/request'
import {
  formatBusinessDate,
  formatMoney,
  parseAmount,
  sumAmounts,
  todayInBusinessTimeZone
} from '../utils/format'
import type { FundPosition } from '../types/fund'
import type {
  AutoPlan,
  OccurrenceStatus,
  PlanCycle,
  PlanOverview,
  PlanType,
  SavePlanInput,
  TradingDay
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
const calendarYear = ref(Number(today.slice(0, 4)))
const calendarMonth = ref(Number(today.slice(5, 7)))
const calendarDays = ref<TradingDay[]>([])
const calendarLoading = ref(false)
const calendarError = ref('')
const calendarCache = new Map<string, TradingDay[]>()
let calendarSequence = 0

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

const typeOptions: { value: PlanType; label: string }[] = [
  { value: 'salaryIncome', label: '工资到账' },
  { value: 'providentDeposit', label: '公积金缴存' },
  { value: 'providentWithdrawal', label: '公积金提取' },
  { value: 'fundSip', label: '基金定投' }
]

interface PlanGroup {
  key: 'fund' | 'provident' | 'income'
  title: string
  icon: string
  newType: PlanType
  plans: AutoPlan[]
  summary: string
}

interface FundPlanGroup {
  key: string
  name: string
  plans: AutoPlan[]
  single: AutoPlan | null
  shareClasses: string[]
  activeCount: number
  activeAmount: number
  nextDueDate: string | null
}

const planGroups = computed<PlanGroup[]>(() => {
  const plans = overview.value?.plans ?? []
  const fundPlans = plans.filter(plan => plan.type === 'fundSip')
  const providentPlans = plans.filter(plan => plan.type === 'providentDeposit' || plan.type === 'providentWithdrawal')
  const incomePlans = plans.filter(plan => plan.type === 'salaryIncome')

  return [
    {
      key: 'fund',
      title: '基金定投',
      icon: 'chart',
      newType: 'fundSip',
      plans: fundPlans,
      summary: groupSummary(fundPlans, '每期合计')
    },
    {
      key: 'provident',
      title: providentPlans.some(plan => plan.type === 'providentWithdrawal') ? '公积金' : '公积金缴存',
      icon: 'home',
      newType: 'providentDeposit',
      plans: providentPlans,
      summary: providentSummary(providentPlans)
    },
    {
      key: 'income',
      title: '定期收入',
      icon: 'wallet',
      newType: 'salaryIncome',
      plans: incomePlans,
      summary: groupSummary(incomePlans, '每期收入')
    }
  ]
})

const fundPlanGroups = computed<FundPlanGroup[]>(() => {
  const groups = new Map<string, FundPlanGroup>()
  const fundPlans = planGroups.value.find(group => group.key === 'fund')?.plans ?? []

  for (const plan of fundPlans) {
    const key = fundGroupKey(plan)
    const existing = groups.get(key)

    if (existing) {
      existing.plans.push(plan)
    } else {
      groups.set(key, {
        key,
        name: fundDisplayName(plan),
        plans: [plan],
        single: null,
        shareClasses: [],
        activeCount: 0,
        activeAmount: 0,
        nextDueDate: null
      })
    }
  }

  return [...groups.values()]
    .map((group) => {
      const activePlans = group.plans.filter(plan => plan.isActive)
      const sortedPlans = [...group.plans]
        .sort((first, second) => shareClassLabel(first).localeCompare(shareClassLabel(second)))
      const nextDates = activePlans
        .map(plan => plan.nextDueDate)
        .filter((date): date is string => date !== null)
        .sort()

      return {
        ...group,
        plans: sortedPlans,
        single: sortedPlans.length === 1 ? sortedPlans[0]! : null,
        shareClasses: [...new Set(sortedPlans.map(shareClassLabel))],
        activeCount: activePlans.length,
        activeAmount: sumAmounts(activePlans.map(plan => plan.amount)),
        nextDueDate: nextDates[0] ?? null
      }
    })
    .sort((first, second) =>
      (first.nextDueDate ?? '9999-12-31').localeCompare(second.nextDueDate ?? '9999-12-31')
      || first.name.localeCompare(second.name))
})

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
  void loadCalendar()
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

async function loadCalendar(year = calendarYear.value, month = calendarMonth.value): Promise<void> {
  const key = `${year}-${month}`
  const cached = calendarCache.get(key)

  calendarYear.value = year
  calendarMonth.value = month
  calendarError.value = ''

  if (cached) {
    calendarSequence++
    calendarDays.value = cached
    calendarLoading.value = false
    return
  }

  const sequence = ++calendarSequence
  calendarDays.value = []
  calendarLoading.value = true

  try {
    const result = await fetchPlanCalendar(year, month)

    if (sequence !== calendarSequence) {
      return
    }

    calendarCache.set(key, result.days)
    calendarDays.value = result.days
  } catch (error) {
    if (sequence !== calendarSequence) {
      return
    }

    calendarDays.value = []
    calendarError.value = getApiErrorMessage(error, '交易日历加载失败，请重试。')
  } finally {
    if (sequence === calendarSequence) {
      calendarLoading.value = false
    }
  }
}

function shiftCalendar(offset: number): void {
  const next = new Date(Date.UTC(calendarYear.value, calendarMonth.value - 1 + offset, 1))
  void loadCalendar(next.getUTCFullYear(), next.getUTCMonth() + 1)
}

function groupSummary(plans: AutoPlan[], amountLabel: string): string {
  if (!plans.length) {
    return '暂无计划'
  }

  const activePlans = plans.filter(plan => plan.isActive)

  if (!activePlans.length) {
    return `已全部暂停 · ${plans.length} 个计划`
  }

  const paused = plans.length - activePlans.length
  const amount = sumAmounts(activePlans.map(plan => plan.amount))

  return `${activePlans.length} 个启用 · ${amountLabel} ¥${formatMoney(amount)}${paused ? ` · ${paused} 个暂停` : ''}`
}

function providentSummary(plans: AutoPlan[]): string {
  if (!plans.length) {
    return '缴存与提取'
  }

  const activePlans = plans.filter(plan => plan.isActive)
  const deposits = sumAmounts(activePlans.filter(plan => plan.type === 'providentDeposit').map(plan => plan.amount))
  const withdrawals = sumAmounts(activePlans.filter(plan => plan.type === 'providentWithdrawal').map(plan => plan.amount))
  const parts: string[] = []

  if (deposits > 0) {
    parts.push(`缴存 ¥${formatMoney(deposits)}`)
  }

  if (withdrawals > 0) {
    parts.push(`提取 ¥${formatMoney(withdrawals)}`)
  }

  const paused = plans.length - activePlans.length

  if (!parts.length) {
    return paused ? `已全部暂停 · ${paused} 个计划` : '缴存与提取'
  }

  return `${parts.join(' · ')}${paused ? ` · ${paused} 个暂停` : ''}`
}

function fundGroupKey(plan: AutoPlan): string {
  return fundDisplayName(plan).normalize('NFKC').replace(/\s/g, '').toUpperCase()
}

function fundDisplayName(plan: AutoPlan): string {
  const name = plan.fundTarget?.name ?? plan.fundPositionName ?? plan.name
  return name.replace(/\s*[·•]\s*(交易日定投|定投)$/, '').trim()
}

function shareClassLabel(plan: AutoPlan): string {
  const shareClass = plan.fundTarget?.shareClass?.trim()

  if (shareClass) {
    return /^[A-Za-z]$/.test(shareClass) ? `${shareClass.toUpperCase()} 类` : shareClass
  }

  const match = (plan.fundPositionName ?? plan.name).match(/\s([A-Za-z])(?:\s*类)?(?:\s*[·]|$)/)
  return match ? `${match[1]!.toUpperCase()} 类` : '未标类别'
}

function nextDueText(plan: AutoPlan): string {
  if (!plan.isActive) {
    return '已暂停'
  }

  return plan.nextDueDate ? `下一期 ${shortDate(plan.nextDueDate)}` : '下一期待生成'
}

function shortDate(value: string): string {
  return value.startsWith(`${today.slice(0, 4)}-`)
    ? `${Number(value.slice(5, 7))}月${Number(value.slice(8, 10))}日`
    : formatBusinessDate(value)
}

function typeLabel(type: PlanType): string {
  return typeOptions.find((option) => option.value === type)?.label ?? type
}

function scheduleText(plan: AutoPlan): string {
  const time = plan.time.slice(0, 5)

  if (plan.cycle === 'tradingDay') {
    return `交易日 ${time}`
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
      <BackButton @back="goBack" />
      <h1>自动记账</h1>
      <span class="header-spacer" />
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

      <div class="plan-actions">
        <button type="button" class="action-primary" @click="openNew('salaryIncome')">新建计划</button>
        <button type="button" class="action-secondary" :disabled="busyAction === 'run'" @click="runNow">
          {{ busyAction === 'run' ? '执行中…' : '立即执行' }}
        </button>
      </div>

      <p v-if="notice" class="notice" role="status">{{ notice }}</p>

      <PlanCalendar
        :year="calendarYear"
        :month="calendarMonth"
        :days="calendarDays"
        :loading="calendarLoading"
        :error="calendarError"
        @prev="shiftCalendar(-1)"
        @next="shiftCalendar(1)"
        @retry="loadCalendar()"
      />

      <div class="section-head">
        <h2>计划</h2>
        <span class="meta">{{ overview.plans.length }} 个 · {{ overview.activeCount }} 个启用</span>
      </div>

      <section
        v-for="group in planGroups"
        :key="group.key"
        class="plan-group"
        :class="`group-${group.key}`"
      >
        <header class="plan-group-head">
          <span class="group-icon"><AppIcon :name="group.icon" /></span>
          <div class="group-heading">
            <h3>{{ group.title }}</h3>
            <p>{{ group.summary }}</p>
          </div>
          <button
            v-if="group.key === 'fund'"
            type="button"
            class="group-add"
            aria-label="新增基金定投"
            @click="openNew('fundSip')"
          >＋ 新增</button>
        </header>

        <template v-if="group.key === 'fund'">
          <div v-if="fundPlanGroups.length" class="fund-plan-list">
            <article
              v-for="fund in fundPlanGroups"
              :key="fund.key"
              class="plan-card fund-plan-card"
              :class="{ 'is-paused': fund.activeCount === 0 }"
            >
              <details v-if="fund.plans.length > 1" class="fund-details">
                <summary>
                  <span class="fund-summary-copy">
                    <strong>{{ fund.name }}</strong>
                    <small>
                      {{ fund.shareClasses.join(' / ') }} · {{ fund.plans.length }} 个计划
                      <template v-if="fund.plans.length - fund.activeCount"> · {{ fund.plans.length - fund.activeCount }} 个暂停</template>
                    </small>
                  </span>
                  <span class="fund-summary-amount">
                    <template v-if="fund.activeCount">
                      <b>¥{{ formatMoney(fund.activeAmount) }}</b>
                      <small>每期</small>
                    </template>
                    <small v-else class="paused-label">已暂停</small>
                  </span>
                </summary>
                <div class="fund-plan-rows">
                  <div
                    v-for="plan in fund.plans"
                    :key="plan.id"
                    class="fund-plan-row"
                    :class="{ paused: !plan.isActive }"
                  >
                    <span class="share-badge">{{ shareClassLabel(plan) }}</span>
                    <span class="fund-row-copy">
                      <b>¥{{ formatMoney(plan.amount) }}</b>
                      <small>{{ scheduleText(plan) }} · {{ nextDueText(plan) }}</small>
                      <small v-if="plan.fundTarget?.code" class="fund-code">{{ plan.fundTarget.code }}</small>
                    </span>
                    <span v-if="!plan.isActive" class="paused-tag">已暂停</span>
                    <span class="fund-row-actions">
                      <button type="button" class="small-button" @click="openEdit(plan)">编辑</button>
                      <button
                        type="button"
                        class="small-button"
                        :disabled="busyAction === plan.id"
                        @click="togglePlan(plan)"
                      >{{ plan.isActive ? '暂停' : '恢复' }}</button>
                    </span>
                  </div>
                </div>
              </details>

              <template v-else-if="fund.single">
                <header>
                  <div>
                    <strong>{{ fund.name }}</strong>
                    <small>{{ shareClassLabel(fund.single) }} · {{ scheduleText(fund.single) }}</small>
                  </div>
                  <span class="plan-amount">¥{{ formatMoney(fund.single.amount) }}</span>
                </header>
                <p class="plan-meta">
                  开始 {{ formatBusinessDate(fund.single.startDate) }} · {{ nextDueText(fund.single) }}
                  <template v-if="fund.single.fundTarget?.code"> · {{ fund.single.fundTarget.code }}</template>
                </p>
                <footer>
                  <button type="button" class="small-button" @click="openEdit(fund.single)">编辑</button>
                  <button
                    type="button"
                    class="small-button"
                    :disabled="busyAction === fund.single.id"
                    @click="togglePlan(fund.single)"
                  >{{ fund.single.isActive ? '暂停' : '恢复' }}</button>
                </footer>
              </template>
            </article>
          </div>
          <p v-else class="group-empty">
            还没有基金定投计划。
            <button type="button" class="text-button" @click="openNew('fundSip')">新建一个</button>
          </p>
        </template>

        <template v-else>
          <article v-for="plan in group.plans" :key="plan.id" class="plan-card">
            <header>
              <div>
                <strong>{{ plan.name }}</strong>
                <small>{{ typeLabel(plan.type) }} · {{ scheduleText(plan) }}</small>
              </div>
              <span class="plan-amount">¥{{ formatMoney(plan.amount) }}</span>
            </header>
            <p class="plan-meta">
              开始 {{ formatBusinessDate(plan.startDate) }} · {{ nextDueText(plan) }}
            </p>
            <p v-if="plan.type === 'providentWithdrawal'" class="plan-meta">提取后等额转入现金，不计入生活收入。</p>
            <footer>
              <button type="button" class="small-button" @click="openEdit(plan)">编辑</button>
              <button
                type="button"
                class="small-button"
                :disabled="busyAction === plan.id"
                @click="togglePlan(plan)"
              >{{ plan.isActive ? '暂停' : '恢复' }}</button>
            </footer>
          </article>
          <p v-if="!group.plans.length" class="group-empty">
            还没有{{ group.title }}计划。
            <button type="button" class="text-button" @click="openNew(group.newType)">新建一个</button>
          </p>
        </template>
      </section>

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

.header-spacer {
  width: 36px;
  flex-shrink: 0;
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

.plan-group {
  --group-accent: var(--color-brand);
}

.group-fund {
  --group-accent: var(--color-chart-investment);
}

.group-provident {
  --group-accent: var(--color-chart-property);
}

.group-income {
  --group-accent: var(--color-finance-income);
}

.plan-group + .plan-group {
  margin-top: 18px;
}

.plan-group-head {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 9px;
}

.group-icon {
  display: grid;
  place-items: center;
  flex: 0 0 auto;
  width: 30px;
  height: 30px;
  border-radius: 9px;
  background: color-mix(in srgb, var(--group-accent) 12%, var(--color-bg-surface));
  color: var(--group-accent);
}

.group-icon .app-icon {
  width: 17px;
  height: 17px;
}

.group-heading {
  flex: 1;
  min-width: 0;
}

.group-heading h3 {
  margin: 0;
  font-size: 13px;
  font-weight: 650;
  letter-spacing: -0.1px;
}

.group-heading p {
  margin: 3px 0 0;
  color: var(--color-text-secondary);
  font-size: 9px;
  line-height: 1.5;
}

.group-add {
  flex: 0 0 auto;
  padding: 4px 7px;
  border: 0;
  border-radius: 6px;
  background: transparent;
  color: var(--group-accent);
  font-size: 10px;
  font-weight: 600;
}

.group-add:hover {
  background: color-mix(in srgb, var(--group-accent) 10%, var(--color-bg-surface));
}

.fund-plan-list {
  display: flex;
  flex-direction: column;
  gap: 9px;
}

.group-empty {
  margin: 0;
  padding: 15px 12px;
  border: 1px dashed var(--color-border-subtle);
  border-radius: 10px;
  color: var(--color-text-secondary);
  font-size: 10px;
  text-align: center;
}

.text-button {
  padding: 0;
  border: 0;
  background: transparent;
  color: var(--color-brand);
  font-size: 10px;
}

.plan-card,
.occurrence-card {
  padding: 14px;
  border: 1px solid var(--color-border-subtle);
  border-radius: 12px;
  background: var(--color-bg-surface);
}

.plan-group .plan-card {
  border-left: 3px solid var(--group-accent);
}

.plan-card.is-paused {
  border-left-color: var(--color-text-disabled);
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

.fund-details > summary {
  display: flex;
  align-items: center;
  gap: 10px;
  list-style: none;
  cursor: pointer;
}

.fund-details > summary::-webkit-details-marker {
  display: none;
}

.fund-details > summary::after {
  flex: 0 0 auto;
  color: var(--color-text-secondary);
  font-size: 14px;
  content: '⌄';
  transition: transform .15s ease;
}

.fund-details[open] > summary::after {
  transform: rotate(180deg);
}

.fund-summary-copy {
  display: flex;
  flex: 1;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}

.fund-summary-copy strong {
  overflow: hidden;
  font-size: 13px;
  font-weight: 600;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.fund-summary-copy small {
  color: var(--color-text-secondary);
  font-size: 10px;
}

.fund-summary-amount {
  display: flex;
  flex: 0 0 auto;
  flex-direction: column;
  align-items: flex-end;
  gap: 3px;
}

.fund-summary-amount b {
  font-size: 16px;
  font-weight: 550;
  font-variant-numeric: tabular-nums;
}

.fund-summary-amount small {
  color: var(--color-text-secondary);
  font-size: 9px;
}

.paused-label {
  color: var(--color-text-secondary);
  font-size: 10px;
}

.fund-plan-rows {
  margin-top: 11px;
  border-top: 1px solid var(--color-border-subtle);
}

.fund-plan-row {
  display: grid;
  grid-template-columns: auto minmax(0, 1fr) auto;
  align-items: start;
  gap: 8px;
  padding: 10px 0;
  border-bottom: 1px solid var(--color-border-subtle);
}

.fund-plan-row:last-child {
  padding-bottom: 0;
  border-bottom: 0;
}

.fund-plan-row.paused {
  opacity: .58;
}

.share-badge {
  display: inline-flex;
  justify-content: center;
  align-items: center;
  box-sizing: border-box;
  min-width: 34px;
  min-height: 22px;
  padding: 0 6px;
  border-radius: 5px;
  background: var(--color-brand-soft);
  color: var(--color-brand);
  font-size: 9px;
  font-weight: 600;
  white-space: nowrap;
}

.group-fund .share-badge {
  background: color-mix(in srgb, var(--color-chart-investment) 12%, var(--color-bg-surface));
  color: var(--color-chart-investment);
}

.fund-row-copy {
  display: flex;
  flex-direction: column;
  gap: 3px;
  min-width: 0;
}

.fund-row-copy b {
  font-size: 12px;
  font-weight: 550;
  font-variant-numeric: tabular-nums;
}

.fund-row-copy small {
  color: var(--color-text-secondary);
  font-size: 9px;
  line-height: 1.5;
}

.fund-row-copy .fund-code {
  color: var(--color-text-disabled);
}

.fund-row-actions {
  display: flex;
  flex: 0 0 auto;
  gap: 5px;
}

.fund-row-actions .small-button {
  min-height: 28px;
  padding: 0 8px;
  font-size: 9px;
}

.occurrence-list {
  display: flex;
  flex-direction: column;
  gap: 9px;
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

.summary-note {
  margin: 14px 0 0;
  color: var(--color-text-secondary);
  font-size: 10px;
  line-height: 1.7;
  text-align: center;
}
</style>
