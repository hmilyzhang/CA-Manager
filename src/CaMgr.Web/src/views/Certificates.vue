<template>
  <div class="page">
    <div class="page-title">{{ $t('certs.title') }}</div>

    <div class="filter-bar">
      <el-radio-group v-model="status" @change="reload(true)">
        <el-radio-button value="all">{{ $t('status.all') }}</el-radio-button>
        <el-radio-button value="issued">{{ $t('status.issued') }}</el-radio-button>
        <el-radio-button value="revoked">{{ $t('status.revoked') }}</el-radio-button>
        <el-radio-button value="denied">{{ $t('status.denied') }}</el-radio-button>
        <el-radio-button value="failed">{{ $t('status.failed') }}</el-radio-button>
        <el-radio-button value="pending">{{ $t('status.pending') }}</el-radio-button>
      </el-radio-group>
      <el-input v-model="keyword" :placeholder="$t('certs.searchPlaceholder')" clearable style="width: 240px" @keyup.enter="reload(true)" @clear="reload(true)">
        <template #prefix><el-icon><Search /></el-icon></template>
      </el-input>
      <el-date-picker v-model="range" type="daterange" value-format="YYYY-MM-DD" :start-placeholder="$t('certs.from')" :end-placeholder="$t('certs.to')" style="width: 250px" @change="reload(true)" />
      <el-button @click="reload(true)">{{ $t('common.search') }}</el-button>
      <el-button @click="exportCsv"><el-icon><Download /></el-icon>&nbsp;{{ $t('common.export') }}</el-button>
      <span v-if="result.truncated" style="color:#e6a23c;font-size:12px">{{ $t('common.truncated') }}</span>
    </div>

    <el-table :data="result.items" v-loading="loading" @row-click="goDetail" row-class-name="clickable" stripe>
      <el-table-column prop="requestId" :label="$t('common.requestId')" width="90" />
      <el-table-column :label="$t('common.commonName')" min-width="200" show-overflow-tooltip>
        <template #default="{ row }">{{ row.commonName || $t('common.none') }}</template>
      </el-table-column>
      <el-table-column :label="$t('common.status')" width="110">
        <template #default="{ row }">
          <el-tag :type="statusTag(row.status)" effect="light">{{ statusText(row.status) }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column prop="template" :label="$t('common.template')" min-width="140" show-overflow-tooltip />
      <el-table-column prop="requester" :label="$t('common.requester')" min-width="130" show-overflow-tooltip />
      <el-table-column :label="$t('common.serialNumber')" min-width="180">
        <template #default="{ row }"><span class="mono">{{ row.serialNumber }}</span></template>
      </el-table-column>
      <el-table-column :label="$t('common.notAfter')" width="110">
        <template #default="{ row }">
          <span :style="expired(row) ? 'color:#f56c6c' : ''">{{ fmtDay(row.notAfter) }}</span>
        </template>
      </el-table-column>
      <el-table-column :label="$t('certs.revokedReason')" width="110">
        <template #default="{ row }">{{ reasonText(row.revokedReason) }}</template>
      </el-table-column>
      <el-table-column width="330" :label="$t('common.action')" class-name="nowrap-cell">
        <template #default="{ row }">
          <div style="display: flex; gap: 6px; flex-wrap: nowrap; align-items: center">
            <el-button size="small" @click.stop="goDetail(row)">{{ $t('common.detail') }}</el-button>
            <el-button size="small" type="primary" plain @click.stop="downloadCerts(row)">{{ $t('common.download') }}</el-button>
            <el-button v-if="row.status === 'issued' && canOperate" size="small" type="danger" plain @click.stop="openRevoke(row)">{{ $t('common.revoke') }}</el-button>
            <el-button v-if="row.status === 'revoked' && row.revokedReason === 6 && canOperate" size="small" type="warning" plain @click.stop="unrevoke(row)">{{ $t('common.unrevoke') }}</el-button>
          </div>
        </template>
      </el-table-column>
    </el-table>
    <div style="margin-top:12px; display:flex; justify-content:space-between; align-items:center">
      <span style="color:#909399;font-size:13px">{{ $t('common.matched', { n: result.totalFetched }) }}</span>
      <el-pagination layout="prev, pager, next" :page-size="limit" :total="Math.min(result.totalFetched, 5000)" :current-page="page" @current-change="onPage" />
    </div>

    <el-dialog v-model="revokeDlg" :title="$t('certs.revokeTitle')" width="520">
      <el-alert type="warning" :closable="false" style="margin-bottom: 12px" :title="$t('certs.revokeWarning')" />
      <el-descriptions :column="1" border size="small" style="margin-bottom: 12px">
        <el-descriptions-item label="CN">{{ revokeTarget?.commonName }}</el-descriptions-item>
        <el-descriptions-item :label="$t('common.serialNumber')"><span class="mono">{{ revokeTarget?.serialNumber }}</span></el-descriptions-item>
      </el-descriptions>
      <el-form label-width="110px">
        <el-form-item :label="$t('certs.reasonLabel')">
          <el-select v-model="revokeForm.reason" style="width: 100%">
            <el-option v-for="r in reasonOptions" :key="r.value" :label="$t('certs.reasons.' + r.value)" :value="r.value" />
          </el-select>
        </el-form-item>
        <el-form-item :label="$t('certs.effectiveFrom')">
          <el-date-picker v-model="revokeForm.effectiveFrom" type="datetime" :placeholder="$t('certs.effectivePlaceholder')" style="width: 100%" />
        </el-form-item>
        <el-form-item :label="$t('certs.confirmSerial')">
          <el-input v-model="revokeForm.confirmSerial" class="mono" :placeholder="$t('certs.confirmSerialPlaceholder')" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="revokeDlg = false">{{ $t('common.cancel') }}</el-button>
        <el-button type="danger" :loading="revoking" @click="doRevoke">{{ $t('certs.confirmRevoke') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { api } from '../api.js'
import { store, roleAtLeast } from '../store.js'
import { fmtDay } from '../i18n.js'

const router = useRouter()
const { t } = useI18n()
const canOperate = computed(() => roleAtLeast(store.user?.role, 'Operator'))
const status = ref('all')
const keyword = ref('')
const range = ref(null)
const loading = ref(false)
const result = ref({ items: [], totalFetched: 0 })
const page = ref(1)
const limit = 50

const revokeDlg = ref(false)
const revokeTarget = ref(null)
const revoking = ref(false)
const revokeForm = ref({ reason: 0, effectiveFrom: null, confirmSerial: '' })
const reasonOptions = [
  { value: 0 }, { value: 1 }, { value: 2 }, { value: 3 }, { value: 4 }, { value: 5 }, { value: 6 },
]

function statusTag(s) {
  return { issued: 'success', revoked: 'danger', pending: 'warning', denied: 'info', failed: 'danger', cacert: '', kracert: '', foreign: 'info' }[s] || 'info'
}
function statusText(s) {
  const key = `status.${s}`
  return t(key) === key ? s : t(key)
}
const reasonText = (r) => r == null ? '' : (t(`reason.${r}`) === `reason.${r}` ? String(r) : t(`reason.${r}`))
const expired = (row) => row.notAfter && new Date(row.notAfter) < new Date()

async function reload(reset) {
  if (reset) page.value = 1
  loading.value = true
  try {
    const params = new URLSearchParams({ status: status.value, limit: String(limit) })
    if (keyword.value) params.set('keyword', keyword.value)
    if (range.value?.length === 2) { params.set('from', range.value[0]); params.set('to', range.value[1]) }
    const before = result.value.items[(page.value - 1) * limit - 1]?.requestId
    if (page.value > 1 && before) params.set('before', before)
    result.value = await api.get('/api/certificates?' + params.toString())
  } catch (e) {
    ElMessage.error(e.message)
  } finally {
    loading.value = false
  }
}

function onPage(p) { page.value = p; reload(false) }
function goDetail(row) { router.push('/certificates/' + row.requestId) }

function downloadCerts(row) {
  api.download(`/api/certificates/${row.requestId}/download?format=cer`, `cert-${row.requestId}.cer`)
}

function exportCsv() {
  const params = new URLSearchParams({ status: status.value })
  if (keyword.value) params.set('keyword', keyword.value)
  if (range.value?.length === 2) { params.set('from', range.value[0]); params.set('to', range.value[1]) }
  api.download('/api/certificates/export?' + params.toString(), 'certificates.csv')
}

function openRevoke(row) {
  revokeTarget.value = row
  revokeForm.value = { reason: 0, effectiveFrom: null, confirmSerial: '' }
  revokeDlg.value = true
}

async function doRevoke() {
  const target = revokeTarget.value
  if (!target) return
  if (revokeForm.value.confirmSerial.trim().toLowerCase() !== (target.serialNumber || '').toLowerCase()) {
    return ElMessage.warning(t('certs.serialMismatch'))
  }
  revoking.value = true
  try {
    await api.post(`/api/certificates/${target.requestId}/revoke`, {
      serialNumber: target.serialNumber,
      reason: revokeForm.value.reason,
      effectiveFrom: revokeForm.value.effectiveFrom,
      confirmSerial: revokeForm.value.confirmSerial,
    })
    ElMessage.success(t('certs.revokedOk'))
    revokeDlg.value = false
    reload(false)
  } catch (e) {
    ElMessage.error(e.message)
  } finally {
    revoking.value = false
  }
}

async function unrevoke(row) {
  try {
    await ElMessageBox.confirm(t('certs.unrevokeConfirm'), t('certs.unrevokeTitle'), { type: 'warning' })
    await api.post(`/api/certificates/${row.requestId}/unrevoke`)
    ElMessage.success(t('certs.unrevokedOk'))
    reload(false)
  } catch (e) {
    if (e !== 'cancel') ElMessage.error(e.message)
  }
}

onMounted(() => reload(true))
</script>
