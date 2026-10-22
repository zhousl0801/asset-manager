import type { EntrySource } from './entry'

export type CashActivityType = 'daily' | 'assetTransfer' | 'adjustment'
export type LinkedAssetType = 'providentFund' | 'fund' | 'gold'

export interface CashActivity {
  id: string
  type: CashActivityType
  title: string
  note: string | null
  amount: number
  businessDate: string
  source: EntrySource | null
  assetType: LinkedAssetType | null
}

export interface CashOverview {
  version: number
  balance: number
  asOfDate: string
  from: string
  to: string
  openingBalance: number
  inflow: number
  outflow: number
  adjustmentTotal: number
  netChange: number
  activities: CashActivity[]
}

export interface CashAdjustment {
  id: string
  businessDate: string
  previousBalance: number
  newBalance: number
  difference: number
  reason: string
  createdAt: string
}

export interface CashReconcileResult {
  isBalanced: boolean
  difference: number
  cashBalance: number
  adjustment: CashAdjustment | null
}

export interface ReconcileCashInput {
  requestId: string
  expectedVersion: number
  actualBalance: number
  reason: string
}
