<template>
  <div class="page">
    <div class="page-title">{{ $t('inventory.title') }}</div>

    <el-alert type="info" :closable="false" style="margin-bottom: 14px" :title="$t('inventory.hint')" />

    <div class="filter-bar">
      <el-radio-group v-model="itemType" @change="reload">
        <el-radio-button value="all">{{ $t('inventory.tAll') }}</el-radio-button>
        <el-radio-button value="x509">{{ $t('inventory.tX509') }}</el-radio-button>
        <el-radio-button value="pgp">{{ $t('inventory.tPgp') }}</el-radio-button>
        <el-radio-button value="imported">{{ $t('inventory.tImported') }}</el-radio-button>
      </el-radio-group>
      <el-radio-group v-model="status" @change="reload">
        <el-radio-button value="all">{{ $t('status.all') }}</el-radio-button>
        <el-radio-button value="active">{{ $t('inventory.stActive') }}</el-radio-button>
        <el-radio-button value="expired">{{ $t('status.expired') }}</el-radio-button>
        <el-radio-button value="revoked">{{ $t('status.revoked') }}</el-radio-button>
        <el-radio-button value="retired">{{ $t('inventory.stRetired') }}</el-radio-button>
      </el-radio-group>
      <el-button @click="exportCsv"><el-icon><Download /></el-icon>&nbsp;{{ $t('common.export') }}</el-button>
      <span v-if="canSync" style="margin-left: auto"></span>
      <el-button v-if="canSync" plain @click="sync">{{ $t('inventory.sync') }}</el-button>
    </div>

    <el-table :data="items" v-loading="loading" stripe @row-click="onRowClick" row-class-name="clickable">
      <el-table-column :label="$t('inventory.type')" width="95">
        <template #default="{ row }">{{ $t('inventory.' + row.itemType) }}</template>
      </el-table-column>
      <el-table-column prop="subject" :label="$t('common.subject')" min-width="200" show-overflow-tooltip />
      <el-table-column :label="$t('inventory.owner')" width="110">
        <template #default="{ row }">{{ row.owner || '—' }}</template>
      </el-table-column>
      <el-table-column :label="$t('inventory.assignedTo')" min-width="130" show-overflow-tooltip>
        <template #default="{ row }">{{ row.assignedTo || '—' }}</template>
      </el-table-column>
      <el-table-column :label="$t('common.notAfter')" width="105">
        <template #default="{ row }">{{ new Date(row.expiresAt).toLocaleDateString() }}</template>
      </el-table-column>
      <el-table-column :label="$t('inventory.daysLeft')" width="105">
        <template #default="{ row }">
          <el-tag :type="daysLeft(row) < 0 ? 'danger' : daysLeft(row) <= 15 ? 'warning' : 'success'" size="small">
            {{ daysLeft(row) < 0 ? $t('dashboard.expiredDays', { n: -daysLeft(row) }) : daysLeft(row) + ' ' + $t('common.days') }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column :label="$t('common.status')" width="95">
        <template #default="{ row }">
          <el-tag :type="statusTag(row)" size="small">{{ statusText(row) }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column :label="$t('common.action')" width="170">
        <template #default="{ row }">
          <el-button v-if="row.certRequestId" size="small" @click.stop="$router.push('/certificates/' + row.certRequestId)">{{ $t('common.detail') }}</el-button>
          <el-button v-if="canEdit(row)" size="small" plain @click.stop="openEdit(row)">{{ $t('inventory.edit') }}</el-button>
        </template>
      </el-table-column>
    </el-table>
    <div v-if="!loading && items.length === 0" style="color:#909399;padding:30px;text-align:center">{{ $t('inventory.empty') }}</div>

    <el-dialog v-model="editDlg" :title="$t('inventory.edit')" width="480">
      <el-form label-width="130px">
        <el-form-item :label="$t('inventory.owner')"><el-input v-model="editForm.owner" /></el-form-item>
        <el-form-item :label="$t('inventory.assignedTo')"><el-input v-model="editForm.assignedTo" /></el-form-item>
        <el-form-item :label="$t('common.detail')"><el-input v-model="editForm.notes" type="textarea" :rows="2" /></el-form-item>
        <el-form-item :label="$t('inventory.status')">
          <el-select v-model="editForm.status" style="width: 200px">
            <el-option value="active" :label="$t('inventory.stActive')" />
            <el-option value="expired" :label="$t('status.expired')" />
            <el-option value="revoked" :label="$t('status.revoked')" />
            <el-option value="retired" :label="$t('inventory.stRetired')" />
          </el-select>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="editDlg = false">{{ $t('common.cancel') }}</el-button>
        <el-button type="primary" :loading="saving" @click="saveEdit">{{ $t('common.confirm') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { computed, onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { api } from '../api.js'
import { store, roleAtLeast } from '../store.js'

const { t } = useI18n()
const itemType = ref('all')
const status = ref('all')
const items = ref([])
const loading = ref(false)
const editDlg = ref(false)
const editRow = ref(null)
const editForm = ref({ owner: '', assignedTo: '', notes: '', status: 'active' })
const saving = ref(false)

const canSync = computed(() => roleAtLeast(store.user?.role, 'Operator'))
const canEdit = (row) => roleAtLeast(store.user?.role, 'Operator') || row.own

const fmt = (d) => d ? new Date(d).toLocaleDateString() : '—'
function daysLeft(row) {
  return Math.floor((new Date(row.expiresAt) - new Date()) / 86400000)
}
function statusTag(row) {
  const s = row.status === 'active' && row.expired ? 'expired' : row.status
  return { active: 'success', expired: 'danger', revoked: 'danger', retired: 'info' }[s] || 'info'
}
function statusText(row) {
  const s = row.status === 'active' && row.expired ? 'expired' : row.status
  const key = `inventory.st_${s}`
  return t(key) === key ? s : t(key)
}

async function reload() {
  loading.value = true
  try {
    items.value = await api.get(`/api/inventory?itemType=${itemType.value}&status=${status.value}`)
  } catch (e) {
    ElMessage.error(e.message)
  } finally {
    loading.value = false
  }
}

function onRowClick(row) {
  if (!canEdit(row)) return
  editRow.value = row
  editForm.value = { owner: row.owner, assignedTo: row.assignedTo, notes: row.notes, status: row.status }
  editDlg.value = true
}

async function saveEdit() {
  saving.value = true
  try {
    await api.post(`/api/inventory/${editRow.value.id}/meta`, editForm.value)
    ElMessage.success(t('users.updated'))
    editDlg.value = false
    reload()
  } catch (e) { ElMessage.error(e.message) } finally { saving.value = false }
}

async function sync() {
  try {
    const r = await api.post('/api/inventory/sync')
    ElMessage.success(r.message)
    reload()
  } catch (e) { ElMessage.error(e.message) }
}

function exportCsv() {
  api.download('/api/inventory/export?itemType=' + itemType.value + '&status=' + status.value, 'inventory.csv')
}

onMounted(reload)
</script>
