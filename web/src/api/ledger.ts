import type { InitializeLedgerInput, LedgerOverview } from '../types/ledger'
import type { AssetTrend, TrendRange } from '../types/trend'
import { apiRequest } from './client'

export function fetchLedger(): Promise<LedgerOverview> {
  return apiRequest<LedgerOverview>('/api/ledger')
}

export function fetchAssetTrend(range: TrendRange): Promise<AssetTrend> {
  return apiRequest<AssetTrend>(`/api/ledger/trend?range=${encodeURIComponent(range)}`)
}

export function initializeLedger(input: InitializeLedgerInput): Promise<LedgerOverview> {
  return apiRequest<LedgerOverview>('/api/ledger/initialize', {
    method: 'POST',
    body: JSON.stringify(input)
  })
}
