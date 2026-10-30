import type {
  BuyStockInput,
  SaveStockHoldingInput,
  SellStockInput,
  StockMutationResult,
  StockOverview,
  StockQuoteRefresh,
  StockQuoteRefreshBatch,
  StockSearchResult
} from '../types/stock'
import { apiRequest } from './client'

export function fetchStockOverview(): Promise<StockOverview> {
  return apiRequest<StockOverview>('/api/ledger/stocks')
}

export function searchStocks(query: string): Promise<StockSearchResult[]> {
  return apiRequest<StockSearchResult[]>(`/api/ledger/stocks/search?query=${encodeURIComponent(query)}`)
}

export function buyStock(input: BuyStockInput): Promise<StockMutationResult> {
  return apiRequest<StockMutationResult>('/api/ledger/stocks/buys', {
    method: 'POST',
    body: JSON.stringify(input)
  })
}

export function sellStock(input: SellStockInput): Promise<StockMutationResult> {
  return apiRequest<StockMutationResult>('/api/ledger/stocks/sells', {
    method: 'POST',
    body: JSON.stringify(input)
  })
}

export function saveStockHolding(input: SaveStockHoldingInput): Promise<StockMutationResult> {
  return apiRequest<StockMutationResult>('/api/ledger/stocks/positions', {
    method: 'POST',
    body: JSON.stringify(input)
  })
}

export function refreshStockQuotes(): Promise<StockQuoteRefreshBatch> {
  return apiRequest<StockQuoteRefreshBatch>('/api/ledger/stocks/quotes/refresh', { method: 'POST' })
}

export function refreshStockQuote(positionId: string): Promise<StockQuoteRefresh> {
  return apiRequest<StockQuoteRefresh>(
    `/api/ledger/stocks/${encodeURIComponent(positionId)}/quote/refresh`,
    { method: 'POST' }
  )
}
