import type { CashOverview, CashReconcileResult, ReconcileCashInput } from '../types/cash'
import { apiRequest } from './client'

export function fetchCashOverview(): Promise<CashOverview> {
  return apiRequest<CashOverview>('/api/ledger/cash')
}

export function reconcileCash(input: ReconcileCashInput): Promise<CashReconcileResult> {
  return apiRequest<CashReconcileResult>('/api/ledger/cash/reconcile', {
    method: 'POST',
    body: JSON.stringify(input)
  })
}
