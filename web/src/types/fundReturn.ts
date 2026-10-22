import type { TrendRange } from './trend'

export type FundReturnStatus = 'complete' | 'partial' | 'notDisclosed' | 'insufficient'

export interface FundReturnPoint {
  date: string
  fundValue: number | null
  dailyProfit: number | null
  cumulativeProfit: number | null
  disclosedFunds: number
  totalFunds: number
  coveredPositions: number
  totalPositions: number
  hasDisclosure: boolean
  isComplete: boolean
  status: FundReturnStatus
}

export interface FundReturnSummary {
  key: string
  name: string
  groupId: string | null
  groupName: string | null
  openingValue: number | null
  closingValue: number | null
  intervalProfit: number | null
  holdingProfit: number | null
  holdingRate: number | null
  cumulativeProfit: number | null
  disclosureDays: number
  latestNetValueDate: string | null
  isComplete: boolean
  notes: string[]
}

export interface FundReturn {
  range: TrendRange
  from: string
  to: string
  intervalFrom: string
  openingAssetValue: number | null
  closingAssetValue: number | null
  netTransfer: number
  reconciliationDifference: number | null
  intervalProfit: number | null
  intervalRate: number | null
  holdingProfit: number | null
  holdingRate: number | null
  disclosureDays: number
  isComplete: boolean
  points: FundReturnPoint[]
  funds: FundReturnSummary[]
  notes: string[]
}

export interface FundReturnDayRow {
  positionId: string
  name: string
  shareClass: string | null
  platform: string | null
  value: number | null
  dailyProfit: number | null
  netValue: number | null
  netValueDate: string | null
  hasDisclosure: boolean
  isComplete: boolean
  status: FundReturnStatus
  note: string | null
}

export interface FundReturnDay {
  date: string
  totalProfit: number | null
  disclosedFunds: number
  totalFunds: number
  coveredPositions: number
  totalPositions: number
  isComplete: boolean
  status: FundReturnStatus
  positions: FundReturnDayRow[]
  notes: string[]
}
