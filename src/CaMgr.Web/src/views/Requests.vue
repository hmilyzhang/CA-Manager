<template>
  <div class="page">
    <div class="page-title">{{ $t('requests.title') }}</div>

    <div class="filter-bar">
      <el-radio-group v-model="status" @change="reload">
        <el-radio-button value="pending">{{ $t('status.pending') }}</el-radio-button>
        <el-radio-button value="denied">{{ $t('status.denied') }}</el-radio-button>
        <el-radio-button value="failed">{{ $t('status.failed') }}</el-radio-button>
        <el-radio-button value="all">{{ $t('status.all') }}</el-radio-button>
      </el-radio-group>
      <el-button type="primary" @click="$router.push('/new-request')" v-if="canOperate">
        <el-icon><Plus /></el-icon>&nbsp;{{ $t('requests.newRequest') }}
      </el-button>
    </div>

    <el-table :data="items" v-loading="loading" stripe>
      <el-table-column prop="requestId" :label="$t('common.requestId')" width="90" />
      <el-table-column :label="$t('common.commonName')" min-width="180" show-overflow-tooltip>
        <template #default="{ row }">{{ row.commonName || '—' }}</template>
      </el-table-column>
      <el-table-column :label="$t('common.status')" width="100">
        <template #default="{ row }">
          <el-tag :type="statusTag(row.status)" size="small">{{ statusText(row.status) }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column prop="template" :label="$t('common.template')" min-width="130" />
      <el-table-column prop="requester" :label="$t('common.requester')" min-width="130" show-overflow-tooltip />
      <el-table-column prop="dispositionMessage" :label="$t('common.message')" min-width="200" show-overflow-tooltip />
      <el-table-column :label="$t('common.submittedAt')" width="160">
        <template #default="{ row }">{{ fmt(row.submittedAt) }}</template>
      </el-table-column>
      <el-table-column v-if="canOperate" :label="$t('common.action')" width="230">
        <template #default="{ row }">
          <el-button size="small" type="success" plain @click="issue(row)"
            :disabled="!['pending', 'denied', 'failed'].includes(row.status)">{{ $t('common.issue') }}</el-button>
          <el-button size="small" type="danger" plain @click="deny(row)" :disabled="row.status !== 'pending'">{{ $t('common.deny') }}</el-button>
          <el-button size="small" @click="$router.push('/certificates/' + row.requestId)">{{ $t('common.detail') }}</el-button>
        </template>
      </el-table-column>
    </el-table>
    <div v-if="!loading && items.length === 0" style="color:#909399;padding:30px;text-align:center">{{ $t('requests.empty') }}</div>
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
const canOperate = computed(() => roleAtLeast(store.user?.role, 'Operator'))
const status = ref('pending')
const items = ref([])
const loading = ref(false)

function statusTag(s) {
  return { pending: 'warning', denied: 'info', failed: 'danger', issued: 'success' }[s] || 'info'
}
function statusText(s) {
  const key = `status.${s}`
  return t(key) === key ? s : t(key)
}
const fmt = (d) => fmtDate(d)

async function reload() {
  loading.value = true
  try {
    const r = await api.get('/api/requests/queue?status=' + status.value + '&limit=200')
    items.value = r.items
  } catch (e) {
    ElMessage.error(e.message)
  } finally {
    loading.value = false
  }
}

async function issue(row) {
  try {
    await ElMessageBox.confirm(
      t('requests.issueConfirm', { id: row.requestId, cn: row.commonName || t('requests.revokedPlaceholder') }),
      t('requests.issueTitle'), { type: 'info' })
  } catch { return }
  try {
    const r = await api.post(`/api/requests/${row.requestId}/issue`)
    ElMessage.success(r.disposition === 3 ? t('newReq.submittedOk') : r.disposition === 5 ? t('newReq.submittedPending') : r.message)
    reload()
  } catch (e) {
    ElMessage.error(e.message)
  }
}

async function deny(row) {
  try {
    await ElMessageBox.confirm(t('requests.denyConfirm', { id: row.requestId }), t('requests.denyTitle'), { type: 'warning' })
  } catch { return }
  try {
    await api.post(`/api/requests/${row.requestId}/deny`)
    ElMessage.success(t('requests.deniedOk'))
    reload()
  } catch (e) {
    ElMessage.error(e.message)
  }
}

onMounted(reload)
</script>
