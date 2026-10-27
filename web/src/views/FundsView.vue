<script setup lang="ts">
import Decimal from 'decimal.js'
import CashAdjustmentLink from '../components/CashAdjustmentLink.vue'
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import {
  arriveFundSell,
  assignFundGroup,
  buyFund,
  cancelFundPending,
  confirmFundSell,
  correctFundShares,
  createFundGroup,
  createFundPosition,
  deleteFundGroup,
  fetchFundOverview,
  refreshFundQuote,
  renameFundGroup,
  sellFund
} from '../api/funds'
import { fetchPlanOverview } from '../api/plans'
import { ApiError, getApiErrorMessage } from '../api/client'
import AppNav from '../components/AppNav.vue'
import AppIcon from '../components/AppIcon.vue'
import EmptyState from '../components/EmptyState.vue'
import BackButton from '../components/BackButton.vue'
import BottomSheet from '../components/BottomSheet.vue'
import FundReturnsPanel from '../components/FundReturnsPanel.vue'
import {
  formatBusinessDate,
  addAmounts,
  formatMoney,
  formatQuantity,
  formatSignedMoney,
  parseAmount,
  parseNonNegativeQuantity,
  parsePrice,
  parseQuantity,
  sumAmounts,
  todayInBusinessTimeZone
} from '../utils/format'
import { createRequestId } from '../utils/request'
import type {
  FundActivity,
  FundOverview,
  FundPosition,
  FundShareClass,
  FundSummary,
  PendingFundArrival,
  PendingFundBuy,
  PendingFundSell
} from '../types/fund'
import type { AutoPlan } from '../types/plan'

type SheetKind = 'opening' | 'buy' | 'shares' | 'sell' | 'sellConfirm' | 'sellArrival'
type PortfolioView = 'positions' | 'returns' | 'activities' | 'plans'
type GroupScope = 'all' | 'ungrouped' | string
type FundMetricKey = 'value' | 'profit' | 'rate' | 'cost' | 'weight' | 'pending'
type FundSortKey = 'value' | 'profit' | 'rate' | 'name'

const metricStorageKey = 'asset-keeper.fund.metrics'
const defaultMetrics: FundMetricKey[] = ['value', 'profit', 'rate']
const metricOptions: { key: FundMetricKey; label: string }[] = [
  { key: 'value', label: '持仓价值' },
  { key: 'profit', label: '持有收益' },
  { key: 'rate', label: '收益率' },
  { key: 'cost', label: '持仓成本' },
  { key: 'weight', label: '占比' },
  { key: 'pending', label: '待确认' }
]
const sortOptions: { key: FundSortKey; label: string }[] = [
  { key: 'value', label: '持仓价值' },
  { key: 'profit', label: '持有收益' },
  { key: 'rate', label: '收益率' },
  { key: 'name', label: '基金名称' }
]

const router = useRouter()
const today = todayInBusinessTimeZone()
const overview = ref<FundOverview | null>(null)
const isLoading = ref(false)
const errorMessage = ref('')
const notice = ref('')
const sheet = ref<SheetKind | null>(null)
const requestId = ref(createRequestId())
const isSaving = ref(false)
const formError = ref('')
const confirmingSellId = ref<string | null>(null)
const arrivingConfirmId = ref<string | null>(null)
const refreshingQuote = ref(false)
const activeGroup = ref<GroupScope>('all')
const activeView = ref<PortfolioView>('positions')
const expandedFund = ref<string | null>(null)
const searchText = ref('')
const sortKey = ref<FundSortKey>('value')
const metricKeys = ref<FundMetricKey[]>(loadMetricKeys())
const metricNotice = ref('')
const groupSheetOpen = ref(false)
const groupDraft = ref('')
const groupError = ref('')
const groupBusy = ref(false)
const editingGroupId = ref<string | null>(null)
const editingGroupName = ref('')
const plans = ref<AutoPlan[]>([])
const plansLoaded = ref(false)
const plansLoading = ref(false)
const plansError = ref('')
const form = reactive({
  positionId: '',
  name: '',
  code: '',
  shareClass: '',
  platform: '',
  shares: '',
  cost: '',
  amount: '',
  netAmount: '',
  netValue: '',
  netValueDate: today,
  fee: '0',
  confirmationDelayDays: '2',
  businessDate: today,
  note: '',
  relatedCashAdjustmentId: null as string | null
})

const selectedPosition = computed<FundPosition | null>(() =>
  overview.value?.positions.find((position) => position.id === form.positionId) ?? null)
const selectedSellApplication = computed<PendingFundSell | null>(() =>
  overview.value?.sellApplications.find((pending) => pending.transactionId === confirmingSellId.value) ?? null)
const selectedPendingArrival = computed<PendingFundArrival | null>(() =>
  overview.value?.pendingArrivals.find((pending) => pending.transactionId === arrivingConfirmId.value) ?? null)

const allFunds = computed<FundSummary[]>(() => {
  const loaded = overview.value

  if (!loaded) {
    return []
  }

  if (loaded.funds?.length) {
    return loaded.funds
  }

  return buildFallbackFunds(loaded.positions, loaded.pendingBuys, loaded.pendingArrivals)
})

const groups = computed(() => overview.value?.groups ?? [])

const groupChips = computed(() => {
  const all = allFunds.value
  const ungroupedFunds = all.filter((fund) => !fund.groupId)
  const chips = [{
    key: 'all' as GroupScope,
    label: '全部',
    count: all.length,
    value: sumAmounts(all.map(fund => fund.assetValue))
  }, ...groups.value.map((group) => ({
    key: group.id as GroupScope,
    label: group.name,
    count: all.filter((fund) => fund.groupId === group.id).length,
    value: sumAmounts(all.filter((fund) => fund.groupId === group.id).map(fund => fund.assetValue))
  }))]

  if (ungroupedFunds.length) {
    chips.push({
      key: 'ungrouped' as GroupScope,
      label: '未分组',
      count: ungroupedFunds.length,
      value: sumAmounts(ungroupedFunds.map(fund => fund.assetValue))
    })
  }

  return chips
})

const scopedFunds = computed(() => {
  const search = searchText.value.trim().toLowerCase()

  return allFunds.value.filter((fund) => {
    if (activeGroup.value === 'ungrouped' && fund.groupId) {
      return false
    }

    if (activeGroup.value !== 'all' && activeGroup.value !== 'ungrouped' && fund.groupId !== activeGroup.value) {
      return false
    }

    if (!search) {
      return true
    }

    return fund.name.toLowerCase().includes(search)
      || fund.classes.some((fundClass) => fundClass.platforms.some((platform) =>
        (platform.code ?? '').toLowerCase().includes(search)))
  })
})

const hasScopeFilter = computed(() => activeGroup.value !== 'all' || searchText.value.trim() !== '')
const returnsGroupId = computed(() =>
  activeGroup.value !== 'all' && activeGroup.value !== 'ungrouped' ? activeGroup.value : null)
const returnsUngrouped = computed(() => activeGroup.value === 'ungrouped')

const scopedPositionIds = computed(() => new Set(
  scopedFunds.value.flatMap((fund) => fund.classes.flatMap((fundClass) =>
    fundClass.platforms.map((platform) => platform.positionId)))
))

const scopedPendingBuys = computed(() => {
  if (!hasScopeFilter.value) {
    return overview.value?.pendingBuys ?? []
  }

  return (overview.value?.pendingBuys ?? []).filter((item) => scopedPositionIds.value.has(item.positionId))
})

const scopedSellApplications = computed(() => {
  if (!hasScopeFilter.value) {
    return overview.value?.sellApplications ?? []
  }

  return (overview.value?.sellApplications ?? []).filter((item) => scopedPositionIds.value.has(item.positionId))
})

const scopedPendingArrivals = computed(() => {
  if (!hasScopeFilter.value) {
    return overview.value?.pendingArrivals ?? []
  }

  return (overview.value?.pendingArrivals ?? []).filter((item) => scopedPositionIds.value.has(item.positionId))
})

const scopedActivities = computed(() => {
  const trades = (overview.value?.activities ?? []).filter((item) => item.type !== 'quote')

  if (!hasScopeFilter.value) {
    return trades
  }

  return trades.filter((item) => scopedPositionIds.value.has(item.positionId))
})

const scope = computed(() => {
  const confirmedValue = sumAmounts(scopedFunds.value.map(fund => fund.value))
  const cost = sumAmounts(scopedFunds.value.map(fund => fund.cost))
  const holdingProfit = sumAmounts(scopedFunds.value.map(fund => fund.holdingProfit))
  const realizedProfit = sumAmounts(scopedFunds.value.map(fund => fund.realizedProfit))
  const pendingBuyAmount = sumAmounts(scopedPendingBuys.value.map(item => item.amount))
  const pendingArrivalAmount = sumAmounts(scopedPendingArrivals.value.map(item => item.amount))

  return {
    confirmedValue,
    cost,
    holdingProfit,
    realizedProfit,
    pendingBuyAmount,
    pendingArrivalAmount,
    assetValue: addAmounts(confirmedValue, pendingBuyAmount, pendingArrivalAmount),
    rate: cost > 0 ? holdingProfit / cost * 100 : null,
    hasEffectiveQuotes: scopedFunds.value.every((fund) => fund.hasEffectiveQuotes),
    historyConfirmed: scopedFunds.value.every((fund) => fund.historyConfirmed)
  }
})

const sortedFunds = computed(() => {
  const funds = [...scopedFunds.value]

  return funds.sort((left, right) => {
    switch (sortKey.value) {
      case 'name':
        return left.name.localeCompare(right.name, 'zh-Hans-CN')
      case 'profit':
        return new Decimal(right.holdingProfit).comparedTo(left.holdingProfit) || left.name.localeCompare(right.name, 'zh-Hans-CN')
      case 'rate':
        return rateOf(right) - rateOf(left) || left.name.localeCompare(right.name, 'zh-Hans-CN')
      default:
        return new Decimal(right.assetValue).comparedTo(left.assetValue) || left.name.localeCompare(right.name, 'zh-Hans-CN')
    }
  })
})

