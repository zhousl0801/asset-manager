export interface AssetOverview {
  isConfigured: boolean
  amount: number | null
  note: string | null
}

export interface MonthSummary {
  income: number
  expense: number
  surplus: number
  hasRecords: boolean
}

export interface LedgerOverview {
  id: string
  currency: string
  timeZone: string
  startDate: string
  asOfDate: string
  isComplete: boolean
  totalAssets: number
  cash: AssetOverview
  fund: AssetOverview
  providentFund: AssetOverview
  gold: AssetOverview
  month: MonthSummary
}

export interface InitializeLedgerInput {
  cashAsOfDate?: string
  startDate: string
  cashBalance: number
}
