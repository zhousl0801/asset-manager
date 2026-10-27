<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { fetchHistory, previewHistory, confirmHistory } from '../api/history'
import { getApiErrorMessage } from '../api/client'
import AppNav from '../components/AppNav.vue'
import BackButton from '../components/BackButton.vue'
import BottomSheet from '../components/BottomSheet.vue'
import CashAdjustmentLink from '../components/CashAdjustmentLink.vue'
import type { BusinessFact, HistoryChange, HistoryOverview, HistoryPreview, ReplayResult } from '../types/history'
import { formatMoney, formatQuantity, todayInBusinessTimeZone } from '../utils/format'
import { createRequestId } from '../utils/request'

const route = useRoute()
const router = useRouter()
const today = todayInBusinessTimeZone()
const date = ref(today)
const history = ref<HistoryOverview | null>(null)
const error = ref('')
const notice = ref('')
const busy = ref(false)
const selected = ref<BusinessFact | null>(null)
const draft = reactive<HistoryChange>({} as HistoryChange)
const preview = ref<HistoryPreview | null>(null)
const confirmedDraft = ref<HistoryChange | null>(null)
const formError = ref('')
let draftRevision = 0
const rows = computed(() => history.value ? [history.value.cashOpening, ...history.value.facts] : [])
const isQuote = computed(() => selected.value?.kind?.endsWith('Quote') ?? false)
const isConfirmedBuy = computed(() => selected.value?.kind === 'FundBuyConfirm')
const isConfirmedSell = computed(() => selected.value?.kind === 'FundSellConfirm')
const isSellApply = computed(() => selected.value?.kind === 'FundSellApply')
const isSellArrival = computed(() => selected.value?.kind === 'FundSellArrival')
const isGoldSellArrival = computed(() => selected.value?.kind === 'GoldSellArrival')
const isConfirmed = computed(() => isConfirmedBuy.value || isConfirmedSell.value)
const hasQuantity = computed(() => selected.value?.source === 'asset' && selected.value.quantity !== null)
const canLink = computed(() => selected.value?.source === 'entry' || (selected.value?.cashEffect ?? 0) !== 0)
const kindNames: Record<string, string> = {
  fundOpening: '基金起始持仓', fundBuy: '基金买入', fundBuyPending: '买入待确认', fundBuyConfirm: '买入确认',
  fundSell: '基金卖出', fundSellApply: '赎回申请', fundSellConfirm: '赎回确认', fundSellArrival: '赎回到账',
  fundQuote: '基金净值', fundCorrection: '基金持仓核对',
  goldOpening: '积存金起始持仓', goldCorrection: '积存金持仓核对', goldBuy: '积存金买入', goldSell: '积存金卖出',
  goldSellPending: '积存金卖出待到账', goldSellArrival: '积存金卖到账', goldQuote: '积存金报价',
  providentOpening: '起始公积金', providentDeposit: '公积金缴存', providentWithdrawal: '公积金提取'
}
function title(fact: BusinessFact): string {
  if (fact.source === 'cashOpening') return '起始现金'
  if (fact.source === 'adjustment') return '现金核对'
  if (fact.source === 'entry') return `${fact.entryKind === 'income' ? '收入' : '支出'} · ${fact.category}`
  return `${kindNames[fact.kind ?? ''] ?? '资产记录'}${fact.subject && !fact.subject.includes('起始资产') ? ` · ${fact.subject}` : ''}`
}
function money(value: number | null | undefined): string { return value == null ? '依据不足' : `¥${formatMoney(value)}` }
watch(draft, () => { draftRevision++; preview.value = null; confirmedDraft.value = null }, { deep: true, flush: 'sync' })
onMounted(load)
async function load(): Promise<void> {
  busy.value = true; error.value = ''
  try {
    history.value = await fetchHistory(date.value)
    const fact = history.value.facts.find(item => item.id === route.query.factId)
    if (fact && !fact.isReversed && !notice.value) edit(fact, 'correct')
  }
  catch (cause) { error.value = getApiErrorMessage(cause, '历史核对加载失败。') }
  finally { busy.value = false }
}
function edit(fact: BusinessFact, action: 'correct' | 'reverse'): void {
  selected.value = fact; formError.value = ''; preview.value = null
  Object.assign(draft, {
    requestId: createRequestId(), expectedLedgerVersion: history.value!.ledgerVersion,
    source: fact.source, factId: fact.id, expectedFactVersion: fact.version, action,
    date: fact.date, sequence: fact.sequence, amount: fact.targetBalance ?? fact.amount,
    quantity: fact.quantity, price: fact.priceDate ? fact.price : null, priceDate: fact.priceDate,
    fee: fact.fee, entryKind: fact.entryKind, category: fact.category, note: fact.note, reason: '', relatedCashAdjustmentId: fact.relatedCashAdjustmentId
  })
}
async function showPreview(): Promise<void> {
  busy.value = true; formError.value = ''
  try {
    if (isQuote.value) draft.priceDate = draft.date
    const change = structuredClone({ ...draft,
      price: typeof draft.price === 'number' ? draft.price : null,
      priceDate: draft.priceDate || null,
      quantity: typeof draft.quantity === 'number' ? draft.quantity : null
    })
    const revision = draftRevision
    const result = await previewHistory(change)
    if (revision !== draftRevision) return
    preview.value = result
    confirmedDraft.value = change
  } catch (cause) { formError.value = getApiErrorMessage(cause, '无法预览，请核对输入。') }
  finally { busy.value = false }
}
async function confirm(): Promise<void> {
  if (!preview.value || !confirmedDraft.value) return
  busy.value = true; formError.value = ''
  try {
    await confirmHistory(confirmedDraft.value, preview.value.previewToken)
    selected.value = null; notice.value = '历史变更已保存，余额、成本和核对差额已重新计算。'
    await load()
  } catch (cause) { formError.value = getApiErrorMessage(cause, '保存失败，请重新加载并预览。') }
  finally { busy.value = false }
}
function impacts(value: ReplayResult): { key: string; label: string; value: string }[] {
  return [
    { key: 'cash', label: '现金余额', value: money(value.cashBalance) },
    { key: 'pf', label: '公积金', value: money(value.providentBalance) },
    ...value.funds.flatMap((fund, index) => [
      { key: `f${index}q`, label: `基金 ${index + 1} 份额`, value: formatQuantity(fund.quantity) },
      { key: `f${index}c`, label: `基金 ${index + 1} 成本`, value: money(fund.cost) },
      { key: `f${index}r`, label: `基金 ${index + 1} 已实现收益`, value: money(fund.realizedProfit) },
      { key: `f${index}p`, label: `基金 ${index + 1} 净值`, value: fund.price == null ? '依据不足' : formatQuantity(fund.price) },
      { key: `f${index}d`, label: `基金 ${index + 1} 行情日`, value: fund.priceDate ?? '依据不足' },
      { key: `f${index}t`, label: `基金 ${index + 1} 待确认`, value: money(fund.pendingAmount) },
      { key: `f${index}a`, label: `基金 ${index + 1} 已申请赎回`, value: formatQuantity(fund.appliedShares) },
      { key: `f${index}w`, label: `基金 ${index + 1} 待到账`, value: money(fund.pendingArrivalAmount) },
      { key: `f${index}v`, label: `基金 ${index + 1} 估值`, value: money(fund.value) }
    ]),
    ...(value.gold ? [
      { key: 'gq', label: '积存金克数', value: formatQuantity(value.gold.quantity) },
      { key: 'gc', label: '积存金成本', value: money(value.gold.cost) },
      { key: 'gr', label: '积存金已实现收益', value: money(value.gold.realizedProfit) },
      { key: 'gp', label: '积存金报价', value: money(value.gold.price) },
      { key: 'gd', label: '积存金行情日', value: value.gold.priceDate ?? '依据不足' },
      { key: 'gw', label: '积存金待到账', value: money(value.gold.pendingArrivalAmount) },
      { key: 'gv', label: '积存金估值', value: money(value.gold.value) }
    ] : []),
    { key: 'knownTotal', label: '已有依据的部分合计', value: money(value.knownTotalAssets) },
    { key: 'total', label: '完整总资产', value: money(value.totalAssets) },
    ...value.adjustments.map(item => ({ key: item.id, label: `核对差额 · ${history.value?.facts.find(fact => fact.id === item.id)?.date ?? ''}`, value: money(item.difference) }))
  ]
}
const impactRows = computed(() => {
  if (!preview.value) return []
  const before = impacts(preview.value.before), after = impacts(preview.value.after)
  return [...new Set([...before, ...after].map(item => item.key))].map(key => ({ key,
    label: (after.find(item => item.key === key) ?? before.find(item => item.key === key))!.label,
    before: before.find(item => item.key === key)?.value ?? '已撤销', after: after.find(item => item.key === key)?.value ?? '已撤销'
  }))
})
function auditSummary(json: string | null): string {
  try { return JSON.parse(json ?? '{}').request.change.reason ?? '历史更正' } catch { return '历史更正' }
}
</script>

