import type {
  CreateEntryInput,
  EntryKind,
  EntryMutationResult,
  EntryPeriod,
  EntryPeriodSummary,
  EntryStats,
  LedgerEntry,
  UpdateEntryInput
} from '../types/entry'
import { apiRequest } from './client'

export interface EntryPeriodQuery {
  period: EntryPeriod
  anchor: string
  kind?: EntryKind | null
  category?: string
  search?: string
  page?: number
  pageSize?: number
}

export function fetchEntryPeriod(query: EntryPeriodQuery): Promise<EntryPeriodSummary> {
  const params = new URLSearchParams({
    period: query.period,
    anchor: query.anchor,
    page: String(query.page ?? 1),
    pageSize: String(query.pageSize ?? 100)
  })

  if (query.kind) {
    params.set('kind', query.kind)
  }

  if (query.category) {
    params.set('category', query.category)
  }

  if (query.search) {
    params.set('search', query.search)
  }

  return apiRequest<EntryPeriodSummary>(`/api/ledger/entries?${params.toString()}`)
}

export function fetchEntryStats(period: EntryPeriod, anchor: string): Promise<EntryStats> {
  const params = new URLSearchParams({
    period,
    anchor
  })

  return apiRequest<EntryStats>(`/api/ledger/entries/stats?${params.toString()}`)
}

export function fetchEntry(entryId: string): Promise<LedgerEntry> {
  return apiRequest<LedgerEntry>(`/api/ledger/entries/${entryId}`)
}

export function createEntry(input: CreateEntryInput): Promise<EntryMutationResult> {
  return apiRequest<EntryMutationResult>('/api/ledger/entries', {
    method: 'POST',
    body: JSON.stringify(input)
  })
}

export function updateEntry(entryId: string, input: UpdateEntryInput): Promise<EntryMutationResult> {
  return apiRequest<EntryMutationResult>(`/api/ledger/entries/${entryId}`, {
    method: 'PUT',
    body: JSON.stringify(input)
  })
}

export function reverseEntry(entryId: string, reason: string, requestId: string): Promise<EntryMutationResult> {
  return apiRequest<EntryMutationResult>(`/api/ledger/entries/${entryId}/reverse`, {
    method: 'POST',
    body: JSON.stringify({ reason, requestId })
  })
}