const scopedPlans = computed(() => {
  if (!hasScopeFilter.value) {
    return plans.value
  }

  return plans.value.filter((plan) => plan.fundPositionId !== null && scopedPositionIds.value.has(plan.fundPositionId))
})

const activeGroupLabel = computed(() => groupChips.value.find((chip) => chip.key === activeGroup.value)?.label ?? '全部')
const sheetTitle = computed(() => {
  switch (sheet.value) {
    case 'opening':
      return '录入基金持仓'
    case 'buy':
      return '记录基金买入'
    case 'shares':
      return '调整基金份额'
    case 'sell':
      return '申请赎回基金'
    case 'sellConfirm':
      return '确认基金赎回'
    case 'sellArrival':
      return '登记赎回到账'
    default:
      return ''
  }
})
const sheetSubtitle = computed(() => {
  switch (sheet.value) {
    case 'opening':
      return '用于已有持仓，不扣现金'
    case 'buy':
      return '现金减少，买入待确认增加'
    case 'shares':
      return '生成份额核对记录，保留原有流水'
    case 'sell':
      return '份额冻结，仍在持仓中估值'
    case 'sellConfirm':
      return '份额减少，净回款转为待到账'
    case 'sellArrival':
      return '待到账转入现金，不重复计算收益'
    default:
      return ''
  }
})
const sheetNote = computed(() => {
  switch (sheet.value) {
    case 'opening':
      return '录入已有持仓不会从现金扣除购买成本，也不产生生活收支。'
    case 'buy':
      return '15:00 前可撤销；截止后按基金 T 日净值和实际申购费自动计算份额。'
    case 'shares':
      return '只修改持仓份额，净值必须由行情数据源获取。'
    case 'sell':
      return '申请后份额仍计入持仓估值，但会冻结，不能重复申请超过可售份额。'
    case 'sellConfirm':
      return '按确认份额比例结转成本；净回款转为待到账，与成本差额计入已实现收益。'
    case 'sellArrival':
      return '到账金额必须与待到账金额一致，不再次确认收益；如有差额，请先更正赎回确认。'
    default:
      return ''
  }
})
const canSubmit = computed(() => {
  if (isSaving.value) {
    return false
  }

  switch (sheet.value) {
    case 'opening':
      return form.name.trim() !== ''
        && form.code.trim() !== ''
        && parseNonNegativeQuantity(form.shares) !== null
        && parseAmount(form.cost) !== null
    case 'buy':
      return form.positionId !== '' && !!selectedPosition.value?.code && (parseAmount(form.amount) ?? 0) > 0
        && (parseAmount(form.fee) ?? -1) >= 0
        && (parseAmount(form.fee) ?? Number.POSITIVE_INFINITY) < (parseAmount(form.amount) ?? 0)
    case 'shares':
      return form.positionId !== '' && parseNonNegativeQuantity(form.shares) !== null
    case 'sell':
      return form.positionId !== '' && parseQuantity(form.shares) !== null
    case 'sellConfirm':
      return confirmingSellId.value !== null
        && parseQuantity(form.shares) !== null
        && parsePrice(form.netValue) !== null
        && (parseAmount(form.netAmount) ?? 0) > 0
        && parseAmount(form.fee) !== null
    case 'sellArrival':
      return arrivingConfirmId.value !== null && (parseAmount(form.netAmount) ?? 0) > 0
    default:
      return false
  }
})

watch(
  () => form.positionId,
  (positionId) => {
    const position = overview.value?.positions.find((item) => item.id === positionId)

    if (!position || sheet.value !== 'shares') {
      return
    }

    form.shares = String(position.shares)
  }
)

onMounted(loadOverview)

async function loadOverview(): Promise<void> {
  isLoading.value = true
  errorMessage.value = ''

  try {
    overview.value = await fetchFundOverview()
    ensureActiveGroup()

    if (!form.positionId && overview.value.positions.length > 0) {
      form.positionId = overview.value.positions[0]!.id
    }
  } catch (error) {
    overview.value = null

    if (error instanceof ApiError && error.status === 404) {
      await router.replace({ name: 'setup' })
      return
    }

    errorMessage.value = getApiErrorMessage(error, '基金数据加载失败，请重试。')
  } finally {
    isLoading.value = false
  }
}

function ensureActiveGroup(): void {
  if (activeGroup.value === 'all') {
    return
  }

  if (activeGroup.value === 'ungrouped') {
    if (!allFunds.value.some((fund) => !fund.groupId)) {
      activeGroup.value = 'all'
    }

    return
  }

  if (!groups.value.some((group) => group.id === activeGroup.value)) {
    activeGroup.value = 'all'
  }
}

function loadMetricKeys(): FundMetricKey[] {
  try {
    const stored = localStorage.getItem(metricStorageKey)

    if (stored) {
      const keys = JSON.parse(stored) as unknown

      if (Array.isArray(keys)) {
        const valid = keys.filter((key): key is FundMetricKey =>
          metricOptions.some((option) => option.key === key)).slice(0, 3)

        if (valid.length) {
          return valid
        }
      }
    }
  } catch {
    // 浏览器存储不可用时使用默认三项指标。
  }

  return [...defaultMetrics]
}

function persistMetricKeys(): void {
  try {
    localStorage.setItem(metricStorageKey, JSON.stringify(metricKeys.value))
  } catch {
    // 指标只是展示设置，保存失败不影响账本。
  }
}

function toggleMetric(key: FundMetricKey): void {
  metricNotice.value = ''

  if (metricKeys.value.includes(key)) {
    if (metricKeys.value.length <= 1) {
      metricNotice.value = '至少保留一项指标。'
      return
    }

    metricKeys.value = metricKeys.value.filter((item) => item !== key)
    persistMetricKeys()
    return
  }

  if (metricKeys.value.length >= 3) {
    metricNotice.value = '最多同时显示 3 项指标，请先取消一项。'
    return
  }

  metricKeys.value = [...metricKeys.value, key]
  persistMetricKeys()
}

function rateOf(fund: FundSummary): number {
  return fund.cost > 0 ? fund.holdingProfit / fund.cost : Number.NEGATIVE_INFINITY
}

function metricDisplay(fund: FundSummary): { key: FundMetricKey; label: string; value: string; tone: string }[] {
  const total = scope.value.assetValue

  return metricKeys.value.map((key) => {
    switch (key) {
      case 'profit':
        return {
          key,
          label: '持有收益',
          value: fund.hasEffectiveQuotes ? formatSignedMoney(fund.holdingProfit) : '行情待补',
          tone: fund.holdingProfit > 0 ? 'profit' : fund.holdingProfit < 0 ? 'loss' : ''
        }
      case 'rate':
        return {
          key,
          label: '收益率',
          value: fund.hasEffectiveQuotes && fund.cost > 0
            ? `${formatSignedMoney(fund.holdingProfit / fund.cost * 100)}%`
            : '—',
          tone: fund.holdingProfit > 0 ? 'profit' : fund.holdingProfit < 0 ? 'loss' : ''
        }
      case 'cost':
        return { key, label: '成本', value: `¥${formatMoney(fund.cost)}`, tone: '' }
      case 'weight':
        return {
          key,
          label: '占比',
          value: total > 0 ? `${formatMoney(fund.assetValue / total * 100)}%` : '—',
          tone: ''
        }
      case 'pending':
        return {
          key,
          label: '在途',
          value: addAmounts(fund.pendingBuyAmount, fund.pendingArrivalAmount) > 0
            ? `¥${formatMoney(addAmounts(fund.pendingBuyAmount, fund.pendingArrivalAmount))}`
            : '—',
          tone: ''
        }
      default:
        return { key, label: '持仓价值', value: `¥${formatMoney(fund.value)}`, tone: '' }
    }
  })
}

function buildFallbackFunds(
  positions: FundPosition[],
  pendingBuys: PendingFundBuy[],
  pendingArrivals: PendingFundArrival[]
): FundSummary[] {
  const byName = new Map<string, FundPosition[]>()

  for (const position of positions) {
    const members = byName.get(position.name) ?? []
    members.push(position)
    byName.set(position.name, members)
  }

  return Array.from(byName, ([name, members]) => {
    const classes: FundShareClass[] = []
    const byClass = new Map<string, FundPosition[]>()

    for (const member of members) {
      const key = `${member.code?.trim() || member.name.normalize('NFKC').replace(/\s/g, '')}|${member.shareClass?.trim().toUpperCase() ?? ''}`
      const classMembers = byClass.get(key) ?? []
      classMembers.push(member)
      byClass.set(key, classMembers)
    }

    for (const [key, classMembers] of byClass) {
      const netValues = [...new Set(classMembers.map((member) => member.netValue))]
      const netValueDates = [...new Set(classMembers.map((member) => member.netValueDate))]

      classes.push({
        key,
        code: classMembers[0]?.code ?? null,
        shareClass: classMembers[0]?.shareClass || null,
        shares: sumAmounts(classMembers.map(member => member.shares)),
        cost: sumAmounts(classMembers.map(member => member.cost)),
        value: sumAmounts(classMembers.map(member => member.value)),
        holdingProfit: sumAmounts(classMembers.map(member => member.holdingProfit)),
        realizedProfit: sumAmounts(classMembers.map(member => member.realizedProfit)),
        netValue: netValues.length === 1 ? netValues[0]! : null,
        netValueDate: netValueDates.length === 1 ? netValueDates[0]! : null,
        platforms: classMembers.map((member) => ({
          positionId: member.id,
          platform: member.platform,
          name: member.name,
          code: member.code,
          shareClass: member.shareClass,
          shares: member.shares,
          cost: member.cost,
          netValue: member.netValue,
          netValueDate: member.netValueDate,
          value: member.value,
          holdingProfit: member.holdingProfit,
          realizedProfit: member.realizedProfit,
          hasEffectiveQuote: member.hasEffectiveQuote
        }))
      })
    }

    const memberIds = new Set(members.map((member) => member.id))
    const pendingBuyAmount = sumAmounts(pendingBuys
      .filter((item) => memberIds.has(item.positionId))
      .map(item => item.amount))
    const pendingArrivalAmount = sumAmounts(pendingArrivals
      .filter((item) => memberIds.has(item.positionId))
      .map(item => item.amount))
    const value = sumAmounts(members.map(member => member.value))
    const groupId = members.find((member) => member.groupId)?.groupId ?? null

    return {
      key: name,
      name,
      groupId,
      groupName: members.find((member) => member.groupName)?.groupName ?? null,
      value,
      cost: sumAmounts(members.map(member => member.cost)),
      holdingProfit: sumAmounts(members.map(member => member.holdingProfit)),
      realizedProfit: sumAmounts(members.map(member => member.realizedProfit)),
      pendingBuyAmount,
      pendingArrivalAmount,
      assetValue: sumAmounts([value, pendingBuyAmount, pendingArrivalAmount]),
      platformCount: new Set(members.map((member) => member.platform).filter(Boolean)).size,
      latestNetValueDate: members.map((member) => member.netValueDate).sort().at(-1) ?? null,
      historyConfirmed: members.every((member) => member.historyConfirmed),
      hasEffectiveQuotes: members.every((member) => member.hasEffectiveQuote),
      classes
    } satisfies FundSummary
  }).sort((left, right) => left.name.localeCompare(right.name, 'zh-Hans-CN'))
}

