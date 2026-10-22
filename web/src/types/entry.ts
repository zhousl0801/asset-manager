export type EntryKind = 'income' | 'expense'
export type EntrySource = 'manual' | 'automatic' | 'import'
export type EntryStatus = 'active' | 'reversed'
export type EntryPeriod = 'day' | 'month' | 'year'

export interface LedgerEntry {
  relatedCashAdjustmentId?: string | null
  id: string
  kind: EntryKind
  category: string
  amount: number
  businessDate: string
  note: string | null
  source: EntrySource
  status: EntryStatus
  createdAt: string
  updatedAt: string
  reversedAt: string | null
  reversalReason: string | null
}

export interface EntryPeriodSummary {
  page: number
  pageSize: number
  totalCount: number
  period: EntryPeriod
  from: string
  to: string
  income: number
  expense: number
  surplus: number
  entries: LedgerEntry[]
}

export interface EntryStatsPoint {
  date: string
  label: string
  income: number
  expense: number
}

export interface EntryStatsCategory {
  category: string
  amount: number
  count: number
  share: number
}

export interface EntryStats {
  period: EntryPeriod
  anchor: string
  from: string
  to: string
  coveredDays: number
  income: number
  expense: number
  incomeDailyAverage: number
  expenseDailyAverage: number
  points: EntryStatsPoint[]
  incomeCategories: EntryStatsCategory[]
  expenseCategories: EntryStatsCategory[]
  note: string | null
}

export interface EntryMutationResult {
  entry: LedgerEntry
  cashBalance: number
}

export interface CreateEntryInput {
  relatedCashAdjustmentId?: string | null
  kind: EntryKind
  category: string
  amount: number
  businessDate: string
  note: string
  requestId: string
}

export interface UpdateEntryInput {
  relatedCashAdjustmentId?: string | null
  requestId: string
  kind: EntryKind
  category: string
  amount: number
  businessDate: string
  note: string
}
