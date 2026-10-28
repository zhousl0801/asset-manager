<script setup lang="ts">
import CashAdjustmentLink from '../components/CashAdjustmentLink.vue'
import { computed, onBeforeUnmount, onMounted, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { createEntry, fetchEntry, reverseEntry, updateEntry } from '../api/entries'
import { fetchCategories } from '../api/categories'
import { ApiError, getApiErrorMessage } from '../api/client'
import AmountKeypad from '../components/AmountKeypad.vue'
import AppIcon from '../components/AppIcon.vue'
import BackButton from '../components/BackButton.vue'
import { categoriesFor } from '../constants/categories'
import type { EntryCategory } from '../types/category'
import { applyAmountKey, evaluateAmountExpression, type AmountKey } from '../utils/amount'
import { todayInBusinessTimeZone } from '../utils/format'
import { createRequestId } from '../utils/request'
import type { EntryKind, EntryStatus, LedgerEntry } from '../types/entry'

const route = useRoute()
const router = useRouter()
const today = todayInBusinessTimeZone()
const entryId = computed(() => (typeof route.query.id === 'string' ? route.query.id : null))
const isEditing = computed(() => entryId.value !== null)
const returnRoute = computed(() =>
  route.query.returnTo === 'cash' ? { name: 'cash' } : { name: 'ledger' })
const entryStatus = ref<EntryStatus>('active')
const entrySource = ref<LedgerEntry['source']>('manual')
const dateInput = ref<HTMLInputElement | null>(null)
const requestId = ref(createRequestId())
const form = reactive({
  kind: 'expense' as EntryKind,
  category: '餐饮',
  expression: '',
  businessDate: today,
  note: '',
  relatedCashAdjustmentId: null as string | null
})
const isLoading = ref(false)
const isSaving = ref(false)
const isReversing = ref(false)
const loadError = ref('')
const formError = ref('')
const showReverse = ref(false)
const reverseReason = ref('')

const categoryItems = ref<EntryCategory[]>([])
const categories = computed(() => {
  const active = categoryItems.value
    .filter((category) => category.kind === form.kind && category.isActive)
    .sort((first, second) => first.sortOrder - second.sortOrder || first.name.localeCompare(second.name))
    .map((category) => category.name)
  const names = active.length ? active : categoriesFor(form.kind)

  if (form.category && !names.includes(form.category)) {
    return [form.category, ...names]
  }

  return names
})
const amount = computed(() => evaluateAmountExpression(form.expression))
const canEdit = computed(() => entryStatus.value === 'active')
const canSubmit = computed(() => amount.value !== null && !isSaving.value && canEdit.value)
const dateLabel = computed(() => {
  if (!form.businessDate) {
    return '日期'
  }

  return form.businessDate === today ? '今天' : form.businessDate.slice(5).replace('-', '/')
})

onMounted(async () => {
  document.addEventListener('keydown', handleKeydown)
  await loadCategories()

  if (entryId.value) {
    await loadEntry()
  } else {
    applyKindPreset()
  }
})

async function loadCategories(): Promise<void> {
  try {
    categoryItems.value = await fetchCategories()
  } catch {
    categoryItems.value = []
  }
}

function defaultCategory(kind: EntryKind): string {
  const first = categoryItems.value.find((category) => category.kind === kind && category.isActive)

  return first?.name ?? categoriesFor(kind)[0] ?? ''
}

onBeforeUnmount(() => {
  document.removeEventListener('keydown', handleKeydown)
})

async function loadEntry(): Promise<void> {
  if (!entryId.value) {
    return
  }

  isLoading.value = true
  loadError.value = ''

  try {
    applyEntry(await fetchEntry(entryId.value))
  } catch (error) {
    loadError.value = error instanceof ApiError && error.status === 404
      ? '记录不存在。'
      : getApiErrorMessage(error, '加载记录失败，请重试。')
  } finally {
    isLoading.value = false
  }
}

function applyKindPreset(): void {
  const requestedKind = route.query.kind

  if (requestedKind !== 'income' && requestedKind !== 'expense') {
    return
  }

  form.kind = requestedKind
  form.category = defaultCategory(requestedKind)
}

function applyEntry(entry: LedgerEntry): void {
  entryStatus.value = entry.status
  entrySource.value = entry.source
  form.kind = entry.kind
  form.category = entry.category
  form.expression = entry.amount.toFixed(2)
  form.businessDate = entry.businessDate
  form.note = entry.note ?? ''
  form.relatedCashAdjustmentId = entry.relatedCashAdjustmentId ?? null
}

function setKind(kind: EntryKind): void {
  if (!canEdit.value) {
    return
  }

  form.kind = kind

  if (!categories.value.includes(form.category)) {
    form.category = defaultCategory(kind)
  }
}

function chooseCategory(category: string): void {
  if (!canEdit.value) {
    return
  }

  form.category = category
}

function pickDate(): void {
  const input = dateInput.value

  if (!input) {
    return
  }

  input.focus()

  try {
    input.showPicker()
  } catch {
    // 浏览器不支持或不在用户手势中时，保留聚焦即可。
  }
}

async function submit(): Promise<void> {
  if (amount.value === null || isSaving.value || !canEdit.value) {
    return
  }

  formError.value = ''

  if (!form.businessDate) {
    formError.value = '请选择业务日期。'
    return
  }

  if (form.businessDate > today) {
    formError.value = '业务日期不能晚于今天。'
    return
  }

  if (!form.category) {
    formError.value = '请选择分类。'
    return
  }

  isSaving.value = true

  try {
    if (entryId.value) {
      await updateEntry(entryId.value, {
        requestId: requestId.value,
        kind: form.kind,
        category: form.category,
        relatedCashAdjustmentId: form.relatedCashAdjustmentId,
        amount: amount.value,
        businessDate: form.businessDate,
        note: form.note.trim()
      })
    } else {
      await createEntry({
        kind: form.kind,
        category: form.category,
        relatedCashAdjustmentId: form.relatedCashAdjustmentId,
        amount: amount.value,
        businessDate: form.businessDate,
        note: form.note.trim(),
        requestId: requestId.value
      })
    }

    await router.replace(returnRoute.value)
  } catch (error) {
    formError.value = getApiErrorMessage(error, '保存失败，请重试。')
  } finally {
    isSaving.value = false
  }
}

async function confirmReverse(): Promise<void> {
  if (!entryId.value || isReversing.value) {
    return
  }

  isReversing.value = true
  formError.value = ''

  try {
    await reverseEntry(entryId.value, reverseReason.value.trim(), requestId.value)
    await router.replace(returnRoute.value)
  } catch (error) {
    formError.value = getApiErrorMessage(error, '撤销失败，请重试。')
  } finally {
    isReversing.value = false
  }
}

function goBack(): void {
  const previous = (router.options.history.state as { back?: string | null }).back

  if (previous) {
    router.back()
    return
  }

  void router.push(returnRoute.value)
}

function handleKeydown(event: KeyboardEvent): void {
  if (event.metaKey || event.ctrlKey || event.altKey) {
    return
  }

  const target = event.target as HTMLElement | null

  if (target?.closest('input, textarea, select')) {
    return
  }

  const isInteractiveFocused = Boolean(target?.closest('button, summary, a'))

  if (/^[0-9.+-]$/.test(event.key)) {
    event.preventDefault()

    if (canEdit.value) {
      form.expression = applyAmountKey(form.expression, event.key as AmountKey)
    }

    return
  }

  if (event.key === 'Backspace') {
    event.preventDefault()

    if (canEdit.value) {
      form.expression = applyAmountKey(form.expression, 'back')
    }

    return
  }

  if (event.key === 'Enter') {
    if (isInteractiveFocused) {
      return
    }

    event.preventDefault()
    void submit()
    return
  }

  if (event.key === 'Escape') {
    event.preventDefault()
    goBack()
  }
}

</script>

<template>
  <main class="entry-screen">
    <header class="entry-header">
      <BackButton @back="goBack" />
      <div class="kind-tabs" role="tablist" aria-label="收支方向">
        <button
          type="button"
          role="tab"
          :aria-selected="form.kind === 'expense'"
          :class="{ active: form.kind === 'expense' }"
          :disabled="!canEdit"
          @click="setKind('expense')"
        >
          支出
        </button>
        <button
          type="button"
          role="tab"
          :aria-selected="form.kind === 'income'"
          :class="{ active: form.kind === 'income' }"
          :disabled="!canEdit"
          @click="setKind('income')"
        >
          收入
        </button>
      </div>
      <span class="header-spacer"></span>
    </header>

    <RouterLink v-if="form.kind === 'income' && !isEditing" class="automatic-income-link" :to="{ name: 'plans', query: { new: 'salaryIncome' } }">
      设置自动收入（工资、奖金等） ›
    </RouterLink>

    <p v-if="isLoading" class="state entry-state">正在读取记录…</p>

    <section v-else-if="loadError" class="state entry-state">
      <p>{{ loadError }}</p>
      <button type="button" class="secondary-button" @click="goBack">返回账本</button>
    </section>

    <template v-else>
      <div class="entry-body">
        <div v-if="entryStatus === 'reversed'" class="reversed-banner">
          该记录已撤销，不能修改。
        </div>

        <div class="category-grid" role="group" aria-label="选择分类">
          <button
            v-for="category in categories"
            :key="category"
            type="button"
            class="category-tile"
            :class="{ active: form.category === category }"
            :disabled="!canEdit"
            :aria-pressed="form.category === category"
            @click="chooseCategory(category)"
          >
            <span class="category-mark"><AppIcon :name="category" /></span>
            <span>{{ category }}</span>
          </button>
        </div>

        <div class="entry-fields">
          <label class="field">
            <span>日期</span>
            <input
              id="entry-date"
              ref="dateInput"
              v-model="form.businessDate"
              type="date"
              :max="today"
              :disabled="!canEdit"
            />
          </label>
          <label class="field">
            <span>备注</span>
            <input
              v-model="form.note"
              type="text"
              maxlength="200"
              placeholder="选填，记录用途细节"
              :disabled="!canEdit"
            />
          </label>
        </div>

        <details class="entry-options ui-disclosure" :open="!!form.relatedCashAdjustmentId">
          <summary>更多选项</summary>
        <CashAdjustmentLink v-model="form.relatedCashAdjustmentId" :business-date="form.businessDate" :cash-effect="form.kind === 'income' ? (amount ?? 0) : -(amount ?? 0)" :disabled="isSaving || !canEdit" />
        </details>
        <button
          v-if="isEditing && canEdit"
          type="button"
          class="reverse-link"
          @click="showReverse = true"
        >
          撤销这笔记录
        </button>

        <section v-if="showReverse" class="reverse-panel">
          <p>撤销会恢复这笔记录对现金的影响，并保留原始记录。</p>
          <label class="field">
            <span>撤销原因</span>
            <input v-model="reverseReason" type="text" maxlength="200" placeholder="选填" />
          </label>
          <div class="reverse-actions">
            <button
              type="button"
              class="secondary-button"
              :disabled="isReversing"
              @click="showReverse = false"
            >
              取消
            </button>
            <button
              type="button"
              class="danger-button"
              :disabled="isReversing"
              @click="confirmReverse"
            >
              {{ isReversing ? '撤销中…' : '确认撤销' }}
            </button>
          </div>
        </section>

        <p v-if="formError" class="form-error" role="alert">{{ formError }}</p>
        <p v-if="entrySource === 'automatic'" class="source-note">自动记账记录</p>
      </div>

      <footer class="entry-footer">
        <div class="amount-line">
          <span class="amount-category">{{ form.category || '未选择分类' }}</span>
          <output class="amount-expression" aria-label="输入金额">{{ form.expression || '0' }}</output>
        </div>
        <AmountKeypad
          v-model="form.expression"
          :can-submit="canSubmit"
          :disabled="isSaving || !canEdit"
          :date-label="dateLabel"
          @submit="submit"
          @pick-date="pickDate"
        />
      </footer>
    </template>
  </main>
</template>

<style scoped>
.entry-screen {
  display: flex;
  flex-direction: column;
  box-sizing: border-box;
  width: min(100%, 430px);
  height: 100dvh;
  margin: 0 auto;
  background: var(--color-bg-page);
}

.entry-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
  padding: max(12px, env(safe-area-inset-top)) 16px 12px;
  background: var(--color-bg-surface);
  border-bottom: 1px solid var(--color-border-subtle);
}

