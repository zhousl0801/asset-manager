import type {
  PlanOccurrence,
  PlanOverview,
  PlanPreview,
  PlanRunResult,
  SavePlanInput
} from '../types/plan'
import { apiRequest, readOnlyRequest } from './client'

export function fetchPlanOverview(): Promise<PlanOverview> {
  return apiRequest<PlanOverview>('/api/ledger/plans')
}

export function savePlan(input: SavePlanInput): Promise<unknown> {
  return apiRequest('/api/ledger/plans', {
    method: 'POST',
    body: JSON.stringify(input)
  })
}

export function pausePlan(planId: string, requestId: string): Promise<unknown> {
  return apiRequest(`/api/ledger/plans/${encodeURIComponent(planId)}/pause`, {
    method: 'POST',
    body: JSON.stringify({ requestId })
  })
}

export function resumePlan(planId: string, requestId: string): Promise<unknown> {
  return apiRequest(`/api/ledger/plans/${encodeURIComponent(planId)}/resume`, {
    method: 'POST',
    body: JSON.stringify({ requestId })
  })
}

export function skipOccurrence(occurrenceId: string, requestId: string): Promise<unknown> {
  return apiRequest(`/api/ledger/plans/occurrences/${encodeURIComponent(occurrenceId)}/skip`, {
    method: 'POST',
    body: JSON.stringify({ requestId })
  })
}

export function retryOccurrence(occurrenceId: string, requestId: string): Promise<unknown> {
  return apiRequest(`/api/ledger/plans/occurrences/${encodeURIComponent(occurrenceId)}/retry`, {
    method: 'POST',
    body: JSON.stringify({ requestId })
  })
}

export interface RecordOccurrenceResult {
  occurrence: PlanOccurrence
  recorded: boolean
  error: string | null
}

export function recordOccurrence(occurrenceId: string, requestId: string): Promise<RecordOccurrenceResult> {
  return apiRequest<RecordOccurrenceResult>(`/api/ledger/plans/occurrences/${encodeURIComponent(occurrenceId)}/record`, {
    method: 'POST',
    body: JSON.stringify({ requestId })
  })
}

export function previewPlans(date: string, trialCash: number | null): Promise<PlanPreview> {
  return readOnlyRequest('/api/ledger/plans/preview', {
    method: 'POST',
    body: JSON.stringify({ date, trialCash })
  })
}

export function runPlans(): Promise<PlanRunResult> {
  return apiRequest<PlanRunResult>('/api/ledger/plans/run', { method: 'POST' })
}
