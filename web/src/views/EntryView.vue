<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { createEntry, fetchEntry, reverseEntry, updateEntry } from '../api/entries'
import { createCategory, deleteCategory, fetchCategories } from '../api/categories'
import { ApiError, getApiErrorMessage } from '../api/client'
import AmountKeypad from '../components/AmountKeypad.vue'
import AppIcon from '../components/AppIcon.vue'
import BottomSheet from '../components/BottomSheet.vue'
import { categoriesFor } from '../constants/categories'
import type { EntryCategory } from '../types/category'
import { applyAmountKey, evaluateAmountExpression, type AmountKey } from '../utils/amount'
import { todayInBusinessTimeZone } from '../utils/format'
import { createRequestId } from '../utils/request'
import type { EntryKind, EntryStatus, LedgerEntry } from '../types/entry'

interface CategoryOption {
  id: string
  name: string
  deletable: boolean
}

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
const categoryEditMode = ref(false)
const categorySheet = ref<'add' | 'delete' | null>(null)
const pendingDeleteCategory = ref<CategoryOption | null>(null)
const newCategoryName = ref('')
const categorySheetError = ref('')
const isCategorySaving = ref(false)
const categoriesLoaded = ref(false)

const categoryItems = ref<EntryCategory[]>([])
const categoryOptions = computed<CategoryOption[]>(() => {
  const active = categoryItems.value
    .filter((category) => category.kind === form.kind && category.isActive)
    .sort((first, second) => first.sortOrder - second.sortOrder || first.name.localeCompare(second.name))

  if (active.length) {
    const options: CategoryOption[] = active.map((category) => ({
      id: category.id,
      name: category.name,
      deletable: category.systemKey !== 'salary'
    }))

    if (form.category && !options.some((option) => option.name === form.category)) {
      options.unshift({ id: `missing:${form.category}`, name: form.category, deletable: false })
    }

    return options
  }

  return categoriesFor(form.kind).map((name) => ({
    id: `fallback:${name}`,
    name,
    deletable: false
  }))
})
const canManageCategories = computed(() => categoriesLoaded.value && canEdit.value)
const hasEntryExtras = computed(() =>
  (isEditing.value && canEdit.value) || entrySource.value === 'automatic')
const categorySheetTitle = computed(() => categorySheet.value === 'add' ? '新增分类' : '删除分类')
const amount = computed(() => evaluateAmountExpression(form.expression))
const canEdit = computed(() => entryStatus.value === 'active')
const canSubmit = computed(() => amount.value !== null && !isSaving.value && canEdit.value)
const dateLabel = computed(() => {
  if (!form.businessDate) {
    return '日期'
  }

  return form.businessDate === today ? '今天' : form.businessDate.slice(5).replace('-', '/')
})

let longPressTimer: ReturnType<typeof setTimeout> | undefined
let suppressClickTimer: ReturnType<typeof setTimeout> | undefined
let longPressTriggered = false
let suppressNextCategoryClick = false

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
    categoriesLoaded.value = true
  } catch {
    categoryItems.value = []
    categoriesLoaded.value = false
  }
}

function defaultCategory(kind: EntryKind): string {
  const first = categoryItems.value.find((category) => category.kind === kind && category.isActive)

  return first?.name ?? categoriesFor(kind)[0] ?? ''
}

function categoryExists(kind: EntryKind, name: string): boolean {
  if (!categoriesLoaded.value) {
    return categoriesFor(kind).includes(name)
  }

  return categoryItems.value.some(
    (category) => category.kind === kind && category.isActive && category.name === name
  )
}