.kind-tabs {
  display: flex;
  gap: 4px;
  padding: 3px;
  border-radius: var(--radius-control);
  background: var(--color-bg-subtle);
}

.kind-tabs button {
  min-height: var(--touch-target);
  padding: 0 16px;
  border: 0;
  border-radius: var(--radius-tag);
  background: transparent;
  color: var(--color-text-secondary);
  font-size: 13px;
}

.kind-tabs button.active {
  background: var(--color-bg-surface);
  color: var(--color-brand);
  font-weight: 600;
  box-shadow: 0 1px 3px var(--color-shadow);
}

.kind-tabs button:disabled {
  background: transparent;
  color: var(--color-text-disabled);
}

.header-spacer {
  width: 38px;
}

.entry-body {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  padding: 16px 16px 20px;
}

.category-grid {
  display: grid;
  grid-template-columns: repeat(5, minmax(0, 1fr));
  gap: 8px 4px;
}

.category-tile {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 6px;
  min-height: 76px;
  padding: 8px 2px;
  border: 1px solid transparent;
  border-radius: var(--radius-control);
  background: transparent;
  color: var(--color-text-secondary);
  font-size: var(--text-footnote);
}

.category-tile:hover:not(:disabled) {
  background: var(--color-bg-hover);
}

.category-tile.active {
  border-color: transparent;
  background: var(--color-brand-soft);
  color: var(--color-brand);
}