<template>
  <main class="page history-page with-nav">
    <header class="history-header"><BackButton @back="router.push({ name: 'home' })" /><h1>历史核对</h1></header>
    <p class="form-note">按业务日期与同日顺序重算。持仓起点与行情有效日期分别记录，依据不足时保留待确认状态。</p>
    <form class="history-date" @submit.prevent="load"><label class="form-field"><span>查看资产日期</span><input v-model="date" type="date" :max="today" required /></label><button class="secondary-button" :disabled="busy">查看</button></form>
    <p v-if="error" class="form-error" role="alert">{{ error }}</p>
    <p v-if="notice" class="notice" role="status">{{ notice }}</p>
    <template v-if="history">
      <section class="history-card">
        <p class="hero-label">{{ history.snapshot.asOfDate }} · 资产回看</p>
        <h2>{{ money(history.snapshot.totalAssets) }}</h2>
        <p v-if="history.snapshot.totalAssets === null" class="form-note">已有依据的部分合计 {{ money(history.snapshot.knownTotalAssets) }}，不代表完整总资产。</p>
        <ul v-if="history.snapshot.issues.length" class="coverage-issues"><li v-for="issue in history.snapshot.issues" :key="issue">{{ issue }}</li></ul>
        <div class="summary-line"><span>现金</span><b>{{ money(history.snapshot.cashBalance) }}</b></div>
        <div class="summary-line"><span>公积金</span><b>{{ money(history.snapshot.providentBalance) }}</b></div>
        <div v-for="(fund, index) in history.snapshot.funds" :key="fund.positionId ?? index" class="history-holding">
          <strong>基金 {{ index + 1 }} · {{ money(fund.value) }}</strong>
          <small>{{ fund.canRebuild ? `起始 ${fund.coverageFrom ?? '未覆盖'} · 份额 ${formatQuantity(fund.quantity)} · 成本 ${money(fund.cost)}` : '持仓日期待确认，当前缓存仅供核对' }}</small>
          <small v-if="fund.pendingAmount > 0">买入待确认 {{ money(fund.pendingAmount) }}</small>
          <small v-if="fund.appliedShares > 0">已申请赎回 {{ formatQuantity(fund.appliedShares) }} 份</small>
          <small v-if="fund.pendingArrivalAmount > 0">赎回待到账 {{ money(fund.pendingArrivalAmount) }}</small>
          <small>行情 {{ fund.priceDate ?? '无该时点行情' }} · {{ fund.priceSource === 'manual' ? '手动录入' : fund.priceSource ?? '未取得' }}</small>
        </div>
        <div v-if="history.snapshot.gold" class="history-holding"><strong>积存金 · {{ money(history.snapshot.gold.value) }}</strong><small>起始 {{ history.snapshot.gold.coverageFrom ?? '待确认' }} · {{ formatQuantity(history.snapshot.gold.quantity) }} 克 · 成本 {{ money(history.snapshot.gold.cost) }}</small><small v-if="history.snapshot.gold.pendingArrivalAmount > 0">卖出待到账 {{ money(history.snapshot.gold.pendingArrivalAmount) }}</small><small>行情 {{ history.snapshot.gold.priceDate ?? '未取得' }} · {{ history.snapshot.gold.priceSource === 'manual' ? '手动录入' : history.snapshot.gold.priceSource ?? '无依据' }}</small></div>
      </section>
      <div class="section-head"><h2>业务事实</h2><span class="meta">全部日期 · 含已撤销</span></div>
      <section class="history-card">
        <article v-for="fact in rows" :key="fact.id" class="history-fact" :class="{ reversed: fact.isReversed }">
          <div><strong>{{ title(fact) }}</strong><span v-if="fact.needsDateConfirmation" class="history-warning">日期待确认</span><span v-if="fact.isReversed"> · 已撤销</span></div>
          <p>{{ fact.date }} · 同日顺序 {{ fact.sequence }} · {{ money(fact.targetBalance ?? fact.amount) }}<template v-if="fact.quantity !== null"> · {{ formatQuantity(fact.quantity) }} {{ fact.assetType === 'gold' ? '克' : '份' }}</template></p>
          <small v-if="fact.priceDate">行情有效日期 {{ fact.priceDate }} · 价格 {{ formatQuantity(fact.price) }}</small>
          <small v-if="fact.fee > 0">费用 {{ money(fact.fee) }}</small>
          <small v-if="fact.relatedCashAdjustmentId">已关联此前现金核对差额</small><small v-if="fact.note">{{ fact.note }}</small>
          <div v-if="!fact.isReversed" class="history-fact-actions"><button class="back-link" @click="edit(fact, 'correct')">{{ fact.needsDateConfirmation ? '确认原始日期' : '更正' }}</button><button v-if="fact.source !== 'cashOpening'" class="back-link" @click="edit(fact, 'reverse')">撤销</button></div>
        </article>
      </section>
      <details class="history-card"><summary>行情来源与修订记录（{{ history.prices.length }}）</summary><p v-for="price in history.prices" :key="price.id" class="form-note">{{ price.effectiveDate }} · {{ price.assetType === 'Fund' ? '基金' : '积存金' }} · {{ formatQuantity(price.price) }} · {{ price.source === 'manual' ? '手动录入' : price.source }} · {{ price.isSuperseded ? '已被更正' : '有效记录' }}</p></details>
      <details class="history-card"><summary>更正审计（最近 {{ history.audit.length }} 条）</summary><details v-for="item in history.audit" :key="item.requestId" class="history-audit"><summary>{{ auditSummary(item.beforeJson) }} · {{ new Date(item.createdAt).toLocaleString('zh-CN') }}</summary><p class="form-note">保留更正请求、原始事实和行情快照</p><pre>{{ JSON.stringify(JSON.parse(item.beforeJson ?? '{}'), null, 2) }}</pre></details></details>
    </template>
    <BottomSheet :open="selected !== null" :title="draft.action === 'reverse' ? '撤销历史记录' : '更正历史记录'" subtitle="先预览完整历史影响，再确认保存" :close-disabled="busy" @close="selected = null">
      <form v-if="selected" class="form-stack" @submit.prevent="showPreview">
        <p>{{ title(selected) }} · 原业务日期 {{ selected.date }}</p>
        <template v-if="draft.action === 'correct'">
          <label class="form-field"><span>业务日期</span><input v-model="draft.date" type="date" :max="today" required /></label>
          <label v-if="draft.source !== 'cashOpening'" class="form-field"><span>同日业务顺序</span><input v-model.number="draft.sequence" type="number" min="1" step="1" required /><small>数字越小越先发生；须使用其他业务未占用的顺序。</small></label>
          <template v-if="draft.source === 'entry'"><label class="form-field"><span>收支方向</span><select v-model="draft.entryKind"><option value="income">收入</option><option value="expense">支出</option></select></label><label class="form-field"><span>分类</span><input v-model="draft.category" maxlength="20" required /></label></template>
          <label v-if="!isQuote && !isSellApply" class="form-field"><span>{{ draft.source === 'adjustment' ? '当时核对的实际余额（元）' : '金额 / 持仓成本（元）' }}</span><input v-model.number="draft.amount" type="number" min="0" step="0.01" :disabled="isConfirmedBuy || isSellArrival || isGoldSellArrival" required /><small v-if="isConfirmedBuy">确认金额由对应的待确认记录决定；修改扣款金额请先撤销确认。</small><small v-else-if="isSellArrival">到账金额必须与待到账金额一致；如有差额，请先更正赎回确认。</small><small v-else-if="isGoldSellArrival">到账金额必须与待到账金额一致；如有差额，请先更正卖出记录。</small></label>
          <label v-if="hasQuantity" class="form-field"><span>份额 / 克数</span><input v-model.number="draft.quantity" type="number" min="0" step="0.0001" required /></label>
          <label v-if="isConfirmed" class="form-field"><span>费用（元）</span><input v-model.number="draft.fee" type="number" min="0" step="0.01" /><small>费用从成交金额中列示，不产生额外现金变动。</small></label>
          <template v-if="selected.price !== null"><label class="form-field"><span>价格（选填，行情更新必填）</span><input v-model.number="draft.price" type="number" min="0.0001" step="0.0001" :required="isQuote" /></label><label v-if="!isQuote" class="form-field"><span>行情有效日期（有价格时必填）</span><input v-model="draft.priceDate" type="date" :max="today" /></label></template>
          <label class="form-field"><span>备注</span><input v-model="draft.note" maxlength="100" /></label>
          <CashAdjustmentLink v-if="canLink" v-model="draft.relatedCashAdjustmentId" :business-date="draft.date" :cash-effect="draft.source === 'entry' ? (draft.entryKind === 'income' ? draft.amount : -draft.amount) : Math.sign(selected.cashEffect) * draft.amount" :disabled="busy" />
        </template>
        <label class="form-field"><span>变更原因</span><input v-model="draft.reason" maxlength="200" required /></label>
        <p v-if="formError" class="form-error" role="alert">{{ formError }}</p>
        <button type="submit" class="secondary-button" :disabled="busy">预览影响</button>
      </form>
      <section v-if="preview" class="history-preview" aria-label="历史变更影响预览">
        <h3>截至今天的影响</h3><table><thead><tr><th>项目</th><th>变更前</th><th>变更后</th></tr></thead><tbody><tr v-for="row in impactRows" :key="row.key" :class="{ changed: row.before !== row.after }"><td>{{ row.label }}</td><td>{{ row.before }}</td><td>{{ row.after }}</td></tr></tbody></table>
        <p class="form-note">相关后续余额、持仓成本、已实现收益及核对差额会一起重算。若账本有新变动，需要重新预览。</p>
        <button type="button" class="primary-button" :disabled="busy" @click="confirm">确认保存历史变更</button>
      </section>
    </BottomSheet>
    <AppNav />
  </main>
