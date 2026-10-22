import type { InitializeLedgerInput, LedgerOverview } from '../types/ledger'
import { apiRequest } from './client'

export function fetchLedger(): Promise<LedgerOverview> {
  return apiRequest<LedgerOverview>('/api/ledger')
}

export function initializeLedger(input: InitializeLedgerInput): Promise<LedgerOverview> {
  return apiRequest<LedgerOverview>('/api/ledger/initialize', {
    method: 'POST',
    body: JSON.stringify(input)
  })
}
