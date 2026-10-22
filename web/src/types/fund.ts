export type FundActivityType =
  | 'opening'
  | 'buy'
  | 'buyPending'
  | 'buyConfirmed'
  | 'sell'
  | 'sellApply'
  | 'sellConfirm'
  | 'sellArrival'
  | 'quote'

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
  lastQuoteAttemptAt: string | null
  lastQuoteSource: string | null
  lastQuoteError: string | null
}

export interface FundQuoteRefreshResult {
  position: FundPosition
  updated: boolean
  source: string
  netValue: number
  effectiveDate: string
  obtainedAt: string
  lastAttemptAt: string
}

export interface PendingFundBuy {
  transactionId: string
  positionId: string
  positionName: string
  amount: number
  businessDate: string
  note: string | null
}

export interface PendingFundSell {
  transactionId: string
  positionId: string
  positionName: string
  shares: number
  businessDate: string
  note: string | null
}

export interface PendingFundArrival {
  transactionId: string
  positionId: string
  positionName: string
  shares: number
  amount: number
  businessDate: string
  note: string | null
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
  fee: number
  linkedTransactionId: string | null
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
  pendingAmount: number
  pendingBuys: PendingFundBuy[]
  pendingArrivalAmount: number
  sellApplications: PendingFundSell[]
  pendingArrivals: PendingFundArrival[]
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
  amount: number
  businessDate: string
  note: string
  requestId: string
}

export interface ConfirmFundBuyInput {
  shares: number
  netValue: number
  netValueDate: string
  fee: number
  businessDate: string
  note: string
  requestId: string
}

export interface SellFundInput {
  positionId: string
  shares: number
  businessDate: string
  note: string
  requestId: string
}

export interface ConfirmFundSellInput {
  shares: number
  netValue: number
  netValueDate: string
  fee: number
  netAmount: number
  businessDate: string
  note: string
  requestId: string
}

export interface FundSellArrivalInput {
  amount: number
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
