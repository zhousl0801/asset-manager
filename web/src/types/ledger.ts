export interface AssetOverview {
  isConfigured: boolean
  amount: number | null
  note: string | null
  notHeld: boolean
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
  fundNotHeld: boolean
  providentFundNotHeld: boolean
  goldNotHeld: boolean
  coverageConfirmed: boolean
  coverageComplete: boolean
  totalConfirmed: boolean
}

export type AssetKey = 'cash' | 'providentFund' | 'fund' | 'gold'

export interface AssetChange {
  assetType: AssetKey
  change: number | null
  isNew: boolean
}

export interface MonthChange {
  from: string
  to: string
  totalChange: number | null
  livingSurplus: number
  providentDeposit: number
  investmentProfit: number | null
  reconciliationDifference: number
  isComplete: boolean
  notes: string[]
  assetChanges: AssetChange[]
  comparableChange: number | null
  comparableBase: number | null
  addedRecords: number | null
  marketValueChange: number | null
  netDeposit: number | null
  hasComparableData: boolean
}

export interface UpdateAssetCoverageInput {
  fundNotHeld: boolean
  providentFundNotHeld: boolean
  goldNotHeld: boolean
  coverageConfirmed: boolean
}

export interface InitializeLedgerInput {
  cashAsOfDate?: string
  startDate: string
  cashBalance: number
}
