<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import {
  downloadBackup,
  downloadDetails,
  restoreBackup,
  validateBackup
} from '../api/backup'
import type { BackupSummary, BackupValidation, RestoreBackupResult } from '../api/backup'
import BackButton from '../components/BackButton.vue'
import { addAmounts, formatBusinessDate, formatMoney } from '../utils/format'

const router = useRouter()
const backup = ref<unknown>(null)
const fileName = ref('')
const isReading = ref(false)
const isValidating = ref(false)
const isRestoring = ref(false)
const isExporting = ref(false)
const validation = ref<BackupValidation | null>(null)
const restoreResult = ref<RestoreBackupResult | null>(null)
const message = ref('')
const errorMessage = ref('')
const confirmReplace = ref(false)
const exportFrom = ref('')
const exportTo = ref('')

const hasBackup = computed(() => backup.value !== null)
const canRestore = computed(() =>
  validation.value?.isValid === true
  && validation.value.validationToken !== null
  && (!validation.value.willReplace || confirmReplace.value)
  && !isRestoring.value)

function goBack(): void {
  void router.push({ name: 'mine' })
}

async function exportDetails(): Promise<void> {
  isExporting.value = true
  message.value = ''
  errorMessage.value = ''

  try {
    await downloadDetails(exportFrom.value, exportTo.value)
    message.value = '明细 CSV 已生成，浏览器已开始下载。'
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : '导出明细失败，请重试。'
  } finally {
    isExporting.value = false
  }
}

async function exportBackup(): Promise<void> {
  isExporting.value = true
  message.value = ''
  errorMessage.value = ''

  try {
    await downloadBackup()
    message.value = '完整备份已生成，浏览器已开始下载。'
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : '下载备份失败，请重试。'
  } finally {
    isExporting.value = false
  }
}

async function chooseFile(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]

  if (!file) {
    return
  }

  fileName.value = file.name
  backup.value = null
  validation.value = null
  restoreResult.value = null
  confirmReplace.value = false
  message.value = ''
  errorMessage.value = ''
  isReading.value = true

  try {
    const text = await file.text()
    backup.value = JSON.parse(text) as unknown
    await validateSelected()
  } catch {
    errorMessage.value = '无法读取备份文件：请确认它是本应用导出的 JSON 文件。'
  } finally {
    isReading.value = false
  }
}

async function validateSelected(): Promise<void> {
  if (!hasBackup.value) {
    errorMessage.value = '请先选择备份文件。'
    return
  }

  isValidating.value = true
  errorMessage.value = ''
  message.value = ''
  restoreResult.value = null
  confirmReplace.value = false

  try {
    validation.value = await validateBackup(backup.value)
    message.value = validation.value.isValid
      ? '备份校验通过，请核对摘要后确认恢复。'
      : '备份校验未通过，未修改当前账本。'
  } catch (error) {
    validation.value = null
    errorMessage.value = error instanceof Error ? error.message : '备份校验失败，请重试。'
  } finally {
    isValidating.value = false
  }
}

async function restore(): Promise<void> {
  const current = validation.value

  if (!current?.isValid || !current.validationToken || !canRestore.value) {
    return
  }

  isRestoring.value = true
  errorMessage.value = ''
  message.value = ''

  try {
    restoreResult.value = await restoreBackup(
      backup.value,
      current.validationToken,
      current.willReplace
    )
    message.value = restoreResult.value.replacedExisting
      ? '恢复完成，当前账本已按备份替换。'
      : '恢复完成，已建立账本。'
    validation.value = null
    backup.value = null
    confirmReplace.value = false
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : '恢复失败，当前账本未修改。'
  } finally {
    isRestoring.value = false
  }
}

function summaryItems(summary: BackupSummary): { label: string; value: string }[] {
  return [
    { label: '收支记录', value: `${summary.entryCount} 条` },
    { label: '资产记录', value: `${summary.assetTransactionCount} 条` },
    { label: '自动计划', value: `${summary.planCount} 个` },
    { label: '执行期次', value: `${summary.planOccurrenceCount} 个` },
    { label: '请求回执', value: `${summary.mutationReceiptCount} 条` },
    { label: '行情观测', value: `${summary.priceObservationCount} 条` },
    { label: '现金', value: `¥${formatMoney(summary.cashBalance)}` },
    { label: '公积金', value: summary.providentBalance === null ? '未录入' : `¥${formatMoney(summary.providentBalance)}` },
    { label: '基金', value: `¥${formatMoney(addAmounts(summary.fundValue, summary.fundPending, summary.fundPendingArrival))}` },
    { label: '积存金', value: summary.goldValue === null ? '未录入' : `¥${formatMoney(addAmounts(summary.goldValue, summary.goldPendingArrival))}` },
    { label: '总资产', value: summary.totalAssets === null ? '依据不足' : `¥${formatMoney(summary.totalAssets)}` },
    { label: '起始日期', value: formatBusinessDate(summary.startDate) }
  ]
}
</script>

