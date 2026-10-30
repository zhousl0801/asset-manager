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
  FundSearchResult,
  FundPosition,
  SellFundInput,
  CorrectFundSharesInput
} from '../types/fund'
import type { FundReturn, FundReturnDay } from '../types/fundReturn'
import type { TrendRange } from '../types/trend'
import { apiRequest } from './client'

export function fetchFundOverview(): Promise<FundOverview> {
  return apiRequest<FundOverview>('/api/ledger/funds')
}

export function fetchFundGroups(): Promise<FundGroup[]> {
  return apiRequest<FundGroup[]>('/api/ledger/fund-groups')
}

export function searchFunds(query: string): Promise<FundSearchResult[]> {
  return apiRequest<FundSearchResult[]>(`/api/ledger/funds/search?query=${encodeURIComponent(query)}`)
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

export function fetchFundReturns(
  range: TrendRange,
  groupId: string | null,
  ungrouped: boolean
): Promise<FundReturn> {
  const params = new URLSearchParams({ range })

  if (groupId) {
    params.set('groupId', groupId)
  } else if (ungrouped) {
    params.set('ungrouped', 'true')
  }

  return apiRequest<FundReturn>(`/api/ledger/funds/returns?${params.toString()}`)
}

export function fetchFundReturnDay(
  date: string,
  groupId: string | null,
  ungrouped: boolean
): Promise<FundReturnDay> {
  const params = new URLSearchParams({ date })

  if (groupId) {
    params.set('groupId', groupId)
  } else if (ungrouped) {
    params.set('ungrouped', 'true')
  }

  return apiRequest<FundReturnDay>(`/api/ledger/funds/returns/day?${params.toString()}`)
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

export function setFundPriorRealizedProfit(
  positionId: string,
  input: { amount: number; note: string; requestId: string }
): Promise<FundPosition> {
  return apiRequest<FundPosition>(
    `/api/ledger/funds/positions/${encodeURIComponent(positionId)}/prior-realized-profit`,
    {
      method: 'POST',
      body: JSON.stringify(input)
    }
  )
}

export function confirmFundBuy(transactionId: string, input: ConfirmFundBuyInput): Promise<FundMutationResult> {
  return apiRequest<FundMutationResult>(`/api/ledger/funds/buys/${encodeURIComponent(transactionId)}/confirm`, {
    method: 'POST',
    body: JSON.stringify(input)
  })
}

export function cancelFundPending(transactionId: string, requestId: string): Promise<FundMutationResult> {
  return apiRequest<FundMutationResult>(`/api/ledger/funds/pending/${encodeURIComponent(transactionId)}/cancel`, {
    method: 'POST',
    body: JSON.stringify({ requestId })
  })
}

export function correctFundShares(input: CorrectFundSharesInput): Promise<FundMutationResult> {
  return apiRequest<FundMutationResult>('/api/ledger/funds/positions/correct-shares', {
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
