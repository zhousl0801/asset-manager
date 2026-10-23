export interface BackupSummary {
  ledgerId: string
  startDate: string
  cashBalance: number
  providentBalance: number | null
  fundValue: number
  fundPending: number
  fundPendingArrival: number
  goldValue: number | null
  goldPendingArrival: number
  totalAssets: number | null
  isComplete: boolean
  entryCount: number
  assetTransactionCount: number
  planCount: number
  planOccurrenceCount: number
  mutationReceiptCount: number
  priceObservationCount: number
}

export interface BackupValidation {
  isValid: boolean
  willReplace: boolean
  validationToken: string | null
  summary: BackupSummary
  current: BackupSummary | null
  errors: string[]
  warnings: string[]
  duplicateRisks: string[]
}

export interface RestoreBackupResult {
  restored: boolean
  replacedExisting: boolean
  summary: BackupSummary
  restoredAt: string
  warnings: string[]
}

export function validateBackup(backup: unknown): Promise<BackupValidation> {
  return requestJson<BackupValidation>('/api/ledger/backup/validate', {
    method: 'POST',
    body: JSON.stringify(backup)
  })
}

export function restoreBackup(
  backup: unknown,
  validationToken: string,
  replaceExisting: boolean
): Promise<RestoreBackupResult> {
  return requestJson<RestoreBackupResult>('/api/ledger/backup/restore', {
    method: 'POST',
    body: JSON.stringify({ backup, validationToken, replaceExisting })
  })
}

export function downloadBackup(): Promise<void> {
  return download('/api/ledger/backup', 'asset-keeper-backup.json')
}

export function downloadDetails(from: string, to: string): Promise<void> {
  const params = new URLSearchParams()

  if (from) {
    params.set('from', from)
  }

  if (to) {
    params.set('to', to)
  }

  const suffix = params.toString() ? `?${params.toString()}` : ''
  return download(`/api/ledger/export/details.csv${suffix}`, 'asset-keeper-details.csv')
}

async function requestJson<T>(path: string, init: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    headers: { 'Content-Type': 'application/json' }
  })

  if (!response.ok) {
    throw new Error(await readError(response))
  }

  return await response.json() as T
}

async function download(path: string, fallbackName: string): Promise<void> {
  const response = await fetch(path)

  if (!response.ok) {
    throw new Error(await readError(response))
  }

  const blob = await response.blob()
  const disposition = response.headers.get('Content-Disposition') ?? ''
  const match = /filename\*?=(?:UTF-8''|")?([^";]+)/i.exec(disposition)
  const fileName = match?.[1] ? decodeURIComponent(match[1].replaceAll('"', '')) : fallbackName
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = fileName
  document.body.appendChild(anchor)
  anchor.click()
  anchor.remove()
  URL.revokeObjectURL(url)
}

async function readError(response: Response): Promise<string> {
  try {
    const problem = await response.json() as {
      title?: string
      detail?: string
      errors?: Record<string, string[]>
    }
    const fieldError = problem.errors ? Object.values(problem.errors).flat()[0] : undefined

    return fieldError ?? problem.detail ?? problem.title ?? `请求失败（HTTP ${response.status}）`
  } catch {
    return `请求失败（HTTP ${response.status}）`
  }
}
