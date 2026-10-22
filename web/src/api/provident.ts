import type {
  ProvidentMutationResult,
  ProvidentOverview,
  ProvidentRecordInput
} from '../types/provident'
import { apiRequest } from './client'

export function fetchProvidentOverview(): Promise<ProvidentOverview> {
  return apiRequest<ProvidentOverview>('/api/ledger/provident')
}

export function depositProvident(input: ProvidentRecordInput): Promise<ProvidentMutationResult> {
  return apiRequest<ProvidentMutationResult>('/api/ledger/provident/deposits', {
    method: 'POST',
    body: JSON.stringify(input)
  })
}

export function withdrawProvident(input: ProvidentRecordInput): Promise<ProvidentMutationResult> {
  return apiRequest<ProvidentMutationResult>('/api/ledger/provident/withdrawals', {
    method: 'POST',
    body: JSON.stringify(input)
  })
}

export function setProvidentOpening(input: { balance: number; businessDate: string; note: string; requestId: string }): Promise<ProvidentMutationResult> {
  return apiRequest('/api/ledger/provident/opening', { method: 'POST', body: JSON.stringify(input) })
}