<template>
  <main class="page backup-page">
    <header class="backup-header">
      <BackButton @back="goBack" />
      <h1>数据导出与备份</h1>
      <span class="header-spacer" />
    </header>

    <p class="page-intro">
      导出用于人工核对明细；完整备份包含起始余额、收支、持仓、在途、分组、行情、计划期次、请求回执和更正历史。
    </p>

    <section class="backup-card">
      <header>
        <h2>导出</h2>
        <small>只读取，不改变账本</small>
      </header>
      <div class="form-row">
        <label class="form-field">
          <span>明细开始日期（选填）</span>
          <input v-model="exportFrom" type="date" />
        </label>
        <label class="form-field">
          <span>明细结束日期（选填）</span>
          <input v-model="exportTo" type="date" />
        </label>
      </div>
      <div class="backup-actions">
        <button type="button" class="action-secondary" :disabled="isExporting" @click="exportDetails">
          导出明细 CSV
        </button>
        <button type="button" class="action-primary" :disabled="isExporting" @click="exportBackup">
          下载完整备份
        </button>
      </div>
      <p class="helper-note">明细 CSV 包含日期、类型、方向、金额、现金影响、数量、净值、费用、分类 / 标的、状态和关联标识。</p>
    </section>

    <section class="backup-card">
      <header>
        <h2>恢复备份</h2>
        <small>先校验，再确认</small>
      </header>
      <label class="file-field">
        <span>选择备份 JSON</span>
        <input type="file" accept=".json,application/json" @change="chooseFile" />
      </label>
      <p v-if="fileName" class="file-name">已选择：{{ fileName }}</p>

      <div class="backup-actions">
        <button
          type="button"
          class="action-secondary"
          :disabled="!hasBackup || isValidating || isReading"
          @click="validateSelected"
        >
          {{ isValidating ? '正在校验…' : '重新校验' }}
        </button>
      </div>

      <p class="helper-note">
        备份文件只在本机浏览器读取后发送到当前账本服务校验。校验通过前不会写入任何数据。
      </p>
    </section>

    <p v-if="message" class="notice" role="status">{{ message }}</p>
    <p v-if="errorMessage" class="form-error" role="alert">{{ errorMessage }}</p>

    <section v-if="restoreResult" class="backup-card success-card">
      <header>
        <h2>恢复完成</h2>
        <small>{{ formatBusinessDate(restoreResult.restoredAt.slice(0, 10)) }}</small>
      </header>
      <div class="summary-grid">
        <div v-for="item in summaryItems(restoreResult.summary)" :key="`restored-${item.label}`">
          <span>{{ item.label }}</span>
          <b>{{ item.value }}</b>
        </div>
      </div>
      <RouterLink class="home-link" to="/">返回总览核对余额 ›</RouterLink>
    </section>

    <template v-if="validation">
      <section class="backup-card" :class="validation.isValid ? 'valid-card' : 'invalid-card'">
        <header>
          <h2>{{ validation.isValid ? '校验通过' : '校验未通过' }}</h2>
          <small>{{ validation.isValid ? (validation.willReplace ? '将替换当前账本' : '将建立新账本') : '不会修改当前账本' }}</small>
        </header>

        <ul v-if="validation.errors.length" class="validation-list errors">
          <li v-for="error in validation.errors" :key="error">{{ error }}</li>
        </ul>

        <div class="summary-compare">
          <article v-if="validation.current">
            <h3>当前账本</h3>
            <div class="summary-grid compact">
              <div v-for="item in summaryItems(validation.current)" :key="`current-${item.label}`">
                <span>{{ item.label }}</span>
                <b>{{ item.value }}</b>
              </div>
            </div>
          </article>
          <article>
            <h3>{{ validation.current ? '备份内容' : '将恢复的备份' }}</h3>
            <div class="summary-grid compact">
              <div v-for="item in summaryItems(validation.summary)" :key="`backup-${item.label}`">
                <span>{{ item.label }}</span>
                <b>{{ item.value }}</b>
              </div>
            </div>
          </article>
        </div>

        <ul v-if="validation.duplicateRisks.length" class="validation-list risks">
          <li v-for="risk in validation.duplicateRisks" :key="risk">{{ risk }}</li>
        </ul>

        <ul v-if="validation.warnings.length" class="validation-list warnings">
          <li v-for="warning in validation.warnings" :key="warning">{{ warning }}</li>
        </ul>

        <label v-if="validation.willReplace && validation.isValid" class="confirm-replace">
          <input v-model="confirmReplace" type="checkbox" />
          <span>我确认用这份备份替换当前账本；恢复失败不会修改当前账本。</span>
        </label>

        <button
          v-if="validation.isValid"
          type="button"
          class="restore-button"
          :disabled="!canRestore"
          @click="restore"
        >
          {{ isRestoring ? '正在恢复…' : validation.willReplace ? '确认恢复并替换当前账本' : '确认恢复' }}
        </button>
      </section>
    </template>

    <p class="page-footnote">
      恢复是替换而不是追加：收支、资产记录和计划期次按备份还原，不会重复记账；恢复前请先下载当前账本备份。
    </p>
  </main>
