import type {
  BuyFundInput,
  CreateFundPositionInput,
  FundMutationResult,
  FundOverview,
  SellFundInput,
  UpdateFundQuoteInput
} from '../types/fund'
import { apiRequest } from './client'

export function fetchFundOverview(): Promise<FundOverview> {
  return apiRequest<FundOverview>('/api/ledger/funds')
}

export function createFundPosition(input: CreateFundPositionInput): Promise<FundMutationResult> {
  return apiRequest<FundMutationResult>('/api/ledger/funds/positions', {
    method: 'POST',
    body: JSON.stringify(input)
  })
}

export function buyFund(input: BuyFundInput): Promise<FundMutationResult> {
  return apiRequest<FundMutationResult>('/api/ledger/funds/buys', {
    method: 'POST',
    body: JSON.stringify(input)
  })
}

export function sellFund(input: SellFundInput): Promise<FundMutationResult> {
  return apiRequest<FundMutationResult>('/api/ledger/funds/sells', {
    method: 'POST',
    body: JSON.stringify(input)
  })
}

export function updateFundQuote(input: UpdateFundQuoteInput): Promise<FundMutationResult> {
  return apiRequest<FundMutationResult>('/api/ledger/funds/quote', {
    method: 'POST',
    body: JSON.stringify(input)
  })
}
