export type PlanType = 'salaryIncome' | 'providentDeposit' | 'providentWithdrawal' | 'fundSip'

export type PlanCycle = 'monthly' | 'weekly'

export type OccurrenceStatus = 'pending' | 'completed' | 'waiting' | 'failed' | 'skipped'

export interface AutoPlan {
  id: string
  type: PlanType
  name: string
  amount: number
  cycle: PlanCycle
  dayOfMonth: number | null
  dayOfWeek: number | null
  time: string
  startDate: string
  generateFrom: string
  isActive: boolean
  fundPositionId: string | null
  fundPositionName: string | null
  nextDueDate: string | null
  version: number
}

export interface PlanOccurrence {
  id: string
  planId: string
  planName: string
  type: PlanType
  amount: number
  dueDate: string
  dueTime: string
  status: OccurrenceStatus
  relatedRecordId: string | null
  errorMessage: string | null
  executedAt: string | null
  periodKey: string
  planVersion: number
}

export interface PlanOverview {
  plans: AutoPlan[]
  pendingOccurrences: PlanOccurrence[]
  recentOccurrences: PlanOccurrence[]
  activeCount: number
  nextDueDate: string | null
  cashBalance: number
  providentBalance: number
}

export interface SavePlanInput {
  id?: string | null
  type: PlanType
  name: string
  amount: number
  cycle: PlanCycle
  dayOfMonth: number | null
  dayOfWeek: number | null
  time: string
  startDate: string
  fundPositionId: string | null
  requestId: string
  expectedVersion?: number | null
}

export interface PlanPreviewRow {
  planId: string | null
  planName: string
  type: PlanType
  amount: number
  status: string
  cashAfter: number
  providentAfter: number
  note: string | null
}

export interface PlanPreview {
  date: string
  trialCashStart: number
  trialCashEnd: number
  providentStart: number
  providentEnd: number
  rows: PlanPreviewRow[]
  notes: string[]
}

export interface PlanRunResult {
  createdOccurrences: number
  completed: number
  waiting: number
  failed: number
}
