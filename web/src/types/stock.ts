export type StockMarket = 'shanghai' | 'shenzhen' | 'beijing'

export type StockActivityType = 'opening' | 'buy' | 'sell' | 'quote' | 'correction'

export interface StockPosition {
  id: string
  market: StockMarket
  code: string
  name: string
  currency: string
  shares: number
  cost: number
  averageCost: number | null
  price: number
  priceDate: string
  previousClose: number | null
  previousCloseDate: string | null
  value: number
  holdingProfit: number
  realizedProfit: number
  todayProfit: number | null
  todayProfitComplete: boolean
  historyConfirmed: boolean
  hasEffectiveQuote: boolean
  version: number
  lastQuoteAttemptAt: string | null
  lastQuoteSource: string | null
  lastQuoteError: string | null
}

export interface StockActivity {
  id: string
  positionId: string
  positionName: string
  type: StockActivityType
  shares: number | null
  amount: number
  cashEffect: number
  fee: number
  price: number | null
  businessDate: string
  note: string | null
  createdAt: string
}

export interface StockOverview {
  isConfigured: boolean
  positionCount: number
  totalCost: number
  totalValue: number
  todayProfit: number | null
  todayProfitComplete: boolean
  todayProfitDate: string | null
  holdingProfit: number
  realizedProfit: number
  cashBalance: number
  positions: StockPosition[]
  activities: StockActivity[]
  historyConfirmed: boolean
  hasEffectiveQuotes: boolean
}

export interface StockSearchResult {
  market: StockMarket
  code: string
  name: string
  currency: string
  price: number | null
  priceDate: string | null
}

export interface StockMutationResult {
  position: StockPosition
  cashBalance: number
  activity: StockActivity
}

export interface StockQuote {
  market: StockMarket
  code: string
  price: number
  priceDate: string
  previousClose: number | null
  source: string
}

export interface StockQuoteRefresh {
  positionId: string
  name: string
  code: string
  market: StockMarket
  updated: boolean
  source: string
  price: number
  priceDate: string
  previousClose: number | null
  previousCloseDate: string | null
  obtainedAt: string
  lastAttemptAt: string
  value: number
  cashBalance: number
}

export interface StockQuoteRefreshBatch {
  attempted: number
  succeeded: number
  failed: number
  updated: number
  items: StockQuoteRefresh[]
  errors: string[]
}

export interface BuyStockInput {
  market: StockMarket
  code: string
  name: string
  shares: number
  price: number
  businessDate: string
  note: string
  requestId: string
}

export interface SellStockInput {
  positionId: string
  shares: number
  price: number
  businessDate: string
  note: string
  requestId: string
}

export interface SaveStockHoldingInput {
  market: StockMarket
  code: string
  name: string
  shares: number
  cost: number
  price: number
  priceDate: string
  holdingDate: string
  note: string
  requestId: string
  expectedVersion: number
  positionId?: string
}