function toggleFund(key: string): void {
  expandedFund.value = expandedFund.value === key ? null : key
}

function selectGroup(key: GroupScope): void {
  activeGroup.value = key
  expandedFund.value = null
}

async function setView(view: PortfolioView): Promise<void> {
  activeView.value = view

  if (view === 'plans') {
    await loadPlans()
  }
}

async function loadPlans(): Promise<void> {
  if (plansLoaded.value || plansLoading.value) {
    return
  }

  plansLoading.value = true
  plansError.value = ''

  try {
    const result = await fetchPlanOverview()
    plans.value = result.plans.filter((plan) => plan.type === 'fundSip')
    plansLoaded.value = true
  } catch (error) {
    plansError.value = getApiErrorMessage(error, '定投计划暂时无法读取。')
  } finally {
    plansLoading.value = false
  }
}

function openGroupSheet(): void {
  groupSheetOpen.value = true
  groupDraft.value = ''
  groupError.value = ''
  editingGroupId.value = null
  editingGroupName.value = ''
}

function closeGroupSheet(): void {
  if (!groupBusy.value) {
    groupSheetOpen.value = false
  }
}

async function submitGroup(): Promise<void> {
  const name = groupDraft.value.trim()

  if (!name) {
    groupError.value = '请输入分组名称。'
    return
  }

  groupBusy.value = true
  groupError.value = ''

  try {
    await createFundGroup(name)
    groupDraft.value = ''
    notice.value = `已新增投资分组「${name}」。`
    await loadOverview()
  } catch (error) {
    groupError.value = getApiErrorMessage(error, '新增分组失败，请重试。')
  } finally {
    groupBusy.value = false
  }
}

function startRenameGroup(groupId: string, name: string): void {
  editingGroupId.value = groupId
  editingGroupName.value = name
  groupError.value = ''
}

async function saveGroupName(groupId: string): Promise<void> {
  const name = editingGroupName.value.trim()

  if (!name) {
    groupError.value = '分组名称不能为空。'
    return
  }

  groupBusy.value = true
  groupError.value = ''

  try {
    await renameFundGroup(groupId, name)
    editingGroupId.value = null
    notice.value = `分组已更名为「${name}」。`
    await loadOverview()
  } catch (error) {
    groupError.value = getApiErrorMessage(error, '分组改名失败，请重试。')
  } finally {
    groupBusy.value = false
  }
}

async function removeGroup(groupId: string, name: string): Promise<void> {
  if (!window.confirm(`删除分组「${name}」？组内基金会回到未分组，持仓和收益不变。`)) {
    return
  }

  groupBusy.value = true
  groupError.value = ''

  try {
    await deleteFundGroup(groupId)

    if (activeGroup.value === groupId) {
      activeGroup.value = 'all'
    }

    notice.value = `已删除分组「${name}」，组内基金回到未分组。`
    await loadOverview()
  } catch (error) {
    groupError.value = getApiErrorMessage(error, '删除分组失败，请重试。')
  } finally {
    groupBusy.value = false
  }
}

async function moveFund(fund: FundSummary, event: Event): Promise<void> {
  const target = event.target as HTMLSelectElement
  const groupId = target.value || null
  const positionId = fund.classes.flatMap((fundClass) => fundClass.platforms)[0]?.positionId

  if (!positionId) {
    return
  }

  try {
    await assignFundGroup(positionId, groupId)
    notice.value = groupId
      ? `「${fund.name}」已归入「${groupChips.value.find((chip) => chip.key === groupId)?.label ?? '分组'}」。`
      : `「${fund.name}」已移到未分组。`
    await loadOverview()
  } catch (error) {
    notice.value = getApiErrorMessage(error, '调整分组失败，请重试。')
    await loadOverview()
  }
}

function planCycleLabel(plan: AutoPlan): string {
  if (plan.cycle === 'tradingDay') {
    return '三地交易日'
  }

  if (plan.cycle === 'weekly') {
    const weekdays = ['', '周一', '周二', '周三', '周四', '周五', '周六', '周日']
    return `每周${weekdays[plan.dayOfWeek ?? 0] ?? ''}`
  }

  return `每月 ${plan.dayOfMonth ?? 1} 日`
}

function openSheet(kind: SheetKind, positionId?: string): void {
  sheet.value = kind
  confirmingSellId.value = null
  arrivingConfirmId.value = null
  requestId.value = createRequestId()
  formError.value = ''
  form.note = ''
  form.relatedCashAdjustmentId = null
  form.businessDate = today
  form.shares = ''
  form.cost = ''
  form.amount = ''
  form.netAmount = ''
  form.fee = '0'
  form.name = ''
  form.code = ''
  form.shareClass = ''
  form.platform = ''
  form.confirmationDelayDays = '2'

  if (kind === 'opening') {
    form.positionId = ''
    form.netValue = ''
    form.netValueDate = today
    return
  }

  form.positionId = positionId ?? overview.value?.positions[0]?.id ?? ''

  const position = selectedPosition.value
  if (kind === 'shares' && position) {
    form.shares = String(position.shares)
  }
}

function openSellConfirmSheet(application: PendingFundSell): void {
  openSheet('sellConfirm', application.positionId)
  confirmingSellId.value = application.transactionId
  form.shares = String(application.shares)
  form.netValue = ''
  form.netValueDate = today
  form.businessDate = today
  form.note = application.note ?? ''
}

function openArrivalSheet(arrival: PendingFundArrival): void {
  openSheet('sellArrival', arrival.positionId)
  arrivingConfirmId.value = arrival.transactionId
  form.netAmount = String(arrival.amount)
  form.businessDate = today
  form.note = arrival.note ?? ''
}

function closeSheet(): void {
  if (!isSaving.value) {
    sheet.value = null
  }
}

async function submit(): Promise<void> {
  if (!sheet.value || !overview.value) {
    return
  }

  formError.value = ''

  try {
    if (sheet.value === 'opening') {
      await submitOpening()
    } else if (sheet.value === 'buy') {
      await submitBuy()
    } else if (sheet.value === 'shares') {
      await submitShareCorrection()
    } else if (sheet.value === 'sell') {
      await submitSell()
    } else if (sheet.value === 'sellConfirm') {
      await submitSellConfirm()
    } else if (sheet.value === 'sellArrival') {
      await submitSellArrival()
    }
  } catch (error) {
    formError.value = getApiErrorMessage(error, '保存失败，请重试。')
  } finally {
    isSaving.value = false
  }
}

async function submitOpening(): Promise<void> {
  const shares = parseNonNegativeQuantity(form.shares)
  const cost = parseAmount(form.cost)

  if (!form.name.trim()) {
    formError.value = '请输入基金名称。'
    return
  }

  if (shares === null || cost === null) {
    formError.value = '请填写有效的份额和成本。'
    return
  }

  if (shares === 0 && cost !== 0) {
    formError.value = '份额为 0 时，持仓成本也应为 0。'
    return
  }

  isSaving.value = true
  await createFundPosition({
    name: form.name.trim(),
    code: form.code.trim(),
    shareClass: form.shareClass.trim(),
    platform: form.platform.trim(),
    shares,
    cost,
    confirmationDelayDays: Number(form.confirmationDelayDays),
    note: form.note.trim(),
    holdingDate: form.businessDate,
    requestId: requestId.value
  })
  notice.value = `已录入基金「${form.name.trim()}」。`
  sheet.value = null
  await loadOverview()
}

async function submitBuy(): Promise<void> {
  const position = selectedPosition.value
  const amount = parseAmount(form.amount)
  const fee = parseAmount(form.fee)

  if (!position) {
    formError.value = '请选择基金持仓。'
    return
  }

  if (amount === null || amount <= 0 || fee === null || fee < 0 || fee >= amount) {
    formError.value = '请填写有效的扣款金额和小于扣款金额的申购费。'
    return
  }

  isSaving.value = true
  const result = await buyFund({
    positionId: position.id,
    amount,
    fee,
    businessDate: form.businessDate,
    note: form.note.trim(),
    relatedCashAdjustmentId: form.relatedCashAdjustmentId,
    requestId: requestId.value
  })
  notice.value = `已记录买入待确认 ¥${formatMoney(result.activity.amount)}，当前现金 ¥${formatMoney(result.cashBalance)}。`
  sheet.value = null
  await loadOverview()
}

async function submitShareCorrection(): Promise<void> {
  const position = selectedPosition.value
  const shares = parseNonNegativeQuantity(form.shares)
  if (!position || shares === null) {
    formError.value = '请填写有效的持仓份额。'
    return
  }

  isSaving.value = true
  await correctFundShares({
    positionId: position.id,
    shares,
    note: form.note.trim() || '持仓份额核对',
    requestId: requestId.value
  })
  notice.value = `已更新「${position.name}」持仓份额。`
  sheet.value = null
  await loadOverview()
}

