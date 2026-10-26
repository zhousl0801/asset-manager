export type FundActivityType =
  | 'opening'
  | 'correction'
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
  groupId: string | null
  groupName: string | null
  confirmationDelayDays?: number
}

export interface FundGroup {
  id: string
  name: string
  sortOrder: number
  fundCount: number
  positionCount: number
  confirmedValue: number
}

export interface FundPlatformHolding {
  positionId: string
  platform: string | null
  name: string
  code: string | null
  shareClass: string | null
  shares: number
  cost: number
  netValue: number
  netValueDate: string
  value: number
  holdingProfit: number
  realizedProfit: number
  hasEffectiveQuote: boolean
  confirmationDelayDays?: number
}

export interface FundShareClass {
  key?: string
  code?: string | null
  identityBasis?: string
  shareClass: string | null
  shares: number
  cost: number
  value: number
  holdingProfit: number
  realizedProfit: number
  netValue: number | null
  netValueDate: string | null
  platforms: FundPlatformHolding[]
}

export interface FundSummary {
  key: string
  name: string
  groupId: string | null
  groupName: string | null
  value: number
  cost: number
  holdingProfit: number
  realizedProfit: number
  pendingBuyAmount: number
  pendingArrivalAmount: number
  assetValue: number
  platformCount: number
  latestNetValueDate: string | null
  historyConfirmed: boolean
  hasEffectiveQuotes: boolean
  classes: FundShareClass[]
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
  fee: number
  businessDate: string
  note: string | null
  confirmationDate: string
  canCancel: boolean
}

export interface PendingFundSell {
  transactionId: string
  positionId: string
  positionName: string
  shares: number
  businessDate: string
  note: string | null
  canCancel: boolean
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
  isReversed?: boolean
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
  groups: FundGroup[]
  funds: FundSummary[]
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
  note: string
  requestId: string
  confirmationDelayDays: number
}

export interface BuyFundInput {
  relatedCashAdjustmentId?: string | null
  positionId: string
  amount: number
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

export interface CorrectFundSharesInput {
  positionId: string
  shares: number
  note: string
  requestId: string
}