onBeforeUnmount(() => {
  clearLongPressTimer()

  if (suppressClickTimer !== undefined) {
    clearTimeout(suppressClickTimer)
  }

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
  categoryEditMode.value = false
  categorySheet.value = null
  pendingDeleteCategory.value = null

  if (!categoryExists(kind, form.category)) {
    form.category = defaultCategory(kind)
  }
}

function chooseCategory(category: string): void {
  if (!canEdit.value) {
    return
  }

  form.category = category
}

function clearLongPressTimer(): void {
  if (longPressTimer !== undefined) {
    clearTimeout(longPressTimer)
    longPressTimer = undefined
  }
}

function startCategoryPress(option: CategoryOption): void {
  clearLongPressTimer()

  if (!canManageCategories.value || option.id.startsWith('fallback:') || option.id.startsWith('missing:')) {
    return
  }

  longPressTriggered = false
  longPressTimer = setTimeout(() => {
    longPressTimer = undefined
    longPressTriggered = true
    categoryEditMode.value = true

    if (typeof navigator.vibrate === 'function') {
      navigator.vibrate(10)
    }
  }, 450)
}

function cancelCategoryPress(): void {
  clearLongPressTimer()

  if (!longPressTriggered) {
    return
  }

  longPressTriggered = false
  suppressNextCategoryClick = true

  if (suppressClickTimer !== undefined) {
    clearTimeout(suppressClickTimer)
  }

  suppressClickTimer = setTimeout(() => {
    suppressClickTimer = undefined
    suppressNextCategoryClick = false
  }, 600)
}

function handleCategoryClick(option: CategoryOption): void {
  if (suppressNextCategoryClick) {
    suppressNextCategoryClick = false

    if (suppressClickTimer !== undefined) {
      clearTimeout(suppressClickTimer)
      suppressClickTimer = undefined
    }

    return
  }

  if (categoryEditMode.value) {
    if (option.deletable) {
      openDeleteCategory(option)
    }

    return
  }

  chooseCategory(option.name)
}

function openAddCategory(): void {
  clearLongPressTimer()
  newCategoryName.value = ''
  categorySheetError.value = ''
  categorySheet.value = 'add'
}

function openDeleteCategory(option: CategoryOption): void {
  clearLongPressTimer()
  pendingDeleteCategory.value = option
  categorySheetError.value = ''
  categorySheet.value = 'delete'
}

function closeCategorySheet(): void {
  if (isCategorySaving.value) {
    return
  }

  categorySheet.value = null
  pendingDeleteCategory.value = null
}

function exitCategoryEdit(): void {
  categoryEditMode.value = false
}

async function saveCategory(): Promise<void> {
  const name = newCategoryName.value.trim()

  if (!name) {
    categorySheetError.value = '请输入分类名称。'
    return
  }

  if (name.length > 20) {
    categorySheetError.value = '分类名称不能超过 20 个字符。'
    return
  }

  isCategorySaving.value = true
  categorySheetError.value = ''

  try {
    const created = await createCategory({ kind: form.kind, name })
    await loadCategories()
    form.category = created.name
    categorySheet.value = null
    categoryEditMode.value = false
  } catch (error) {
    categorySheetError.value = error instanceof ApiError && error.status === 409
      ? '同类型下已存在同名分类。'
      : getApiErrorMessage(error, '新增分类失败，请重试。')
  } finally {
    isCategorySaving.value = false
  }
}

async function confirmDeleteCategory(): Promise<void> {
  const target = pendingDeleteCategory.value

  if (!target || isCategorySaving.value) {
    return
  }

  if (categoryItems.value.filter((category) => category.kind === form.kind && category.isActive).length <= 1) {
    categorySheetError.value = '至少保留一个分类。'
    return
  }

  isCategorySaving.value = true
  categorySheetError.value = ''

  try {
    await deleteCategory(target.id)
    await loadCategories()

    if (form.category === target.name) {
      form.category = defaultCategory(form.kind)
    }

    categorySheet.value = null
    pendingDeleteCategory.value = null
  } catch (error) {
    categorySheetError.value = getApiErrorMessage(error, '删除分类失败，请重试。')
  } finally {
    isCategorySaving.value = false
  }
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

function handleHeaderAction(): void {
  if (categoryEditMode.value) {
    exitCategoryEdit()
    return
  }

  goBack()
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
      <button type="button" class="header-cancel" @click="handleHeaderAction">
        {{ categoryEditMode ? '完成' : '取消' }}
      </button>
    </header>

    <p v-if="isLoading" class="state entry-state">正在读取记录…</p>

    <section v-else-if="loadError" class="state entry-state">
      <p>{{ loadError }}</p>
      <button type="button" class="secondary-button" @click="goBack">返回账本</button>
    </section>

    <template v-else>
      <div class="category-area">
        <div class="category-scroll">
          <div v-if="entryStatus === 'reversed'" class="reversed-banner">
            该记录已撤销，不能修改。
          </div>

          <div class="category-grid" role="group" aria-label="选择分类">
            <button
              v-for="option in categoryOptions"
              :key="option.id"
              type="button"
              class="category-tile"
              :class="{
                active: !categoryEditMode && form.category === option.name,
                editing: categoryEditMode
              }"
              :disabled="!canEdit"
              :aria-pressed="form.category === option.name"
              @pointerdown="startCategoryPress(option)"
              @pointerup="cancelCategoryPress"
              @pointerleave="cancelCategoryPress"
              @pointercancel="cancelCategoryPress"
              @click="handleCategoryClick(option)"
              @contextmenu.prevent
            >
              <span class="category-mark-wrap">
                <span class="category-mark"><AppIcon :name="option.name" /></span>
                <span
                  v-if="categoryEditMode && option.deletable"
                  class="category-remove"
                  aria-hidden="true"
                >×</span>
              </span>
              <span class="category-name">{{ option.name }}</span>
            </button>
            <button
              v-if="categoryEditMode && canManageCategories"
              type="button"
              class="category-tile category-add"
              @click="openAddCategory"
            >
              <span class="category-mark-wrap">
                <span class="category-mark"><AppIcon name="plus" /></span>
              </span>
              <span class="category-name">添加</span>
            </button>
          </div>
          <p v-if="categoryEditMode && canManageCategories" class="category-hint">
            点分类可删除
          </p>

          <div v-if="hasEntryExtras" class="category-extras">
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

            <p v-if="entrySource === 'automatic'" class="source-note">自动记账记录</p>
          </div>
        </div>
        <div class="category-fade" aria-hidden="true" />
      </div>

      <section class="amount-panel">
        <output class="amount-expression" aria-label="输入金额">{{ form.expression || '0.00' }}</output>
      </section>

      <p v-if="formError" class="form-error entry-error" role="alert">{{ formError }}</p>

      <label class="note-bar">
        <input
          v-model="form.note"
          type="text"
          maxlength="200"
          placeholder="备注：点击填写备注"
          :disabled="!canEdit"
          aria-label="备注"
        />
      </label>

      <input
        id="entry-date"
        ref="dateInput"
        v-model="form.businessDate"
        class="date-proxy"
        type="date"
        :max="today"
        :disabled="!canEdit"
        tabindex="-1"
        aria-label="业务日期"
      />

      <footer class="entry-footer">
        <AmountKeypad
          v-model="form.expression"
          :can-submit="canSubmit"
          :disabled="isSaving || !canEdit"
          :date-label="dateLabel"
          @submit="submit"
          @pick-date="pickDate"
        />
      </footer>

      <BottomSheet
        :open="categorySheet !== null"
        :title="categorySheetTitle"
        :subtitle="categorySheet === 'add' ? `新增后立即选中，只影响「${form.kind === 'income' ? '收入' : '支出'}」分类` : '历史记录仍会保留该分类名称'"
        :close-disabled="isCategorySaving"
        @close="closeCategorySheet"
      >
        <form v-if="categorySheet === 'add'" class="form-stack" @submit.prevent="saveCategory">
          <label class="form-field">
            <span>分类名称</span>
            <input v-model="newCategoryName" maxlength="20" placeholder="例如：咖啡" />
          </label>
          <p v-if="categorySheetError" class="form-error">{{ categorySheetError }}</p>
          <div class="form-actions">
            <button type="button" class="secondary-button" :disabled="isCategorySaving" @click="closeCategorySheet">取消</button>
            <button type="submit" class="primary-button" :disabled="isCategorySaving || !newCategoryName.trim()">
              {{ isCategorySaving ? '保存中…' : '保存' }}
            </button>
          </div>
        </form>

        <template v-else-if="categorySheet === 'delete' && pendingDeleteCategory">
          <p class="category-delete-note">
            删除「{{ pendingDeleteCategory.name }}」后，新记账不再显示该分类；已有记录仍保留原分类名称。
          </p>
          <p v-if="categorySheetError" class="form-error">{{ categorySheetError }}</p>
          <div class="form-actions">
            <button type="button" class="secondary-button" :disabled="isCategorySaving" @click="closeCategorySheet">取消</button>
            <button type="button" class="danger-button" :disabled="isCategorySaving" @click="confirmDeleteCategory">
              {{ isCategorySaving ? '删除中…' : '确认删除' }}
            </button>
          </div>
        </template>
      </BottomSheet>
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
  overflow: hidden;
  background: var(--color-bg-surface);
}

.entry-header {
  position: relative;
  display: flex;
  justify-content: center;
  align-items: center;
  flex-shrink: 0;
  min-height: calc(56px + env(safe-area-inset-top));
  padding: env(safe-area-inset-top) 72px 0;
  background: var(--color-brand);
  color: var(--color-on-brand);
}

.kind-tabs {
  display: flex;
  align-items: center;
  gap: 32px;
  height: 56px;
}

.kind-tabs button {
  position: relative;
  min-height: var(--touch-target);
  padding: 0 6px;
  border: 0;
  background: transparent;
  color: var(--color-on-brand);
  font-size: 1.0625rem;
  font-weight: 500;
  opacity: 0.66;
}

.kind-tabs button.active {
  font-weight: 600;
  opacity: 1;
}

.kind-tabs button.active::after {
  content: '';
  position: absolute;
  right: 0;
  bottom: 1px;
  left: 0;
  height: 2px;
  border-radius: var(--radius-pill);
  background: currentColor;
}

.kind-tabs button:disabled {
  opacity: 0.35;
}

.header-cancel {
  position: absolute;
  right: 16px;
  bottom: 0;
  min-height: 56px;
  padding: 0 4px;
  border: 0;
  background: transparent;
  color: var(--color-on-brand);
  font-size: var(--text-body);
  font-weight: 500;
}

.category-area {
  position: relative;
  display: flex;
  flex: 1;
  min-height: 0;
}

.category-scroll {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  padding: 10px 16px 24px;
  -webkit-overflow-scrolling: touch;
  scrollbar-width: none;
}

.category-scroll::-webkit-scrollbar {
  display: none;
}

.category-fade {
  position: absolute;
  right: 0;
  bottom: 0;
  left: 0;
  height: 24px;
  background: linear-gradient(to bottom, transparent, var(--color-bg-surface));
  pointer-events: none;
}

.category-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  row-gap: 16px;
  column-gap: 4px;
  padding: 6px 0 0;
}