.category-mark {
  display: inline-flex;
  justify-content: center;
  align-items: center;
  width: 38px;
  height: 38px;
  border-radius: 50%;
  background: var(--color-bg-surface);
  color: var(--color-brand);
  font-size: 14px;
  font-weight: 500;
}

.category-tile.active .category-mark {
  background: var(--color-brand);
  color: var(--color-on-brand);
}

.category-tile:disabled .category-mark {
  background: var(--color-bg-disabled);
  color: var(--color-text-disabled);
}

.entry-options {
  margin-top: 8px;
  border-top: 1px solid var(--color-border-subtle);
}
.entry-options .back-link {
  font-size: 12px;
}

.entry-fields {
  display: flex;
  flex-direction: column;
  gap: 10px;
  margin-top: 20px;
}

.field {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
  padding: 10px 0;
  border-bottom: 1px solid var(--color-border-subtle);
  color: var(--color-text-secondary);
  font-size: 12px;
}

.field input {
  flex: 1;
  min-width: 0;
  max-width: 240px;
  padding: 6px 0;
  border: 0;
  background: transparent;
  color: var(--color-text-primary);
  font-size: 14px;
  text-align: right;
}

.reversed-banner {
  margin-bottom: 12px;
  padding: 10px 12px;
  border-radius: var(--radius-tag);
  background: var(--color-warning-bg);
  color: var(--color-warning);
  font-size: 12px;
  line-height: 1.7;
}

