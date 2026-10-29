import type { EntryKind } from '../types/entry'
import type { CreateCategoryInput, EntryCategory } from '../types/category'
import { apiRequest, readOnlyRequest } from './client'

export function fetchCategories(): Promise<EntryCategory[]> {
  return readOnlyRequest<EntryCategory[]>('/api/ledger/categories')
}

export function createCategory(input: CreateCategoryInput): Promise<EntryCategory> {
  return apiRequest<EntryCategory>('/api/ledger/categories', {
    method: 'POST',
    body: JSON.stringify(input)
  })
}

export function updateCategory(
  categoryId: string,
  input: { name: string; isActive: boolean }
): Promise<EntryCategory> {
  return apiRequest<EntryCategory>(`/api/ledger/categories/${encodeURIComponent(categoryId)}`, {
    method: 'PUT',
    body: JSON.stringify(input)
  })
}

export function reorderCategories(kind: EntryKind, ids: string[]): Promise<void> {
  return apiRequest<void>('/api/ledger/categories/order', {
    method: 'PUT',
    body: JSON.stringify({ kind, ids })
  })
}

export function deleteCategory(categoryId: string): Promise<void> {
  return apiRequest<void>(`/api/ledger/categories/${encodeURIComponent(categoryId)}`, {
    method: 'DELETE'
  })
}
