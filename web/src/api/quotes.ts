import { apiRequest } from './client'

export type QuoteRefreshScope = 'all' | 'funds' | 'gold' | 'stocks'

export interface QuoteRefreshItem {
  assetType: string
  positionId: string | null
  name: string
  code: string | null
  platform: string | null
  success: boolean
  updated: boolean
  attempts: number
  source: string | null
  price: number | null
  effectiveDate: string | null
  lastAttemptAt: string | null
  error: string | null
}

export interface QuoteRefreshRun {
  trigger: string
  scope: QuoteRefreshScope
  startedAt: string
  completedAt: string
  durationMs: number
  fundAttempted: number
  fundSucceeded: number
  fundFailed: number
  goldAttempted: number
  goldSucceeded: number
  goldFailed: number
  stockAttempted: number
  stockSucceeded: number
  stockFailed: number
  updated: number
  items: QuoteRefreshItem[]
  errors: string[]
}

export interface QuoteStatusItem {
  assetType: string
  positionId: string | null
  name: string
  code: string | null
  platform: string | null
  price: number | null
  priceDate: string | null
  lastAttemptAt: string | null
  lastSource: string | null
  lastError: string | null
  status: string
}

export interface QuoteStatus {
  schedulerEnabled: boolean
  intervalSeconds: number
  nextRunAt: string | null
  isRunning: boolean
  lastRun: QuoteRefreshRun | null
  items: QuoteStatusItem[]
  checkedAt: string
}

export function fetchQuoteStatus(): Promise<QuoteStatus> {
  return apiRequest<QuoteStatus>('/api/ledger/quotes/status')
}

export function refreshQuotes(scope: QuoteRefreshScope): Promise<QuoteRefreshRun> {
  return apiRequest<QuoteRefreshRun>('/api/ledger/quotes/refresh', {
    method: 'POST',
    body: JSON.stringify({ scope })
  })
}
