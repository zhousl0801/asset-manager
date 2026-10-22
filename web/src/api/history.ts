import { apiRequest, readOnlyRequest } from './client'
import type { HistoryChange, HistoryOverview, HistoryPreview } from '../types/history'

export function fetchHistory(asOf?: string): Promise<HistoryOverview> {
  return apiRequest(`/api/ledger/history${asOf ? `?asOf=${encodeURIComponent(asOf)}` : ''}`)
}

export function previewHistory(change: HistoryChange): Promise<HistoryPreview> {
  return readOnlyRequest('/api/ledger/history/preview', { method: 'POST', body: JSON.stringify(change) })
}

export function confirmHistory(change: HistoryChange, previewToken: string): Promise<HistoryPreview> {
  return apiRequest('/api/ledger/history/change', { method: 'POST', body: JSON.stringify({ change, previewToken }) })
}