.category-tile {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: flex-start;
  gap: 8px;
  min-width: 0;
  padding: 0;
  border: 0;
  background: transparent;
  color: var(--color-text-primary);
  font-size: 0.875rem;
  -webkit-touch-callout: none;
  user-select: none;
  touch-action: manipulation;
}

.category-mark-wrap {
  position: relative;
  display: inline-flex;
}

.category-mark {
  display: inline-flex;
  justify-content: center;
  align-items: center;
  flex: 0 0 auto;
  width: 58px;
  height: 58px;
  border-radius: 50%;
  background: var(--color-bg-subtle);
  color: var(--color-icon-default);
  transition: background var(--motion-fast) ease, color var(--motion-fast) ease;
}

.category-mark :deep(.app-icon) {
  width: 26px;
  height: 26px;
}

@media (hover: hover) and (pointer: fine) {
  .category-tile:hover:not(:disabled) .category-mark {
    background: var(--color-brand-soft);
    color: var(--color-brand);
  }
}

.category-tile.active .category-mark,
.category-tile.active:hover .category-mark,
.category-tile.active:active .category-mark {
  background: var(--color-brand);
  color: var(--color-on-brand);
}

.category-name {
  max-width: 100%;
  overflow: hidden;
  line-height: 1.2;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.category-tile.active .category-name {
  color: var(--color-brand);
  font-weight: 600;
}

.category-tile:disabled {
  opacity: 0.5;
}

.category-remove {
  position: absolute;
  top: -3px;
  right: -3px;
  display: inline-flex;
  justify-content: center;
  align-items: center;
  width: 22px;
  height: 22px;
  border-radius: 50%;
  background: var(--color-danger);
  color: var(--color-on-danger);
  font-size: 0.875rem;
  font-weight: 600;
  line-height: 1;
  box-shadow: 0 1px 3px var(--color-shadow);
}

.category-add .category-mark {
  border: 1px dashed var(--color-border-control);
  background: transparent;
  color: var(--color-brand);
}

.category-add .category-name {
  color: var(--color-brand);
}

.category-hint {
  margin: 12px 0 0;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
  text-align: center;
}

.category-extras {
  margin-top: 20px;
  padding-top: 14px;
  border-top: 1px solid var(--color-border-subtle);
}

.category-delete-note {
  margin: 0;
  color: var(--color-text-secondary);
  font-size: var(--text-footnote);
  line-height: 1.7;
}

.amount-panel {
  display: flex;
  justify-content: flex-end;
  flex-shrink: 0;
  padding: 10px 20px 4px;
  background: var(--color-bg-surface);
}

.amount-expression {
  max-width: 100%;
  overflow: hidden;
  color: var(--color-text-primary);
  font-size: clamp(2.75rem, 12vw, 3.25rem);
  font-weight: 500;
  letter-spacing: -1.4px;
  line-height: 1.1;
  font-variant-numeric: tabular-nums;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.note-bar {
  display: block;
  flex-shrink: 0;
  margin: 4px 16px 12px;
}

.note-bar input {
  box-sizing: border-box;
  width: 100%;
  min-height: 44px;
  padding: 0 14px;
  border: 0;
  border-radius: var(--radius-control);
  background: var(--color-bg-subtle);
  color: var(--color-text-primary);
  font-size: 1rem;
}

.note-bar input::placeholder {
  color: var(--color-text-secondary);
}

.note-bar input:focus-visible {
  outline: 2px solid var(--color-focus-ring);
  outline-offset: 1px;
}

.date-proxy {
  position: fixed;
  bottom: 0;
  left: 50%;
  width: 1px;
  height: 1px;
  padding: 0;
  border: 0;
  opacity: 0;
  pointer-events: none;
}

.field {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
  padding: 10px 0;
  border-bottom: 1px solid var(--color-border-subtle);
  color: var(--color-text-secondary);
  font-size: 0.75rem;
}

.field input {
  flex: 1;
  min-width: 0;
  max-width: 240px;
  padding: 6px 0;
  border: 0;
  background: transparent;
  color: var(--color-text-primary);
  font-size: 0.875rem;
  text-align: right;
}

.reversed-banner {
  margin-bottom: 12px;
  padding: 10px 12px;
  border-radius: var(--radius-tag);
  background: var(--color-warning-bg);
  color: var(--color-warning);
  font-size: 0.75rem;
  line-height: 1.7;
}

.reverse-link {
  margin-top: 0;
  padding: 0;
  border: 0;
  background: transparent;
  color: var(--color-danger);
  font-size: 0.8125rem;
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
  font-size: 0.8125rem;
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
  font-size: 0.75rem;
  line-height: 1.7;
}

.entry-error {
  margin: 0 16px 8px;
}

.source-note {
  margin: 10px 0 0;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
}

.entry-footer {
  flex-shrink: 0;
  padding-bottom: env(safe-area-inset-bottom);
  background: var(--color-bg-surface);
}

.entry-state {
  padding: 24px 18px;
}
</style>
