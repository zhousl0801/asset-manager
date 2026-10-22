import type { EntryKind } from './entry'

export interface BusinessFact {
  id: string
  source: 'cashOpening' | 'entry' | 'asset' | 'adjustment'
  assetType: string | null
  positionId: string | null
  kind: string | null
  entryKind: EntryKind | null
  category: string | null
  date: string
  sequence: number
  amount: number
  cashEffect: number
  quantity: number | null
  price: number | null
  priceDate: string | null
  fee: number
  linkedTransactionId: string | null
  isReversed: boolean
  needsDateConfirmation: boolean
  relatedCashAdjustmentId: string | null
  targetBalance: number | null
  recordedDifference: number | null
  subject?: string | null
  note: string | null
  version: number
}

export interface HoldingReplay {
  positionId: string | null
  quantity: number
  cost: number
  realizedProfit: number
  coverageFrom: string | null
  canRebuild: boolean
  price: number | null
  priceDate: string | null
  priceSource: string | null
  pendingAmount: number
  appliedShares: number
  pendingArrivalAmount: number
  value: number | null
}

export interface ReplayResult {
  asOfDate: string
  cashBalance: number | null
  cashCoverageFrom: string | null
  providentBalance: number | null
  providentCoverageFrom: string | null
  funds: HoldingReplay[]
  gold: HoldingReplay | null
  adjustments: { id: string; previousBalance: number; difference: number }[]
  issues: string[]
  knownTotalAssets: number
  totalAssets: number | null
}

export interface HistoryOverview {
  ledgerVersion: number
  cashOpening: BusinessFact
  facts: BusinessFact[]
  prices: { id: string; positionId: string | null; assetType: string; price: number; effectiveDate: string; source: string; obtainedAt: string; isSuperseded: boolean }[]
  audit: { requestId: string; createdAt: string; beforeJson: string | null }[]
  snapshot: ReplayResult
}

export interface HistoryChange {
  requestId: string
  expectedLedgerVersion: number
  source: BusinessFact['source']
  factId: string
  expectedFactVersion: number
  action: 'correct' | 'reverse'
  date: string
  sequence: number
  amount: number
  quantity: number | null
  price: number | null
  priceDate: string | null
  entryKind: EntryKind | null
  category: string | null
  note: string | null
  reason: string
  relatedCashAdjustmentId: string | null
  fee?: number | null
}

export interface HistoryPreview {
  ledgerVersion: number
  before: ReplayResult
  after: ReplayResult
  previewToken: string
}