.reverse-link {
  margin-top: 20px;
  padding: 0;
  border: 0;
  background: transparent;
  color: var(--color-danger);
  font-size: 13px;
}

.reverse-panel {
  margin-top: 12px;
  padding: 14px;
  border: 1px solid var(--color-border-subtle);
  border-radius: var(--radius-control);
  background: var(--color-bg-surface);
}

.reverse-panel p {
  margin: 0 0 8px;
  color: var(--color-text-secondary);
  font-size: var(--text-footnote);
  line-height: 1.7;
}

.reverse-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  margin-top: 12px;
}

.danger-button {
  min-height: var(--touch-target);
  padding: 0 16px;
  border: 0;
  border-radius: var(--radius-control);
  background: var(--color-danger);
  color: var(--color-on-brand);
  font-size: 13px;
}

.danger-button:hover:not(:disabled) {
  background: var(--color-danger-hover);
}

.danger-button:active:not(:disabled) {
  background: var(--color-danger-active);
}

.danger-button:disabled {
  background: var(--color-bg-disabled);
  color: var(--color-text-disabled);
}

.form-error {
  margin: 14px 0 0;
  padding: 10px 12px;
  border-radius: var(--radius-tag);
  background: var(--color-error-bg);
  color: var(--color-error);
  font-size: 12px;
  line-height: 1.7;
}

.source-note {
  margin: 12px 0 0;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
}

.entry-footer {
  background: var(--color-bg-surface);
  border-top: 1px solid var(--color-border-subtle);
}

.amount-line {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 12px;
  padding: 14px 18px 10px;
}

.amount-category {
  color: var(--color-text-secondary);
  font-size: 12px;
}

.amount-expression {
  color: var(--color-text-primary);
  font-size: 30px;
  font-weight: 500;
  letter-spacing: -0.8px;
  font-variant-numeric: tabular-nums;
}

.entry-state {
  padding: 24px 18px;
}
</style>
