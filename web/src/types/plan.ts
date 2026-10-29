export type PlanType = 'salaryIncome' | 'providentDeposit' | 'providentWithdrawal' | 'fundSip'

export type PlanCycle = 'monthly' | 'weekly' | 'tradingDay'

export type OccurrenceStatus = 'pending' | 'completed' | 'waiting' | 'failed' | 'skipped'

export interface AutoPlan {
  id: string
  type: PlanType
  name: string
  amount: number
  fee?: number
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
  fundTarget?: FundPlanTarget | null
  upcomingDates?: string[]
}

export interface PlanOccurrence {
  id: string
  planId: string
  planName: string
  type: PlanType
  amount: number
  fee: number
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
  activeCount: number
  nextDueDate: string | null
  cashBalance: number
  providentBalance: number
}

export interface PlanOccurrencePage {
  items: PlanOccurrence[]
  page: number
  pageSize: number
  totalCount: number
}

export interface PlanCalendar {
  year: number
  month: number
  days: TradingDay[]
}

export interface SavePlanInput {
  id?: string | null
  type: PlanType
  name: string
  amount: number
  fee?: number
  cycle: PlanCycle
  dayOfMonth: number | null
  dayOfWeek: number | null
  time: string
  startDate: string
  fundPositionId: string | null
  requestId: string
  expectedVersion?: number | null
  fundTarget?: FundPlanTarget | null
}

export interface PlanRunResult {
  createdOccurrences: number
  completed: number
  waiting: number
  failed: number
}

export interface FundPlanTarget {
  name: string
  code: string | null
  shareClass: string | null
}

export interface MarketDay {
  market: string
  name: string
  year: number
  isOpen: boolean
  isHalfDay: boolean
  usesWeekdayFallback: boolean
  version: string | null
  sourceUrl: string | null
  publishedOn: string | null
  verifiedOn: string | null
}

export interface TradingDay {
  date: string
  isTradingDay: boolean
  markets: MarketDay[]
}
