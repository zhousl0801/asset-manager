import type {
  BuyFundInput,
  ConfirmFundBuyInput,
  ConfirmFundSellInput,
  CreateFundPositionInput,
  FundGroup,
  FundMutationResult,
  FundOverview,
  FundQuoteRefreshResult,
  FundSellArrivalInput,
  SellFundInput,
  UpdateFundQuoteInput
} from '../types/fund'
import { apiRequest } from './client'

export function fetchFundOverview(): Promise<FundOverview> {
  return apiRequest<FundOverview>('/api/ledger/funds')
}

export function fetchFundGroups(): Promise<FundGroup[]> {
  return apiRequest<FundGroup[]>('/api/ledger/fund-groups')
}

export function createFundGroup(name: string): Promise<FundGroup> {
  return apiRequest<FundGroup>('/api/ledger/fund-groups', {
    method: 'POST',
    body: JSON.stringify({ name })
  })
}

export function renameFundGroup(groupId: string, name: string): Promise<FundGroup> {
  return apiRequest<FundGroup>(`/api/ledger/fund-groups/${encodeURIComponent(groupId)}`, {
    method: 'PUT',
    body: JSON.stringify({ name })
  })
}

export function deleteFundGroup(groupId: string): Promise<void> {
  return apiRequest<void>(`/api/ledger/fund-groups/${encodeURIComponent(groupId)}`, {
    method: 'DELETE'
  })
}

export function assignFundGroup(positionId: string, groupId: string | null): Promise<{ groupId: string | null; positionIds: string[] }> {
  return apiRequest(`/api/ledger/fund-groups/positions/${encodeURIComponent(positionId)}`, {
    method: 'PUT',
    body: JSON.stringify({ groupId })
  })
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

export function confirmFundBuy(transactionId: string, input: ConfirmFundBuyInput): Promise<FundMutationResult> {
  return apiRequest<FundMutationResult>(`/api/ledger/funds/buys/${encodeURIComponent(transactionId)}/confirm`, {
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

export function confirmFundSell(transactionId: string, input: ConfirmFundSellInput): Promise<FundMutationResult> {
  return apiRequest<FundMutationResult>(`/api/ledger/funds/sells/${encodeURIComponent(transactionId)}/confirm`, {
    method: 'POST',
    body: JSON.stringify(input)
  })
}

export function arriveFundSell(transactionId: string, input: FundSellArrivalInput): Promise<FundMutationResult> {
  return apiRequest<FundMutationResult>(`/api/ledger/funds/sells/${encodeURIComponent(transactionId)}/arrival`, {
    method: 'POST',
    body: JSON.stringify(input)
  })
}

export function refreshFundQuote(positionId: string): Promise<FundQuoteRefreshResult> {
  return apiRequest<FundQuoteRefreshResult>(`/api/ledger/funds/${encodeURIComponent(positionId)}/quote/refresh`, {
    method: 'POST'
  })
}

export function updateFundQuote(input: UpdateFundQuoteInput): Promise<FundMutationResult> {
  return apiRequest<FundMutationResult>('/api/ledger/funds/quote', {
    method: 'POST',
    body: JSON.stringify(input)
  })
}
