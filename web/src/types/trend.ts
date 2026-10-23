export type TrendRange = 'week' | 'month' | 'quarter' | 'year' | 'all'

export interface AssetTrendPoint {
  date: string
  totalAssets: number | null
  knownTotalAssets: number
  cash: number | null
  provident: number | null
  fundValue: number | null
  fundPending: number
  fundPendingArrival: number
  goldValue: number | null
  goldPendingArrival: number
  isComplete: boolean
  hasCash: boolean
  hasProvident: boolean
  hasFunds: boolean
  hasGold: boolean
}

export interface AssetTrend {
  range: TrendRange
  from: string
  to: string
  points: AssetTrendPoint[]
  notes: string[]
}