async function submitSell(): Promise<void> {
  const position = selectedPosition.value
  const shares = parseQuantity(form.shares)

  if (!position) {
    formError.value = '请选择基金持仓。'
    return
  }

  if (shares === null) {
    formError.value = '请填写有效的申请赎回份额。'
    return
  }

  isSaving.value = true
  const result = await sellFund({
    positionId: position.id,
    shares,
    businessDate: form.businessDate,
    note: form.note.trim(),
    requestId: requestId.value
  })
  notice.value = `已申请赎回 ${formatQuantity(result.activity.shares)} 份，确认后转为待到账。`
  sheet.value = null
  await loadOverview()
}

async function submitSellConfirm(): Promise<void> {
  const application = selectedSellApplication.value
  const shares = parseQuantity(form.shares)
  const netValue = parsePrice(form.netValue)
  const netAmount = parseAmount(form.netAmount)
  const fee = parseAmount(form.fee)

  if (!application) {
    formError.value = '赎回申请不存在，请刷新后重试。'
    return
  }

  if (shares === null || netValue === null || netAmount === null || netAmount <= 0 || fee === null || fee < 0) {
    formError.value = '请填写有效的确认份额、净值、净回款和费用。'
    return
  }

  if (shares > application.shares) {
    formError.value = '确认份额不能超过申请赎回份额。'
    return
  }

  if (fee > netAmount) {
    formError.value = '费用不能超过净回款金额。'
    return
  }

  if (form.businessDate < application.businessDate) {
    formError.value = '确认日期不能早于申请日期。'
    return
  }

  const valueDateError = validateValueDate(form.netValueDate, '净值日期')

  if (valueDateError) {
    formError.value = valueDateError
    return
  }

  if (form.netValueDate > form.businessDate) {
    formError.value = '净值日期不能晚于确认日期。'
    return
  }

  isSaving.value = true
  const result = await confirmFundSell(application.transactionId, {
    shares,
    netValue,
    netValueDate: form.netValueDate,
    fee,
    netAmount,
    businessDate: form.businessDate,
    note: form.note.trim(),
    requestId: requestId.value
  })
  notice.value = `已确认赎回 ${formatQuantity(result.activity.shares)} 份，待到账 ¥${formatMoney(result.activity.amount)}。`
  sheet.value = null
  await loadOverview()
}

async function submitSellArrival(): Promise<void> {
  const arrival = selectedPendingArrival.value
  const amount = parseAmount(form.netAmount)

  if (!arrival) {
    formError.value = '待到账记录不存在，请刷新后重试。'
    return
  }

  if (amount === null || amount <= 0) {
    formError.value = '请填写有效的到账金额。'
    return
  }

  if (amount !== arrival.amount) {
    formError.value = '到账金额必须与待到账金额一致；如有差额，请先更正赎回确认。'
    return
  }

  if (form.businessDate < arrival.businessDate) {
    formError.value = '到账日期不能早于确认日期。'
    return
  }

  isSaving.value = true
  const result = await arriveFundSell(arrival.transactionId, {
    amount,
    businessDate: form.businessDate,
    note: form.note.trim(),
    requestId: requestId.value
  })
  notice.value = `赎回到账 ¥${formatMoney(result.activity.amount)}，当前现金 ¥${formatMoney(result.cashBalance)}。`
  sheet.value = null
  await loadOverview()
}

function validateValueDate(value: string, label: string): string | null {
  if (!value) {
    return `请选择${label}。`
  }

  if (value > today) {
    return `${label}不能晚于今天。`
  }

  return null
}

function positionLabel(position: FundPosition): string {
  return position.shareClass ? `${position.name} ${position.shareClass}` : position.name
}

function quoteSourceLabel(source: string | null): string {
  switch (source) {
    case 'eastmoney':
      return '天天基金'
    default:
      return source ?? '净值数据源'
  }
}

async function refreshQuote(positionId: string): Promise<void> {
  refreshingQuote.value = true

  try {
    const result = await refreshFundQuote(positionId)
    notice.value = result.updated
      ? `已获取最新净值 ${formatQuantity(result.netValue)}（${formatBusinessDate(result.effectiveDate)}，${quoteSourceLabel(result.source)}）。`
      : `净值已是最新 ${formatQuantity(result.netValue)}（${formatBusinessDate(result.effectiveDate)}）。`
    await loadOverview()
  } catch (error) {
    notice.value = getApiErrorMessage(error, '获取净值失败，已保留最近有效值。')
  } finally {
    refreshingQuote.value = false
  }
}

async function cancelPending(transactionId: string): Promise<void> {
  try {
    await cancelFundPending(transactionId, createRequestId())
    notice.value = '待处理基金交易已撤销。'
    await loadOverview()
  } catch (error) {
    notice.value = getApiErrorMessage(error, '撤销失败，请刷新后重试。')
  }
}

function activityTitle(activity: FundActivity): string {
  switch (activity.type) {
    case 'opening':
      return '录入持仓'
    case 'correction':
      return '份额核对'
    case 'buy':
      return '基金买入'
    case 'buyPending':
      return '买入待确认'
    case 'buyConfirmed':
      return '买入确认'
    case 'sell':
      return '基金卖出'
    case 'sellApply':
      return '赎回申请'
    case 'sellConfirm':
      return '赎回确认'
    case 'sellArrival':
      return '赎回到账'
    case 'quote':
      return '更新净值'
    default:
      return '基金记录'
  }
}

function activityNote(activity: FundActivity): string {
  if (activity.note) {
    return activity.note
  }

  switch (activity.type) {
    case 'opening':
      return '已有持仓，不扣现金'
    case 'correction':
      return '按实际持有份额核对'
    case 'buy':
      return '现金 → 基金持仓'
    case 'buyPending':
      return '现金 → 买入待确认'
    case 'buyConfirmed':
      return '买入待确认 → 基金持仓'
    case 'sell':
      return '基金持仓 → 现金'
    case 'sellApply':
      return '份额冻结，仍在持仓'
    case 'sellConfirm':
      return '基金持仓 → 待到账'
    case 'sellArrival':
      return '待到账 → 现金'
    default:
      return '仅更新估值'
  }
}

function activityAmountText(activity: FundActivity): string {
  if (activity.type === 'buy' || activity.type === 'buyPending' || activity.type === 'sell'
    || activity.type === 'sellArrival') {
    return formatSignedMoney(activity.cashEffect)
  }

  if (activity.type === 'quote') {
    return `净值 ${formatQuantity(activity.netValue)}`
  }

  if (activity.type === 'buyConfirmed' || activity.type === 'sellApply') {
    return `${formatQuantity(activity.shares)} 份`
  }

  if (activity.type === 'correction') {
    return `核对后 ${formatQuantity(activity.shares)} 份`
  }

  if (activity.type === 'sellConfirm') {
    return `待到账 ${formatMoney(activity.amount)}`
  }

  return `成本 ${formatMoney(activity.amount)}`
}

function activityMeta(activity: FundActivity): string {
  if (activity.type === 'quote') {
    return `${activity.positionName} · ${formatBusinessDate(activity.businessDate)}`
  }

  const shares = activity.shares === null ? '' : `${formatQuantity(activity.shares)} 份`
  const value = activity.netValue === null ? '' : `净值 ${formatQuantity(activity.netValue)}`
  const fee = activity.fee > 0 ? ` · 费用 ${formatMoney(activity.fee)}` : ''
  const details = [shares, value].filter(Boolean).join(' · ')

  return `${activity.positionName}${details ? ` · ${details}` : ''}${fee}`
}

function goBack(): void {
  void router.push({ name: 'home' })
}
</script>

