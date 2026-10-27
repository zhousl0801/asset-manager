<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { createCategory, fetchCategories, reorderCategories, updateCategory } from '../api/categories'
import { ApiError, getApiErrorMessage } from '../api/client'
import AppNav from '../components/AppNav.vue'
import BackButton from '../components/BackButton.vue'
import BottomSheet from '../components/BottomSheet.vue'
import type { EntryKind } from '../types/entry'
import type { EntryCategory } from '../types/category'

const router = useRouter()
const categories = ref<EntryCategory[]>([])
const activeKind = ref<EntryKind>('expense')
const isLoading = ref(false)
const errorMessage = ref('')
const notice = ref('')
const sheetOpen = ref(false)
const editing = ref<EntryCategory | null>(null)
const formName = ref('')
const formError = ref('')
const isSaving = ref(false)
const busyId = ref('')

const visibleCategories = computed(() =>
  categories.value
    .filter((category) => category.kind === activeKind.value)
    .sort((first, second) => first.sortOrder - second.sortOrder || first.name.localeCompare(second.name))
)

const sheetTitle = computed(() => editing.value ? '编辑分类' : '新增分类')

function goBack(): void {
  void router.push({ name: 'mine' })
}

function setKind(kind: EntryKind): void {
  activeKind.value = kind
  notice.value = ''
}

async function loadCategories(): Promise<void> {
  isLoading.value = true
  errorMessage.value = ''

  try {
    categories.value = await fetchCategories()
  } catch (error) {
    errorMessage.value = getApiErrorMessage(error, '分类暂时无法读取，请稍后重试。')
  } finally {
    isLoading.value = false
  }
}

function openCreate(): void {
  editing.value = null
  formName.value = ''
  formError.value = ''
  sheetOpen.value = true
}

function openEdit(category: EntryCategory): void {
  editing.value = category
  formName.value = category.name
  formError.value = ''
  sheetOpen.value = true
}

function closeSheet(): void {
  if (!isSaving.value) {
    sheetOpen.value = false
  }
}

async function saveCategory(): Promise<void> {
  const name = formName.value.trim()

  if (!name) {
    formError.value = '请输入分类名称。'
    return
  }

  isSaving.value = true
  formError.value = ''

  try {
    if (editing.value) {
      await updateCategory(editing.value.id, {
        name,
        isActive: editing.value.isActive
      })
      notice.value = '分类已更新，历史记录同步显示新名称。'
    } else {
      await createCategory({ kind: activeKind.value, name })
      notice.value = '分类已新增。'
    }

    sheetOpen.value = false
    await loadCategories()
  } catch (error) {
    formError.value = error instanceof ApiError && error.status === 409
      ? '同类型下已存在同名分类。'
      : getApiErrorMessage(error, '保存分类失败，请重试。')
  } finally {
    isSaving.value = false
  }
}

async function toggleActive(category: EntryCategory): Promise<void> {
  busyId.value = category.id
  notice.value = ''

  try {
    await updateCategory(category.id, {
      name: category.name,
      isActive: !category.isActive
    })
    notice.value = category.isActive ? '分类已停用，历史记录仍保留。' : '分类已启用。'
    await loadCategories()
  } catch (error) {
    errorMessage.value = getApiErrorMessage(error, '更新分类状态失败，请重试。')
  } finally {
    busyId.value = ''
  }
}

async function moveCategory(index: number, offset: number): Promise<void> {
  const items = [...visibleCategories.value]
  const target = index + offset

  if (target < 0 || target >= items.length) {
    return
  }

  const current = items[index]!
  items[index] = items[target]!
  items[target] = current
  busyId.value = current.id
  notice.value = ''

  try {
    await reorderCategories(activeKind.value, items.map((item) => item.id))
    categories.value = categories.value.map((category) => {
      const position = items.findIndex((item) => item.id === category.id)

      return position >= 0 && category.kind === activeKind.value
        ? { ...category, sortOrder: position }
        : category
    })
  } catch (error) {
    errorMessage.value = getApiErrorMessage(error, '调整顺序失败，请重试。')
  } finally {
    busyId.value = ''
  }
}

onMounted(loadCategories)
</script>

