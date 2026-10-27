export type GoldActivityType = 'buy' | 'sell' | 'sellPending' | 'sellArrival' | 'quote' | 'correction'

export interface PendingGoldArrival {
  transactionId: string
  grams: number
  amount: number
  businessDate: string
  note: string | null
}

export interface GoldActivity {
  id: string
  type: GoldActivityType
  grams: number | null
  amount: number
  cashEffect: number
  quote: number | null
  businessDate: string
  note: string | null
  createdAt: string
}

export interface GoldOverview {
  historyConfirmed: boolean
  hasEffectiveQuote: boolean
  version: number
  isConfigured: boolean
  grams: number
  cost: number
  quote: number
  quoteDate: string | null
  value: number
  averageCost: number | null
  holdingProfit: number
  realizedProfit: number
  cashBalance: number
  activities: GoldActivity[]
  pendingArrivalAmount: number
  pendingArrivals: PendingGoldArrival[]
  lastQuoteAttemptAt: string | null
  lastQuoteSource: string | null
  lastQuoteError: string | null
}

export interface GoldQuoteRefreshResult {
  updated: boolean
  source: string
  quote: number
  quoteDate: string
  obtainedAt: string
  lastAttemptAt: string
  value: number
  cashBalance: number
}

export interface GoldMutationResult {
  grams: number
  cost: number
  quote: number
  quoteDate: string | null
  value: number
  cashBalance: number
  activity: GoldActivity
}

export interface BuyGoldInput {
  grams: number
  amount: number
  quote: number
  quoteDate: string
  businessDate: string
  note: string
  requestId: string
}

export interface SellGoldInput {
  grams: number
  netAmount: number
  businessDate: string
  note: string
  requestId: string
}

export interface GoldSellArrivalInput {
  amount: number
  businessDate: string
  note: string
  requestId: string
}

export interface SaveGoldHoldingInput {
  holdingDate: string
  expectedVersion: number
  grams: number
  cost: number
  quote: number
  quoteDate: string
  note: string
  requestId: string
}