<template>
  <main class="page funds-page with-nav">
    <header class="funds-header">
      <BackButton @back="goBack" />
      <details class="investment-actions">
          <summary class="icon-button" aria-label="记录投资"><AppIcon name="plus" /></summary>
          <div class="funds-actions" @click="($event.currentTarget as HTMLElement).closest('details')?.removeAttribute('open')">
        <button type="button" class="action-secondary" @click="openSheet('opening')">录入持仓</button>
        <button
          type="button"
          class="action-primary"
          :disabled="!overview?.positions.length"
          @click="openSheet('buy')"
        >
          买入
        </button>
        <button
          type="button"
          class="action-secondary"
          :disabled="!overview?.positions.length"
          @click="openSheet('sell')"
        >
          卖出
        </button>
      </div>
      </details>
    </header>

    <p v-if="isLoading && !overview" class="state">正在读取基金…</p>

    <section v-else-if="errorMessage" class="state">
      <p>{{ errorMessage }}</p>
      <button type="button" class="secondary-button" @click="loadOverview">重新加载</button>
    </section>

    <template v-else-if="overview">
      <section class="funds-hero">
        <p class="hero-label">
          {{ activeGroupLabel }}
          <span v-if="searchText.trim()" class="hero-search" :title="searchText">· 搜索「{{ searchText.trim() }}」</span>
          <span class="hero-tag">{{ scope.historyConfirmed ? '已确认持仓' : '持仓日期待确认' }}</span>
        </p>
        <h2 class="hero-balance"><span v-if="scope.hasEffectiveQuotes">¥</span>{{ scope.hasEffectiveQuotes ? formatMoney(scope.assetValue) : '行情待补' }}</h2>
        <p class="hero-meta">
          {{ scopedFunds.length }} 只基金 · 持仓成本 {{ formatMoney(scope.cost) }}
          <template v-if="scope.pendingBuyAmount > 0"> · 含待确认 ¥{{ formatMoney(scope.pendingBuyAmount) }}</template>
          <template v-if="scope.pendingArrivalAmount > 0"> · 含待到账 ¥{{ formatMoney(scope.pendingArrivalAmount) }}</template>
        </p>
        <div class="hero-metrics">
          <div>
            <span>持有收益</span>
            <b :class="{ profit: scope.holdingProfit > 0, loss: scope.holdingProfit < 0 }">
              {{ scope.hasEffectiveQuotes ? formatSignedMoney(scope.holdingProfit) : '行情待补' }}
            </b>
          </div>
          <div>
            <span>持有收益率</span>
            <b>{{ scope.hasEffectiveQuotes && scope.cost > 0 ? `${formatSignedMoney(scope.holdingProfit / scope.cost * 100)}%` : '—' }}</b>
          </div>
          <div>
            <span>已实现收益</span>
            <b>{{ scope.historyConfirmed ? formatSignedMoney(scope.realizedProfit) : '待核对' }}</b>
          </div>
        </div>
      </section>


      <div class="group-bar">
        <div class="group-chips" role="tablist" aria-label="投资分组">
          <button
            v-for="chip in groupChips"
            :key="chip.key"
            type="button"
            :class="{ active: activeGroup === chip.key }"
            role="tab"
            :aria-selected="activeGroup === chip.key"
            @click="selectGroup(chip.key)"
          >
            {{ chip.label }} <small>{{ chip.count }}</small>
          </button>
        </div>
        <button type="button" class="group-manage" @click="openGroupSheet">管理</button>
      </div>

      <div class="view-tabs ui-segmented" role="tablist" aria-label="投资视图">
        <button type="button" role="tab" :aria-selected="activeView === 'positions'" :class="{ active: activeView === 'positions' }" @click="setView('positions')">持仓</button>
        <button type="button" role="tab" :aria-selected="activeView === 'returns'" :class="{ active: activeView === 'returns' }" @click="setView('returns')">收益</button>
        <button type="button" role="tab" :aria-selected="activeView === 'activities'" :class="{ active: activeView === 'activities' }" @click="setView('activities')">交易</button>
        <button type="button" role="tab" :aria-selected="activeView === 'plans'" :class="{ active: activeView === 'plans' }" @click="setView('plans')">定投</button>
      </div>
      <details v-if="allFunds.length" class="portfolio-settings ui-disclosure">
        <summary><span><AppIcon name="filter" /> {{ searchText.trim() ? `已筛选：${searchText.trim()}` : '搜索、排序与指标' }}</span></summary>
      <div class="portfolio-toolbar">
        <input v-model="searchText" type="search" placeholder="搜索基金名称或代码" aria-label="搜索基金" />
        <select v-model="sortKey" aria-label="排序方式">
          <option v-for="option in sortOptions" :key="option.key" :value="option.key">按{{ option.label }}</option>
        </select>
      </div>

      <div class="metric-picker" aria-label="持仓指标">
        <span>指标</span>
        <button
          v-for="option in metricOptions"
          :key="option.key"
          type="button"
          :class="{ active: metricKeys.includes(option.key) }"
          @click="toggleMetric(option.key)"
        >
          {{ option.label }}
        </button>
      </div>
      <p v-if="metricNotice" class="summary-note">{{ metricNotice }}</p>

      </details>
      <div v-if="searchText.trim()" class="active-search">
        <span>当前仅显示搜索结果</span>
        <button type="button" class="back-link" @click="searchText = ''">清除搜索</button>
      </div>

      <p v-if="notice" class="notice" role="status">{{ notice }}</p>

      <details v-if="scopedPendingBuys.length || scopedSellApplications.length || scopedPendingArrivals.length" class="pending-overview ui-disclosure">
        <summary><span>待处理交易</span><b>{{ scopedPendingBuys.length + scopedSellApplications.length + scopedPendingArrivals.length }} 笔待核对</b></summary>
      <template v-if="scopedPendingBuys.length">
        <div class="section-head">
          <h2>买入待确认</h2>
          <span class="meta">{{ scopedPendingBuys.length }} 笔 · ¥{{ formatMoney(scope.pendingBuyAmount) }}</span>
        </div>
        <section class="pending-list">
          <article v-for="pending in scopedPendingBuys" :key="pending.transactionId" class="pending-card">
            <div class="pending-copy">
              <strong>{{ pending.positionName }}</strong>
              <small>
                下单 {{ formatBusinessDate(pending.businessDate) }} · T 日 {{ formatBusinessDate(pending.tradeDate) }}
                · 申购费 ¥{{ formatMoney(pending.fee) }}<template v-if="pending.note"> · {{ pending.note }}</template>
              </small>
            </div>
            <div class="pending-side">
              <b>¥{{ formatMoney(pending.amount) }}</b>
              <button v-if="pending.canCancel" type="button" class="confirm-button" @click="cancelPending(pending.transactionId)">撤销</button>
              <small v-else>预计 {{ formatBusinessDate(pending.confirmationDate) }} 按确认净值自动结转</small>
            </div>
          </article>
        </section>
      </template>

      <template v-if="scopedSellApplications.length">
        <div class="section-head">
          <h2>赎回申请</h2>
          <span class="meta">{{ scopedSellApplications.length }} 笔 · 份额已冻结</span>
        </div>
        <section class="pending-list">
          <article v-for="application in scopedSellApplications" :key="application.transactionId" class="pending-card">
            <div class="pending-copy">
              <strong>{{ application.positionName }}</strong>
              <small>
                {{ formatBusinessDate(application.businessDate) }} · {{ formatQuantity(application.shares) }} 份
                <template v-if="application.note"> · {{ application.note }}</template>
              </small>
            </div>
            <div class="pending-side">
              <button type="button" class="confirm-button" @click="openSellConfirmSheet(application)">登记实际结果</button>
              <button v-if="application.canCancel" type="button" class="secondary-button" @click="cancelPending(application.transactionId)">撤销申请</button>
              <small v-else>15:00 后不可撤销</small>
            </div>
          </article>
        </section>
      </template>

      <template v-if="scopedPendingArrivals.length">
        <div class="section-head">
          <h2>赎回待到账</h2>
          <span class="meta">{{ scopedPendingArrivals.length }} 笔 · ¥{{ formatMoney(scope.pendingArrivalAmount) }}</span>
        </div>
        <section class="pending-list">
          <article v-for="arrival in scopedPendingArrivals" :key="arrival.transactionId" class="pending-card">
            <div class="pending-copy">
              <strong>{{ arrival.positionName }}</strong>
              <small>
                {{ formatBusinessDate(arrival.businessDate) }} · {{ formatQuantity(arrival.shares) }} 份
                <template v-if="arrival.note"> · {{ arrival.note }}</template>
              </small>
            </div>
            <div class="pending-side">
              <b>¥{{ formatMoney(arrival.amount) }}</b>
              <button type="button" class="confirm-button" @click="openArrivalSheet(arrival)">登记到账</button>
            </div>
          </article>
        </section>
      </template>



      </details>

      <template v-if="activeView === 'positions' || activeView === 'returns'">
        <FundReturnsPanel
          v-if="activeView === 'returns'"
          :group-id="returnsGroupId"
          :ungrouped="returnsUngrouped"
        />

        <div class="section-head">
          <h2>{{ activeView === 'positions' ? '组合持仓' : '基金收益明细' }}</h2>
          <span class="meta">
            <template v-if="activeView === 'returns'">共 {{ scopedFunds.length }} 只基金</template>
            <template v-else>可用现金 ¥{{ formatMoney(overview.cashBalance) }}</template>
          </span>
        </div>

        <div v-if="sortedFunds.length" class="fund-columns" aria-hidden="true">
          <span>基金 / 类别</span>
          <div><span v-for="metric in metricDisplay(sortedFunds[0]!)" :key="metric.key">{{ metric.label }}</span></div>
          <span />
        </div>
        <section v-if="sortedFunds.length" class="fund-list">
          <article v-for="fund in sortedFunds" :key="fund.key" class="fund-card">
            <header
              role="button"
              tabindex="0"
              @click="toggleFund(fund.key)"
              @keydown.enter="toggleFund(fund.key)"
              @keydown.space.prevent="toggleFund(fund.key)"
              :aria-expanded="expandedFund === fund.key"
            >
              <div class="fund-title">
                <strong>{{ fund.name }}</strong>
                <small>
                  {{ fund.groupName ?? '未分组' }} · {{ fund.classes.length }} 个类别 · 净值日期
                  {{ fund.latestNetValueDate ? fund.latestNetValueDate.slice(5) : '待补' }}
                </small>
              </div>
              <div class="fund-metrics">
                <div v-for="metric in metricDisplay(fund)" :key="metric.key">
                  <span>{{ metric.label }}</span>
                  <b :class="metric.tone">{{ metric.value }}</b>
                </div>
              </div>
              <AppIcon class="expand-mark" :class="{ expanded: expandedFund === fund.key }" name="chevron" />
            </header>

            <div v-if="expandedFund === fund.key" class="fund-detail">
              <label class="fund-group-select">
                <span>投资分组</span>
                <select :value="fund.groupId ?? ''" @change="moveFund(fund, $event)">
                  <option value="">未分组</option>
                  <option v-for="group in groups" :key="group.id" :value="group.id">{{ group.name }}</option>
                </select>
              </label>

              <section
                v-for="(fundClass, index) in fund.classes"
                :key="fundClass.key ?? index"
                class="fund-class"
              >
                <header>
                  <strong>{{ fundClass.shareClass ? `${fundClass.shareClass} 类` : '未标类别' }}</strong>
                  <span>{{ formatQuantity(fundClass.shares) }} 份 · 成本 ¥{{ formatMoney(fundClass.cost) }}</span>
                </header>
                <p class="fund-class-meta">
                  净值 {{ fundClass.netValue === null ? '来源净值不一致' : formatQuantity(fundClass.netValue) }}
                  <template v-if="fundClass.netValueDate"> · {{ formatBusinessDate(fundClass.netValueDate) }}</template>
                  · 估值 ¥{{ formatMoney(fundClass.value) }}
                  · 持有盈亏 {{ formatSignedMoney(fundClass.holdingProfit) }}
                  · 已实现盈亏 {{ formatSignedMoney(fundClass.realizedProfit) }}
                </p>
                <div class="holding-actions">
                  <div v-for="holding in fundClass.platforms" :key="holding.positionId" class="holding-action-row">
                    <span>{{ holding.platform || '未标平台' }} · {{ formatQuantity(holding.shares) }} 份</span>
                    <div>
                      <button type="button" @click="openSheet('buy', holding.positionId)">买入</button>
                      <button type="button" @click="openSheet('sell', holding.positionId)">卖出</button>
                      <button type="button" @click="openSheet('shares', holding.positionId)">调整份额</button>
                      <button type="button" :disabled="refreshingQuote || !holding.code" @click="refreshQuote(holding.positionId)">刷新净值</button>
                    </div>
                  </div>
                </div>
                <p class="fund-class-meta">
                  {{ fundClass.code ?? fundClass.platforms[0]?.code ?? '未填代码' }} ·
                  {{ fundClass.identityBasis ?? (fundClass.platforms[0]?.code ? '按基金代码和类别匹配' : '按规范化基金名称和类别匹配') }}
                </p>
              </section>

              <p class="summary-note">
                同一基金的 A/C 类分别列示份额和净值，不合并份额，也不平均净值。
                <template v-if="!fund.hasEffectiveQuotes"> 部分持仓行情依据不足，估值仅供参考。</template>
              </p>
            </div>
          </article>
        </section>

        <EmptyState
          v-else
          icon="chart"
          :title="allFunds.length ? '没有找到基金' : '从第一笔持仓开始'"
          :description="allFunds.length ? '试试其他分组，或清除搜索条件。' : '录入已有基金，查看持仓价值与收益。'"
          :action-label="allFunds.length ? '查看全部基金' : '录入已有持仓'"
          @action="allFunds.length ? (selectGroup('all'), searchText = '') : openSheet('opening')"
        />

        <p v-if="!scope.historyConfirmed || !scope.hasEffectiveQuotes" class="summary-note">
          部分持仓日期或行情依据不足，当前估值可能不完整。
        </p>
      </template>

      <template v-else-if="activeView === 'activities'">
        <div class="section-head">
          <h2>交易记录</h2>
          <span class="meta">{{ scopedActivities.length }} 条 · 最近 200 条</span>
        </div>

        <section v-if="scopedActivities.length" class="activity-card">
          <div v-for="activity in scopedActivities" :key="activity.id" class="activity-row">
            <span class="activity-mark">{{ activity.type === 'correction' ? '核' : activity.type === 'buy' ? '买' : activity.type === 'buyPending' ? '待' : activity.type === 'buyConfirmed' ? '确' : activity.type === 'sell' ? '卖' : activity.type === 'sellApply' ? '申' : activity.type === 'sellConfirm' ? '赎' : activity.type === 'sellArrival' ? '到' : activity.type === 'quote' ? '净' : '录' }}</span>
            <span class="activity-copy">
              <strong>{{ activity.isReversed ? `已撤销 · ${activityTitle(activity)}` : activityTitle(activity) }}</strong>
              <small>{{ activityNote(activity) }} · {{ activityMeta(activity) }}</small>
            </span>
            <b class="activity-value" :class="{ income: activity.cashEffect > 0 }">
              {{ activity.isReversed ? '已撤销' : activityAmountText(activity) }}
            </b>
          </div>
        </section>

        <EmptyState v-else compact icon="ledger" title="还没有交易记录" description="记录买入、赎回等投资变动后，可在这里查看。" />
      </template>

      <template v-else>
        <div class="section-head">
          <h2>定投计划</h2>
          <span class="meta">与自动记账共用同一计划</span>
        </div>

        <p v-if="plansLoading" class="state">正在读取定投计划…</p>
        <section v-else-if="plansError" class="state">
          <p>{{ plansError }}</p>
          <button type="button" class="secondary-button" @click="plansLoaded = false; plansError = ''; loadPlans()">重新加载</button>
        </section>
        <section v-else-if="scopedPlans.length" class="fund-plan-list">
          <article v-for="plan in scopedPlans" :key="plan.id" class="fund-plan-row">
            <div>
              <strong>{{ plan.name }}</strong>
              <small>
                {{ planCycleLabel(plan) }} · {{ plan.isActive ? '启用' : '已暂停' }}
                · 下期 {{ plan.nextDueDate ? formatBusinessDate(plan.nextDueDate) : '待定' }}
              </small>
            </div>
            <b>¥{{ formatMoney(plan.amount) }}</b>
            <RouterLink to="/plans">管理</RouterLink>
          </article>
        </section>
        <EmptyState v-else compact icon="clock" title="还没有定投计划" description="为固定投入设置计划，到期后按计划记账。" action-label="设置定投计划" @action="router.push('/plans')" />
      </template>

      <details class="fund-help ui-disclosure">
        <summary>投资统计</summary>
      <p class="summary-note">
        基金买卖属于资产流转，不计入生活收支；净值更新只影响估值。分组只改变查看范围，不改持仓、成本和收益。
      </p>
      </details>
    </template>

    <BottomSheet
      :open="groupSheetOpen"
      title="投资分组"
      subtitle="自定义组合视角，最多 20 个字"
      :close-disabled="groupBusy"
      @close="closeGroupSheet"
    >
      <form class="form-stack" @submit.prevent="submitGroup">
        <label class="form-field">
          <span>新增分组</span>
          <div class="group-create-row">
            <input v-model="groupDraft" type="text" maxlength="20" placeholder="例如：纳斯达克指数" />
            <button type="submit" class="primary-button" :disabled="groupBusy">添加</button>
          </div>
        </label>
      </form>

      <p v-if="groupError" class="form-error" role="alert">{{ groupError }}</p>

      <section class="group-manage-list">
        <div v-for="group in groups" :key="group.id" class="group-manage-row">
          <template v-if="editingGroupId === group.id">
            <input v-model="editingGroupName" type="text" maxlength="20" aria-label="分组名称" />
            <button type="button" class="confirm-button" :disabled="groupBusy" @click="saveGroupName(group.id)">保存</button>
            <button type="button" class="text-button" :disabled="groupBusy" @click="editingGroupId = null">取消</button>
          </template>
          <template v-else>
            <div>
              <strong>{{ group.name }}</strong>
              <small>{{ group.fundCount }} 只基金 · {{ group.positionCount }} 个持仓</small>
            </div>
            <button type="button" class="text-button" @click="startRenameGroup(group.id, group.name)">改名</button>
            <button type="button" class="text-button danger" :disabled="groupBusy" @click="removeGroup(group.id, group.name)">删除</button>
          </template>
        </div>
        <p v-if="!groups.length" class="summary-note">还没有分组。新增后可在基金卡片里调整所属分组。</p>
      </section>

      <p class="summary-note">删除分组不会删除基金或改变资产，组内基金回到「未分组」。</p>
    </BottomSheet>

    <BottomSheet
      :open="sheet !== null"
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
          <span>当前持仓</span>
          <b>{{ formatQuantity(selectedPosition.shares) }} 份</b>
        </div>
        <div v-if="selectedSellApplication" class="summary-line">
          <span>申请赎回</span>
          <b>{{ formatQuantity(selectedSellApplication.shares) }} 份</b>
        </div>
        <div v-if="selectedPendingArrival" class="summary-line">
          <span>待到账</span>
          <b>¥{{ formatMoney(selectedPendingArrival.amount) }}</b>
        </div>
      </div>

      <form class="form-stack" @submit.prevent="submit">
        <template v-if="sheet === 'opening'">
          <label class="form-field">
            <span>基金名称</span>
            <input v-model="form.name" type="text" maxlength="50" placeholder="例如：纳指基金" />
          </label>
          <div class="form-row">
          <label class="form-field">
            <span>基金代码</span>
            <input v-model="form.code" type="text" maxlength="20" placeholder="获取官方净值所需" required />
            </label>
            <label class="form-field">
              <span>份额类别</span>
              <input v-model="form.shareClass" type="text" maxlength="10" placeholder="A / C" />
            </label>
          </div>
          <label class="form-field">
            <span>平台</span>
            <input v-model="form.platform" type="text" maxlength="30" placeholder="选填" />
          </label>
          <div class="form-row">
            <label class="form-field">
              <span>持有份额</span>
              <input v-model="form.shares" type="text" inputmode="decimal" placeholder="0.0000" />
            </label>
            <label class="form-field">
              <span>持仓成本（元）</span>
              <input v-model="form.cost" type="text" inputmode="decimal" placeholder="0.00" />
            </label>
          </div>
          <label class="form-field">
            <span>确认周期</span>
            <select v-model="form.confirmationDelayDays">
              <option value="2">默认 T+2</option>
              <option value="1">该基金 T+1</option>
            </select>
            <small>净值由数据源自动获取；T+1/T+2 请按基金规则设置。</small>
          </label>
        </template>

        <template v-else-if="sheet === 'buy'">
          <label class="form-field">
            <span>基金持仓</span>
            <select v-model="form.positionId">
              <option v-for="position in overview?.positions ?? []" :key="position.id" :value="position.id">
                {{ positionLabel(position) }}
              </option>
            </select>
          </label>
          <label class="form-field">
            <span>扣款金额（元）</span>
            <input v-model="form.amount" type="text" inputmode="decimal" placeholder="0.00" />
          </label>
          <label class="form-field">
            <span>实际申购费（元）</span>
            <input v-model="form.fee" type="text" inputmode="decimal" placeholder="0.00" />
          </label>
          <small v-if="!selectedPosition?.code" class="form-note">请先为该持仓设置基金代码，才能从数据源自动确认份额。</small>
        </template>

        <template v-else-if="sheet === 'shares'">
          <label class="form-field">
            <span>核对后实际份额</span>
            <input v-model="form.shares" type="text" inputmode="decimal" placeholder="0.0000" />
            <small>份额调整会生成当天的持仓核对记录，成本保持不变。</small>
          </label>
        </template>

        <template v-else-if="sheet === 'sell'">
          <label class="form-field">
            <span>基金持仓</span>
            <select v-model="form.positionId">
              <option v-for="position in overview?.positions ?? []" :key="position.id" :value="position.id">
                {{ positionLabel(position) }}
              </option>
            </select>
          </label>
          <label class="form-field">
            <span>申请赎回份额</span>
            <input v-model="form.shares" type="text" inputmode="decimal" placeholder="0.0000" />
            <small>申请后份额冻结，仍计入持仓估值；确认时才减少份额并转为待到账。</small>
          </label>
        </template>

        <template v-else-if="sheet === 'sellConfirm'">
          <div class="form-row">
            <label class="form-field">
              <span>确认赎回份额</span>
              <input v-model="form.shares" type="text" inputmode="decimal" placeholder="0.0000" />
            </label>
            <label class="form-field">
              <span>成交净值</span>
              <input v-model="form.netValue" type="text" inputmode="decimal" placeholder="0.0000" />
            </label>
          </div>
          <div class="form-row">
            <label class="form-field">
              <span>净值日期</span>
              <input v-model="form.netValueDate" type="date" :max="today" />
            </label>
            <label class="form-field">
              <span>费用（元）</span>
              <input v-model="form.fee" type="text" inputmode="decimal" placeholder="0.00" />
            </label>
          </div>
          <label class="form-field">
            <span>净到账金额（元）</span>
            <input v-model="form.netAmount" type="text" inputmode="decimal" placeholder="0.00" />
          </label>
          <p class="form-note">确认份额不能超过申请份额；按今天的净值日期结转。</p>
        </template>

        <template v-else-if="sheet === 'sellArrival'">
          <label class="form-field">
            <span>到账金额（元）</span>
            <input v-model="form.netAmount" type="text" inputmode="decimal" placeholder="0.00" />
            <small>必须与待到账金额一致；如有差额，请先更正赎回确认。</small>
          </label>
        </template>

        <label class="form-field">
          <span>备注</span>
          <input v-model="form.note" type="text" maxlength="200" placeholder="选填" />
        </label>

        <CashAdjustmentLink v-if="sheet === 'buy'" v-model="form.relatedCashAdjustmentId" :business-date="form.businessDate" :cash-effect="-(parseAmount(form.amount) ?? 0)" :disabled="isSaving" />
        <p class="form-note">{{ sheetNote }}</p>
        <p v-if="formError" class="form-error" role="alert">{{ formError }}</p>

        <div class="form-actions">
          <button type="button" class="secondary-button" :disabled="isSaving" @click="closeSheet">
            取消
          </button>
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
.funds-page {
  padding-bottom: calc(97px + env(safe-area-inset-bottom));
}