<template>
  <main class="page categories-page with-nav">
    <header class="categories-header">
      <BackButton @back="goBack" />
      <h1>收支分类</h1>
      <button type="button" class="add-button" @click="openCreate">＋ 新增</button>
    </header>

    <p class="page-note">分类改名会同步到全部历史记录；停用后新记账不再显示，历史记录仍保留并参与统计。</p>

    <div class="kind-tabs" role="tablist" aria-label="分类类型">
      <button
        type="button"
        :class="{ active: activeKind === 'expense' }"
        @click="setKind('expense')"
      >
        支出 {{ categories.filter((category) => category.kind === 'expense').length }}
      </button>
      <button
        type="button"
        :class="{ active: activeKind === 'income' }"
        @click="setKind('income')"
      >
        收入 {{ categories.filter((category) => category.kind === 'income').length }}
      </button>
    </div>

    <p v-if="notice" class="notice">{{ notice }}</p>
    <p v-if="isLoading && !categories.length" class="state">正在读取分类…</p>
    <section v-else-if="errorMessage" class="state">
      <p>{{ errorMessage }}</p>
      <button type="button" class="secondary-button" @click="loadCategories">重新加载</button>
    </section>

    <div v-else class="category-list">
      <div
        v-for="(category, index) in visibleCategories"
        :key="category.id"
        class="category-row"
        :class="{ inactive: !category.isActive }"
      >
        <div class="category-main">
          <span class="category-name">{{ category.name }}</span>
          <span v-if="!category.isActive" class="status-tag">已停用</span>
          <span v-if="category.systemKey === 'salary'" class="status-tag system">自动记账使用</span>
        </div>
        <div class="category-actions">
          <button
            type="button"
            class="icon-action"
            aria-label="上移"
            :disabled="index === 0 || busyId !== ''"
            @click="moveCategory(index, -1)"
          >
            ↑
          </button>
          <button
            type="button"
            class="icon-action"
            aria-label="下移"
            :disabled="index === visibleCategories.length - 1 || busyId !== ''"
            @click="moveCategory(index, 1)"
          >
            ↓
          </button>
          <button type="button" class="text-action" :disabled="busyId !== ''" @click="openEdit(category)">
            编辑
          </button>
          <button
            type="button"
            class="text-action"
            :disabled="busyId !== '' || category.systemKey === 'salary'"
            @click="toggleActive(category)"
          >
            {{ category.isActive ? '停用' : '启用' }}
          </button>
        </div>
      </div>
      <p v-if="!visibleCategories.length" class="state">还没有分类，先新增一个。</p>
    </div>

    <BottomSheet
      :open="sheetOpen"
      :title="sheetTitle"
      :subtitle="editing ? '改名会同步更新历史记录' : '新增后可在记一笔中选择'"
      :close-disabled="isSaving"
      @close="closeSheet"
    >
      <form class="form-stack" @submit.prevent="saveCategory">
        <label class="form-field">
          <span>分类名称</span>
          <input v-model="formName" maxlength="20" placeholder="例如：咖啡" />
        </label>
        <p v-if="formError" class="form-error">{{ formError }}</p>
        <div class="form-actions">
          <button type="button" class="secondary-button" :disabled="isSaving" @click="closeSheet">取消</button>
          <button type="submit" class="primary-button" :disabled="isSaving || !formName.trim()">
            {{ isSaving ? '保存中…' : '保存' }}
          </button>
        </div>
      </form>
    </BottomSheet>

    <AppNav />
  </main>
</template>

<style scoped>
.categories-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  margin-bottom: 14px;
}

.categories-header h1 {
  margin: 0;
  font-size: 19px;
  font-weight: 600;
  letter-spacing: -0.3px;
}

.add-button {
  min-height: 36px;
  padding: 0 4px;
  border: 0;
  background: transparent;
  color: var(--color-brand);
  font-size: 12px;
  font-weight: 550;
  text-decoration: none;
}

.page-note {
  margin: 0 0 14px;
  padding: 10px 12px;
  border-radius: 8px;
  background: var(--color-bg-subtle);
  color: var(--color-text-secondary);
  font-size: 10px;
  line-height: 1.7;
}

.kind-tabs {
  display: flex;
  gap: 8px;
  margin-bottom: 14px;
}

.kind-tabs button {
  min-height: 34px;
  padding: 0 14px;
  border: 1px solid var(--color-border-subtle);
  border-radius: 8px;
  background: var(--color-bg-surface);
  color: var(--color-text-secondary);
  font-size: 12px;
}

.kind-tabs button.active {
  border-color: var(--color-brand);
  background: var(--color-brand-soft);
  color: var(--color-brand);
  font-weight: 550;
}

.notice {
  margin: 0 0 12px;
  padding: 10px 12px;
  border-radius: 8px;
  background: var(--color-success-bg);
  color: var(--color-success);
  font-size: 11px;
  line-height: 1.6;
}

.category-list {
  overflow: hidden;
  border: 1px solid var(--color-border-subtle);
  border-radius: 12px;
  background: var(--color-bg-surface);
}

.category-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  box-sizing: border-box;
  min-height: 50px;
  padding: 8px 12px;
}

.category-row + .category-row {
  border-top: 1px solid var(--color-border-subtle);
}

.category-row.inactive .category-name {
  color: var(--color-text-secondary);
  text-decoration: line-through;
}

.category-main {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
  min-width: 0;
}

.category-name {
  font-size: 13px;
}

.status-tag {
  padding: 2px 5px;
  border-radius: 4px;
  background: var(--color-bg-subtle);
  color: var(--color-text-secondary);
  font-size: 9px;
}

.status-tag.system {
  background: var(--color-brand-soft);
  color: var(--color-brand);
}

.category-actions {
  display: flex;
  align-items: center;
  gap: 2px;
}

.icon-action,
.text-action {
  min-height: 30px;
  padding: 0 6px;
  border: 0;
  border-radius: 5px;
  background: transparent;
  color: var(--color-brand);
  font-size: 11px;
}

.icon-action {
  font-size: 13px;
}

.icon-action:disabled,
.text-action:disabled {
  color: var(--color-text-disabled);
}

.icon-action:hover:not(:disabled),
.text-action:hover:not(:disabled) {
  background: var(--color-bg-hover);
}

@media (max-width: 360px) {
  .category-actions {
    gap: 0;
  }

  .icon-action,
  .text-action {
    padding: 0 4px;
  }
}
</style>
