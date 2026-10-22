export type FundActivityType = 'opening' | 'buy' | 'sell' | 'quote'

export interface FundPosition {
  historyConfirmed: boolean
  hasEffectiveQuote: boolean
  id: string
  name: string
  code: string | null
  shareClass: string | null
  platform: string | null
  shares: number
  cost: number
  netValue: number
  netValueDate: string
  value: number
  holdingProfit: number
  realizedProfit: number
}

export interface FundActivity {
  id: string
  positionId: string
  positionName: string
  type: FundActivityType
  shares: number | null
  amount: number
  cashEffect: number
  netValue: number | null
  businessDate: string
  note: string | null
  createdAt: string
}

export interface FundOverview {
  historyConfirmed: boolean
  hasEffectiveQuotes: boolean
  totalValue: number
  totalCost: number
  holdingProfit: number
  totalRealizedProfit: number
  cashBalance: number
  positions: FundPosition[]
  activities: FundActivity[]
}

export interface FundMutationResult {
  position: FundPosition
  cashBalance: number
  activity: FundActivity
}

export interface CreateFundPositionInput {
  holdingDate: string
  name: string
  code: string
  shareClass: string
  platform: string
  shares: number
  cost: number
  netValue: number
  netValueDate: string
  note: string
  requestId: string
}

export interface BuyFundInput {
  relatedCashAdjustmentId?: string | null
  positionId: string
  shares: number
  amount: number
  netValue: number
  netValueDate: string
  businessDate: string
  note: string
  requestId: string
}

export interface SellFundInput {
  relatedCashAdjustmentId?: string | null
  positionId: string
  shares: number
  netAmount: number
  businessDate: string
  note: string
  requestId: string
}

export interface UpdateFundQuoteInput {
  positionId: string
  netValue: number
  netValueDate: string
  requestId: string
}