.funds-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 10px;
  margin-bottom: 16px;
}

.funds-header h1 {
  margin: 0;
  font-size: 19px;
  font-weight: 600;
  letter-spacing: -0.3px;
}

.back-link {
  min-height: 36px;
  padding: 0 4px;
  border: 0;
  background: transparent;
  color: var(--color-brand);
  font-size: 12px;
}

.header-spacer {
  width: 46px;
}

.funds-hero {
  padding: 0 3px 22px;
  background: transparent;
}

.hero-label {
  display: flex;
  align-items: center;
  gap: 7px;
  margin: 0;
  color: var(--color-text-secondary);
  font-size: 11px;
}

.hero-tag {
  padding: 2px 5px;
  border: 1px solid var(--color-border-subtle);
  border-radius: 3px;
  background: var(--color-bg-surface);
  color: var(--color-brand);
  font-size: 9px;
}

.hero-balance {
  margin: 8px 0 7px;
  font-size: 40px;
  font-weight: 600;
  letter-spacing: -1.1px;
  line-height: 1.2;
  font-variant-numeric: tabular-nums;
}

.hero-balance span {
  margin-right: 5px;
  font-size: 19px;
  font-weight: 400;
  letter-spacing: 0;
}

.hero-meta {
  margin: 0;
  color: var(--color-text-secondary);
  font-size: 10px;
  line-height: 1.6;
}

