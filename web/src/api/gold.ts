import type {
  BuyGoldInput,
  GoldMutationResult,
  GoldOverview,
  GoldSellArrivalInput,
  SaveGoldHoldingInput,
  SellGoldInput,
  UpdateGoldQuoteInput
} from '../types/gold'
import { apiRequest } from './client'

export function fetchGoldOverview(): Promise<GoldOverview> {
  return apiRequest<GoldOverview>('/api/ledger/gold')
}

export function buyGold(input: BuyGoldInput): Promise<GoldMutationResult> {
  return apiRequest<GoldMutationResult>('/api/ledger/gold/buys', {
    method: 'POST',
    body: JSON.stringify(input)
  })
}

export function sellGold(input: SellGoldInput): Promise<GoldMutationResult> {
  return apiRequest<GoldMutationResult>('/api/ledger/gold/sells', {
    method: 'POST',
    body: JSON.stringify(input)
  })
}

export function arriveGoldSell(transactionId: string, input: GoldSellArrivalInput): Promise<GoldMutationResult> {
  return apiRequest<GoldMutationResult>(`/api/ledger/gold/sells/${encodeURIComponent(transactionId)}/arrival`, {
    method: 'POST',
    body: JSON.stringify(input)
  })
}

export function updateGoldQuote(input: UpdateGoldQuoteInput): Promise<GoldMutationResult> {
  return apiRequest<GoldMutationResult>('/api/ledger/gold/quote', {
    method: 'POST',
    body: JSON.stringify(input)
  })
}

export function saveGoldHolding(input: SaveGoldHoldingInput): Promise<GoldMutationResult> {
  return apiRequest<GoldMutationResult>('/api/ledger/gold/holding', {
    method: 'POST',
    body: JSON.stringify(input)
  })
}