</template>

<style scoped>
.backup-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
}

.header-spacer {
  width: 36px;
  flex-shrink: 0;
}

.backup-header h1 {
  margin: 0;
  font-size: 18px;
  font-weight: 600;
  letter-spacing: -0.4px;
}

.page-intro {
  margin: 14px 0 18px;
  color: var(--color-text-secondary);
  font-size: var(--text-footnote);
  line-height: 1.8;
}

.backup-card {
  margin-top: 12px;
  padding: 14px;
  border: 1px solid var(--color-border-subtle);
  border-radius: var(--radius-card);
  background: var(--color-bg-surface);
}

.backup-card > header {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 10px;
}

.backup-card h2 {
  margin: 0;
  font-size: 14px;
  font-weight: 600;
}

.backup-card header small {
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
}

.backup-actions {
  display: flex;
  gap: 8px;
  margin-top: 12px;
}

.backup-actions button {
  flex: 1;
  min-height: var(--touch-target);
  border-radius: var(--radius-control);
  font-size: var(--text-footnote);
}

.action-primary {
  border: 1px solid var(--color-brand);
  background: var(--color-brand);
  color: var(--color-on-brand);
}

.action-secondary {
  border: 1px solid var(--color-border-control);
  background: var(--color-bg-surface);
  color: var(--color-brand);
}

.backup-actions button:disabled,
.restore-button:disabled {
  border-color: var(--color-border-disabled);
  background: var(--color-bg-disabled);
  color: var(--color-text-disabled);
}

.helper-note,
.page-footnote {
  margin: 10px 0 0;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
  line-height: 1.7;
}

.page-footnote {
  margin-top: 18px;
}

.file-field {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin-top: 12px;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
}

.file-field input {
  padding: 9px;
  border: 1px dashed var(--color-border-control);
  border-radius: var(--radius-control);
  background: var(--color-bg-subtle);
  font-size: var(--text-caption);
}

.file-name {
  margin: 8px 0 0;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
}

.notice {
  margin: 12px 0 0;
  padding: 10px 12px;
  border-radius: var(--radius-tag);
  background: var(--color-success-bg);
  color: var(--color-success);
  font-size: var(--text-footnote);
  line-height: 1.7;
}

.form-error {
  margin: 12px 0 0;
  padding: 10px 12px;
  border-radius: var(--radius-tag);
  background: var(--color-error-bg);
  color: var(--color-finance-loss);
  font-size: var(--text-footnote);
  line-height: 1.7;
}

.valid-card {
  border-color: var(--color-border-control);
}

.invalid-card {
  border-color: var(--color-finance-loss);
}

.validation-list {
  margin: 10px 0 0;
  padding-left: 16px;
  font-size: var(--text-caption);
  line-height: 1.7;
}

.validation-list.errors {
  color: var(--color-finance-loss);
}

.validation-list.risks {
  color: var(--color-finance-gain);
}

.validation-list.warnings {
  color: var(--color-text-secondary);
}

.summary-compare {
  display: grid;
  gap: 10px;
  margin-top: 12px;
}

.summary-compare article {
  min-width: 0;
}

.summary-compare h3 {
  margin: 0 0 8px;
  font-size: var(--text-caption);
  font-weight: 600;
}

.summary-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 8px;
}

.summary-grid > div {
  min-width: 0;
  padding: 8px;
  border-radius: var(--radius-tag);
  background: var(--color-bg-subtle);
}

.summary-grid span {
  display: block;
  color: var(--color-text-secondary);
  font-size: var(--text-caption);
}

.summary-grid b {
  display: block;
  margin-top: 3px;
  overflow: hidden;
  font-size: var(--text-caption);
  font-weight: 500;
  font-variant-numeric: tabular-nums;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.confirm-replace {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  margin-top: 14px;
  color: var(--color-text-primary);
  font-size: var(--text-caption);
  line-height: 1.6;
}

.confirm-replace input {
  margin-top: 2px;
}

.restore-button {
  width: 100%;
  min-height: var(--touch-target);
  margin-top: 12px;
  border: 1px solid var(--color-finance-loss);
  border-radius: var(--radius-control);
  background: var(--color-bg-surface);
  color: var(--color-finance-loss);
  font-size: var(--text-footnote);
  font-weight: 600;
}

.success-card .home-link {
  display: inline-block;
  margin-top: 12px;
  color: var(--color-brand);
  font-size: var(--text-caption);
  text-decoration: none;
}
</style>