.hero-metrics {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 10px;
  margin-top: 22px;
}


.hero-metrics span {
  display: block;
  color: var(--color-text-secondary);
  font-size: 11px;
}

.hero-metrics b {
  display: block;
  margin-top: 7px;
  font-size: 15px;
  font-weight: 550;
  letter-spacing: -0.3px;
  font-variant-numeric: tabular-nums;
}

.profit {
  color: var(--color-finance-gain);
}

.loss {
  color: var(--color-finance-loss);
}

.hero-search {
  overflow: hidden;
  max-width: 170px;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.active-search {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin: -8px 0 12px;
  color: var(--color-text-secondary);
  font-size: 11px;
}
.funds-header {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 20px;
}

.funds-header .investment-actions {
  margin-left: auto;
}

.investment-actions {
  position: relative;
}
.investment-actions > summary {
  list-style: none;
  cursor: pointer;
}
.investment-actions > summary::-webkit-details-marker {
  display: none;
}
.funds-actions {
  position: absolute;
  top: 43px;
  right: 0;
  z-index: 30;
  display: grid;
  gap: 6px;
  width: 180px;
  padding: 10px;
  border: 1px solid var(--color-border-subtle);
  border-radius: 14px;
  background: var(--color-bg-surface);
  box-shadow: 0 8px 24px var(--color-shadow);
}
.funds-actions button {
  min-height: 42px;
  padding: 0 12px;
  border-radius: 8px;
  font-size: 13px;
  text-align: left;
}
.portfolio-settings {
  margin: 0 0 14px;
}
.portfolio-settings summary .app-icon {
  width: 16px;
  height: 16px;
  margin-right: 5px;
}
.portfolio-settings[open] {
  padding-bottom: 14px;
  border-bottom: 1px solid var(--color-border-subtle);
}
.pending-overview {
  container-type: inline-size;
  margin: 0 0 18px;
  padding: 0 12px;
  border-radius: 10px;
  background: var(--color-brand-soft);
}
.pending-overview summary {
  color: var(--color-brand);
}
.pending-overview summary b {
  margin-left: auto;
  font-size: 12px;
  font-weight: 500;
}
.pending-overview[open] {
  padding-bottom: 12px;
}
.pending-overview .section-head {
  margin-top: 14px;
}
.fund-help {
  margin-top: 24px;
  border-top: 1px solid var(--color-border-subtle);
}

.action-primary {
  border: 1px solid var(--color-brand);
  background: var(--color-brand);
  color: var(--color-on-brand);
}

.action-primary:hover:not(:disabled) {
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

.funds-actions button:disabled {
  border-color: var(--color-border-disabled);
  background: var(--color-bg-disabled);
  color: var(--color-text-disabled);
}

.group-bar {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-top: 0;
  margin-bottom: 16px;
}

.group-chips {
  display: flex;
  flex: 1;
  gap: 7px;
  overflow-x: auto;
  padding-bottom: 2px;
  scrollbar-width: none;
}

.group-chips::-webkit-scrollbar {
  display: none;
}

.group-chips button {
  flex: 0 0 auto;
  min-height: 30px;
  padding: 0 11px;
  border: 1px solid var(--color-border-subtle);
  border-radius: 999px;
  background: var(--color-bg-surface);
  color: var(--color-text-secondary);
  font-size: 11px;
}

.group-chips button.active {
  border-color: var(--color-brand);
  background: var(--color-brand);
  color: var(--color-on-brand);
  font-weight: 600;
}

.group-chips small {
  margin-left: 3px;
  font-size: 9px;
  font-weight: 400;
}

.group-manage {
  flex: 0 0 auto;
  min-height: 30px;
  padding: 0 8px;
  border: 0;
  background: transparent;
  color: var(--color-brand);
  font-size: 10px;
}

.portfolio-toolbar {
  display: flex;
  gap: 8px;
  margin-top: 12px;
}

.portfolio-toolbar input,
.portfolio-toolbar select {
  min-height: 36px;
  border: 1px solid var(--color-border-control);
  border-radius: 9px;
  background: var(--color-bg-surface);
  color: var(--color-text-primary);
  font-size: 11px;
}

.portfolio-toolbar input {
  flex: 1;
  min-width: 0;
  padding: 0 11px;
}

.portfolio-toolbar select {
  flex: 0 0 auto;
  max-width: 118px;
  padding: 0 8px;
}

.metric-picker {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
  margin-top: 10px;
}

.metric-picker > span {
  color: var(--color-text-secondary);
  font-size: 9px;
}

.metric-picker button {
  min-height: 32px;
  padding: 0 9px;
  border: 1px solid var(--color-border-subtle);
  border-radius: 999px;
  background: transparent;
  color: var(--color-text-secondary);
  font-size: 9px;
}

.metric-picker button.active {
  border-color: var(--color-brand);
  background: var(--color-brand-soft);
  color: var(--color-brand);
}

.view-tabs {
  margin-bottom: 8px;
}
.fund-list {
  overflow: hidden;
  border-radius: 12px;
  background: var(--color-bg-surface);
}
.fund-card {
  border-bottom: 1px solid var(--color-border-subtle);
}
.fund-card:last-child {
  border-bottom: 0;
}
.fund-card > header, .fund-columns {
  display: grid;
  grid-template-columns: minmax(0, 1.3fr) minmax(0, 2.1fr) 12px;
  align-items: center;
  gap: 8px;
}
.fund-card > header {
  min-height: 84px;
  padding: 10px 10px;
  cursor: pointer;
}
.fund-card > header:hover {
  background: var(--color-bg-hover);
}
.fund-title {
  min-width: 0;
}
.fund-title strong {
  display: block;
  color: var(--color-text-primary);
  font-size: 13px;
  font-weight: 550;
  line-height: 1.6;
  overflow-wrap: anywhere;
}
.fund-title small {
  display: block;
  margin-top: 4px;
  color: var(--color-text-secondary);
  font-size: 10px;
  line-height: 1.7;
}
.fund-metrics {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  align-items: center;
  gap: 5px;
  min-width: 0;
  text-align: right;
}
.fund-metrics > div {
  min-width: 0;
}
.fund-metrics span {
  position: absolute;
  width: 1px;
  height: 1px;
  overflow: hidden;
  clip-path: inset(50%);
  white-space: nowrap;
}
.fund-metrics b {
  display: block;
  font-size: 11px;
  font-weight: 550;
  line-height: 1.5;
  font-variant-numeric: tabular-nums;
  overflow-wrap: anywhere;
}
.fund-columns {
  padding: 10px;
  color: var(--color-text-secondary);
  font-size: 10px;
}
.fund-columns > div {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 5px;
  text-align: right;
}
.expand-mark {
  width: 12px;
  height: 16px;
  color: var(--color-text-secondary);
}
.expand-mark.expanded {
  transform: rotate(90deg);
}

@media (max-width: 360px) {
  .fund-columns {
    display: none;
  }
  .fund-card > header {
    grid-template-columns: minmax(0, 1fr) 12px;
    row-gap: 10px;
    padding: 14px 12px;
  }
  .fund-title {
    grid-column: 1;
    grid-row: 1;
  }
  .fund-title small {
    font-size: 11px;
  }
  .expand-mark {
    grid-column: 2;
    grid-row: 1;
  }
  .fund-metrics {
    grid-column: 1 / -1;
    grid-row: 2;
    text-align: left;
  }
  .fund-metrics span {
    position: static;
    display: block;
    width: auto;
    height: auto;
    margin-bottom: 5px;
    color: var(--color-text-secondary);
    font-size: 10px;
    clip-path: none;
  }
  .fund-metrics b {
    font-size: 12px;
    white-space: nowrap;
    overflow-wrap: normal;
  }
}

.fund-detail {
  padding: 0 14px 14px;
  border-top: 1px solid var(--color-border-subtle);
}

.fund-group-select {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-top: 12px;
  color: var(--color-text-secondary);
  font-size: 10px;
}

.fund-group-select select {
  flex: 1;
  min-height: 32px;
  padding: 0 8px;
  border: 1px solid var(--color-border-control);
  border-radius: 8px;
  background: var(--color-bg-surface);
  color: var(--color-text-primary);
  font-size: 11px;
}

.fund-class {
  margin-top: 12px;
  padding: 11px;
  border-radius: 10px;
  background: var(--color-bg-soft, rgba(0, 47, 167, 0.04));
}

.fund-class header {
  display: flex;
  justify-content: space-between;
  gap: 10px;
  font-size: 11px;
}

.fund-class header span {
  color: var(--color-text-secondary);
  font-size: 9px;
}

.fund-class-meta {
  margin: 6px 0 0;
  color: var(--color-text-secondary);
  font-size: 9px;
  line-height: 1.6;
}

.holding-actions {
  display: grid;
  gap: 8px;
  margin-top: 12px;
}

.holding-action-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  padding-top: 8px;
  border-top: 1px solid var(--color-border-subtle);
  color: var(--color-text-secondary);
  font-size: 11px;
}

.holding-action-row > div {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.holding-action-row button {
  min-height: 30px;
  padding: 0 9px;
  border: 1px solid var(--color-border-subtle);
  border-radius: 7px;
  background: var(--color-bg-surface);
  color: var(--color-brand);
  font-size: 11px;
}

.holding-action-row button:disabled {
  opacity: 0.5;
}

.fund-platform-row {
  display: flex;
  justify-content: space-between;
  gap: 10px;
  margin-top: 9px;
  padding-top: 9px;
  border-top: 1px dashed var(--color-border-subtle);
}

.fund-platform-row strong {
  display: block;
  font-size: 10px;
  font-weight: 550;
}

.fund-platform-row small {
  display: block;
  margin-top: 3px;
  color: var(--color-text-secondary);
  font-size: 9px;
}

.fund-platform-value {
  text-align: right;
}

.fund-platform-value b {
  font-size: 10px;
  font-weight: 550;
  font-variant-numeric: tabular-nums;
}

.fund-plan-list {
  display: flex;
  flex-direction: column;
  gap: 9px;
}

.fund-plan-row {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 13px 14px;
  border: 1px solid var(--color-border-subtle);
  border-radius: 12px;
  background: var(--color-bg-surface);
}

.fund-plan-row > div {
  flex: 1;
  min-width: 0;
}

.fund-plan-row strong {
  display: block;
  font-size: 12px;
  font-weight: 600;
}

.fund-plan-row small {
  display: block;
  margin-top: 4px;
  color: var(--color-text-secondary);
  font-size: 9px;
  line-height: 1.5;
}

.fund-plan-row > b {
  font-size: 12px;
  font-weight: 550;
  font-variant-numeric: tabular-nums;
}

.fund-plan-row a {
  color: var(--color-brand);
  font-size: 10px;
  text-decoration: none;
}

.group-create-row {
  display: flex;
  gap: 8px;
}

.group-create-row input {
  flex: 1;
  min-width: 0;
}

.group-create-row button {
  flex: 0 0 auto;
  min-height: 38px;
  padding: 0 14px;
  border-radius: 9px;
  font-size: 11px;
}

.group-manage-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin-top: 16px;
}

.group-manage-row {
  display: flex;
  align-items: center;
  gap: 8px;
  min-height: 44px;
  padding: 8px 10px;
  border: 1px solid var(--color-border-subtle);
  border-radius: 10px;
}

.group-manage-row > div {
  flex: 1;
  min-width: 0;
}

.group-manage-row strong {
  display: block;
  font-size: 11px;
  font-weight: 600;
}

.group-manage-row small {
  display: block;
  margin-top: 3px;
  color: var(--color-text-secondary);
  font-size: 9px;
}

.group-manage-row input {
  flex: 1;
  min-width: 0;
  min-height: 32px;
  padding: 0 8px;
  border: 1px solid var(--color-border-control);
  border-radius: 8px;
  font-size: 11px;
}

.text-button {
  flex: 0 0 auto;
  min-height: 28px;
  padding: 0 4px;
  border: 0;
  background: transparent;
  color: var(--color-brand);
  font-size: 10px;
}

.text-button.danger {
  color: var(--color-finance-loss);
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
  letter-spacing: -0.2px;
}

.meta {
  color: var(--color-text-secondary);
  font-size: 10px;
}

.pending-list {
  display: flex;
  flex-direction: column;
  gap: 9px;
}

.pending-card {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(190px, 48%);
  align-items: center;
  gap: 12px;
  padding: 13px 14px;
  border: 1px dashed var(--color-border-default);
  border-radius: 12px;
  background: var(--color-brand-soft);
}

.pending-copy {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}

.pending-copy strong {
  font-size: 13px;
  font-weight: 600;
  overflow-wrap: anywhere;
}

.pending-copy small {
  overflow: hidden;
  color: var(--color-text-secondary);
  font-size: 10px;
  line-height: 1.5;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.pending-side {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 10px;
  min-width: 0;
}

.pending-side b {
  flex: 0 0 auto;
  font-size: 14px;
  font-weight: 550;
  font-variant-numeric: tabular-nums;
}

.pending-side small {
  flex: 1 1 120px;
  min-width: 0;
  color: var(--color-text-secondary);
  font-size: 10px;
  line-height: 1.5;
  text-align: right;
  white-space: normal;
}

@container (max-width: 560px) {
  .pending-card {
    grid-template-columns: minmax(0, 1fr);
    gap: 8px;
  }

  .pending-side {
    justify-content: space-between;
  }
}

.confirm-button {
  min-height: 30px;
  padding: 0 10px;
  border: 1px solid var(--color-brand);
  border-radius: 7px;
  background: var(--color-bg-surface);
  color: var(--color-brand);
  font-size: 10px;
  font-weight: 550;
}

.position-list {
  display: flex;
  flex-direction: column;
  gap: 9px;
}

.position-card {
  padding: 14px;
  border: 1px solid var(--color-border-subtle);
  border-radius: 12px;
  background: var(--color-bg-surface);
}

.position-card header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 12px;
}

.position-card header strong {
  display: block;
  font-size: 13px;
  font-weight: 600;
}

.position-card header small {
  display: block;
  margin-top: 4px;
  color: var(--color-text-secondary);
  font-size: 10px;
}

.position-card header > b {
  font-size: 16px;
  font-weight: 550;
  font-variant-numeric: tabular-nums;
}

.position-metrics {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 10px;
  margin-top: 13px;
  padding-top: 11px;
  border-top: 1px solid var(--color-border-subtle);
}

.position-metrics > div + div {
  padding-left: 10px;
  border-left: 1px solid var(--color-border-subtle);
}

.position-metrics span {
  display: block;
  color: var(--color-text-secondary);
  font-size: 9px;
}

.position-metrics b {
  display: block;
  margin-top: 5px;
  font-size: 12px;
  font-weight: 550;
  font-variant-numeric: tabular-nums;
}

.position-card footer {
  display: flex;
  justify-content: space-between;
  gap: 10px;
  margin-top: 11px;
  color: var(--color-text-secondary);
  font-size: 9px;
}

.quote-error {
  display: block;
  margin-top: 7px;
  color: var(--color-finance-loss);
  font-size: 9px;
  line-height: 1.5;
}

.empty-state {
  padding: 34px 16px;
  border: 1px dashed var(--color-border-default);
  border-radius: 12px;
  text-align: center;
}

.empty-state p {
  margin: 0 0 14px;
  color: var(--color-text-secondary);
  font-size: 12px;
}

.empty-state .primary-button {
  width: 100%;
}

.activity-card {
  overflow: hidden;
  border: 1px solid var(--color-border-subtle);
  border-radius: 12px;
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
  font-size: 12px;
  font-weight: 550;
}

.activity-copy {
  display: flex;
  flex-direction: column;
  gap: 3px;
  flex: 1;
  min-width: 0;
}

.activity-copy strong {
  font-size: 13px;
  font-weight: 500;
}

.activity-copy small {
  overflow: hidden;
  color: var(--color-text-secondary);
  font-size: 10px;
  line-height: 1.5;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.activity-value {
  flex: 0 0 auto;
  font-size: 13px;
  font-weight: 550;
  font-variant-numeric: tabular-nums;
}

.income {
  color: var(--color-finance-income);
}

.summary-note {
  margin: 14px 0 0;
  color: var(--color-text-secondary);
  font-size: 10px;
  line-height: 1.7;
  text-align: center;
}
</style>
