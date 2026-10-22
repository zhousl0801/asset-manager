export type ProvidentActivityType = 'opening' | 'deposit' | 'withdrawal'

export interface ProvidentActivity {
  id: string
  type: ProvidentActivityType
  amount: number
  balanceAfter: number
  businessDate: string
  note: string | null
  createdAt: string
}

export interface ProvidentOverview {
  periodFrom: string
  periodTo: string
  isConfigured: boolean
  balance: number
  asOfDate: string
  monthDeposit: number
  monthWithdrawal: number
  netChange: number
  activities: ProvidentActivity[]
}

export interface ProvidentMutationResult {
  balance: number
  activity: ProvidentActivity
}

export interface ProvidentRecordInput {
  relatedCashAdjustmentId?: string | null
  amount: number
  businessDate: string
  note: string
  requestId: string
}
