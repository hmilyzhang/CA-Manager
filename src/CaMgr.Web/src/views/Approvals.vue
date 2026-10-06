<template>
  <div class="page">
    <div class="page-title">{{ $t('approvals.title') }}</div>
    <el-alert type="info" :closable="false" style="margin-bottom: 14px" :title="$t('approvals.hint')" />

    <div class="filter-bar">
      <el-radio-group v-model="status" @change="reload">
        <el-radio-button value="pending">{{ $t('status.pending') }}</el-radio-button>
        <el-radio-button value="approved">{{ $t('status.issued') }}</el-radio-button>
        <el-radio-button value="rejected">{{ $t('status.denied') }}</el-radio-button>
        <el-radio-button value="all">{{ $t('status.all') }}</el-radio-button>
      </el-radio-group>
    </div>

    <el-table :data="items" v-loading="loading" stripe>
      <el-table-column prop="id" label="#" width="60" />
      <el-table-column prop="username" :label="$t('common.user')" width="110" />
      <el-table-column :label="$t('common.status')" width="100">
        <template #default="{ row }">
          <el-tag :type="statusTag(row.status)" size="small">{{ statusText(row.status) }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column :label="$t('common.subject')" min-width="170" show-overflow-tooltip>
        <template #default="{ row }">{{ row.commonName }}</template>
      </el-table-column>
      <el-table-column :label="$t('newReq.san')" min-width="140" show-overflow-tooltip>
        <template #default="{ row }">{{ row.san || '—' }}</template>
      </el-table-column>
      <el-table-column prop="template" :label="$t('common.template')" min-width="110" />
      <el-table-column :label="$t('common.time')" width="150">
        <template #default="{ row }">{{ fmt(row.createdAt) }}</template>
      </el-table-column>
      <el-table-column :label="$t('common.requestId')" width="95">
        <template #default="{ row }">{{ row.requestId || '—' }}</template>
      </el-table-column>
      <el-table-column prop="decidedBy" :label="$t('approvals.decidedBy')" width="100" />
      <el-table-column :label="$t('common.action')" min-width="220">
        <template #default="{ row }">
          <template v-if="row.status === 'pending' && canDecide">
            <el-button size="small" type="success" plain @click="approve(row)">{{ $t('approvals.approve') }}</el-button>
            <el-button size="small" type="danger" plain @click="reject(row)">{{ $t('approvals.reject') }}</el-button>
          </template>
          <el-button v-if="row.downloadable && row.type === 'self' && (row.own || canDecide)" size="small" type="primary" plain
            @click="openPfx(row)">{{ $t('approvals.downloadPfx') }}</el-button>
          <el-button v-if="row.downloadable && row.type === 'pgp' && (row.own || canDecide)" size="small" type="primary" plain
            @click="downloadPgp(row)">{{ $t('approvals.downloadPgp') }}</el-button>
          <el-button v-if="row.requestId" size="small" @click="$router.push('/certificates/' + row.requestId)">
            {{ $t('common.detail') }}
          </el-button>
        </template>
      </el-table-column>
    </el-table>
    <div v-if="!loading && items.length === 0" style="color:#909399;padding:30px;text-align:center">{{ $t('requests.empty') }}</div>

    <el-dialog v-model="pfxDlg" :title="$t('approvals.pfxTitle')" width="420">
      <el-alert type="warning" :closable="false" style="margin-bottom: 12px" :title="$t('approvals.pfxOnce')" />
      <el-form label-width="110px">
        <el-form-item :label="$t('pgp.passphrase')">
          <el-input v-model="pfxPassword" type="password" show-password :placeholder="$t('approvals.pfxPwdPh')" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="pfxDlg = false">{{ $t('common.cancel') }}</el-button>
        <el-button type="primary" :loading="downloading" @click="doDownloadPfx">{{ $t('approvals.downloadPfx') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { computed, onMounted, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { api } from '../api.js'
import { store, roleAtLeast } from '../store.js'
import { fmtDate } from '../i18n.js'

const { t } = useI18n()
const canDecide = computed(() => roleAtLeast(store.user?.role, 'Operator'))
const status = ref('pending')
const items = ref([])
const loading = ref(false)
const pfxDlg = ref(false)
const pfxRow = ref(null)
const pfxPassword = ref('')
const downloading = ref(false)

const fmt = (d) => fmtDate(d)
function statusTag(s) { return { pending: 'warning', approved: 'success', rejected: 'danger' }[s] || 'info' }
function statusText(s) {
  const key = `approvals.st_${s}`
  return t(key) === key ? s : t(key)
}

async function reload() {
  loading.value = true
  try {
    items.value = await api.get('/api/approvals?status=' + status.value)
  } catch (e) {
    ElMessage.error(e.message)
  } finally {
    loading.value = false
  }
}

async function approve(row) {
  try {
    await ElMessageBox.confirm(t('approvals.approveConfirm', { id: row.id, cn: row.commonName }), t('approvals.approve'), { type: 'info' })
  } catch { return }
  try {
    const r = await api.post(`/api/approvals/${row.id}/approve`)
    ElMessage.success(r.message)
    reload()
  } catch (e) { ElMessage.error(e.message) }
}

async function reject(row) {
  try {
    const { value } = await ElMessageBox.prompt(t('approvals.rejectPrompt', { id: row.id }), t('approvals.reject'), { inputType: 'textarea' })
    await api.post(`/api/approvals/${row.id}/reject`, { comment: value || '' })
    ElMessage.success(t('approvals.rejectedOk'))
    reload()
  } catch (e) { if (e !== 'cancel') ElMessage.error(e.message) }
}

async function downloadPgp(row) {
  try {
    await ElMessageBox.confirm(t('approvals.pgpOnce'), t('approvals.downloadPgp'), { type: 'warning' })
  } catch { return }
  try {
    const r = await api.post(`/api/approvals/${row.id}/pgp`)
    dlText(r.privateKeyAsc, `${r.fileNameBase}-priv.asc`)
    dlText(r.publicKeyAsc, `${r.fileNameBase}-pub.asc`)
    ElMessage.success(t('approvals.pgpDownloaded'))
    reload()
  } catch (e) { ElMessage.error(e.message) }
}

function dlText(text, filename) {
  const a = document.createElement('a')
  a.href = URL.createObjectURL(new Blob([text], { type: 'application/pgp-keys' }))
  a.download = filename
  a.click()
  URL.revokeObjectURL(a.href)
}

function openPfx(row) {
  pfxRow.value = row
  pfxPassword.value = ''
  pfxDlg.value = true
}

async function doDownloadPfx() {
  if (!pfxPassword.value) return ElMessage.warning(t('approvals.pfxPwdPh'))
  downloading.value = true
  try {
    const resp = await fetch(`/api/approvals/${pfxRow.value.id}/pfx`, {
      method: 'POST', credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ password: pfxPassword.value }),
    })
    if (!resp.ok) {
      const d = await resp.json().catch(() => ({}))
      throw new Error(d.error || `HTTP ${resp.status}`)
    }
    const blob = await resp.blob()
    const a = document.createElement('a')
    a.href = URL.createObjectURL(blob)
    a.download = pfxRow.value.commonName + '.pfx'
    a.click()
    URL.revokeObjectURL(a.href)
    pfxDlg.value = false
    ElMessage.success(t('approvals.pfxDownloaded'))
    reload()
  } catch (e) {
    ElMessage.error(e.message)
  } finally {
    downloading.value = false
  }
}

onMounted(reload)
</script>