</template>

<style scoped>
.history-header { display:flex;align-items:center;gap:12px;margin-bottom:18px; }
.history-header h1 { font-size:21px;margin:0; }
.history-date { display:flex;gap:12px;align-items:end;margin:20px 0; }
.history-date label { flex:1; }
.history-card { padding:18px;margin:14px 0;background:var(--color-bg-surface);border:1px solid var(--color-border-default);border-radius:16px; }
.history-card h2 { margin:10px 0; }
.history-holding { display:grid;gap:6px;padding:14px 0;border-top:1px solid var(--color-border-default); }
.history-holding small,.history-fact small { display:block;color:var(--color-text-secondary);font-size:12px;line-height:1.6; }
.coverage-issues { font-size:12px;line-height:1.8;padding-left:18px;color:var(--color-text-secondary); }
.history-fact { padding:14px 0;border-bottom:1px solid var(--color-border-default); }
.history-fact:last-child { border:0; }
.history-fact p { font-size:12px;color:var(--color-text-secondary);margin:8px 0; }
.history-warning { display:inline-block;margin-left:8px;padding:3px 6px;border:1px solid currentColor;border-radius:5px;color:var(--color-brand);font-size:11px; }
.history-fact-actions { display:flex;gap:22px;margin-top:10px; }
.reversed { opacity:.65; }
.history-preview table { width:100%;border-collapse:collapse;font-size:12px; }
.history-preview th,.history-preview td { padding:10px 4px;border-bottom:1px solid var(--color-border-default);text-align:right; }
.history-preview th:first-child,.history-preview td:first-child { text-align:left; }
.changed td:last-child { color:var(--color-brand);font-weight:600; }
.history-preview .primary-button { width:100%; }
.history-audit { margin-top:16px;font-size:12px; }
.history-audit pre { overflow:auto;max-height:320px;font-size:11px; }
</style>
