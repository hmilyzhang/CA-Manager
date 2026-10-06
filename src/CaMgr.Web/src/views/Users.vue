<template>
  <div class="page">
    <div class="page-title">{{ $t('users.title') }}</div>

    <div class="filter-bar">
      <el-button type="primary" @click="openCreate"><el-icon><Plus /></el-icon>&nbsp;{{ $t('users.create') }}</el-button>
      <span style="color:#909399;font-size:13px">{{ $t('users.hint') }}</span>
    </div>

    <el-table :data="users" v-loading="loading" stripe>
      <el-table-column prop="username" :label="$t('users.username')" min-width="140" />
      <el-table-column prop="displayName" :label="$t('common.displayName')" min-width="120" />
      <el-table-column :label="$t('common.source')" width="90">
        <template #default="{ row }">
          <el-tag size="small" :type="row.source === 'Local' ? 'info' : 'success'">{{ row.source === 'Local' ? $t('users.local') : $t('users.ad') }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column :label="$t('common.role')" width="130">
        <template #default="{ row }">
          <el-select :model-value="row.role" size="small" style="width: 115px" @change="(v) => changeRole(row, v)"
            :disabled="row.username === 'admin'">
            <el-option value="Admin" :label="$t('role.Admin')" />
            <el-option value="Operator" :label="$t('role.Operator')" />
            <el-option value="Viewer" :label="$t('role.Viewer')" />
          </el-select>
        </template>
      </el-table-column>
      <el-table-column :label="$t('common.status')" width="90">
        <template #default="{ row }">
          <el-tag :type="row.enabled ? 'success' : 'danger'" size="small">{{ row.enabled ? $t('common.enable') : $t('common.disable') }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column :label="$t('common.lastLogin')" width="160">
        <template #default="{ row }">{{ fmt(row.lastLoginAt) }}</template>
      </el-table-column>
      <el-table-column :label="$t('common.action')" width="270">
        <template #default="{ row }">
          <el-button size="small" @click="resetPassword(row)">{{ $t('users.resetPwd') }}</el-button>
          <el-button size="small" :type="row.enabled ? 'warning' : 'success'" plain @click="toggleEnabled(row)"
            :disabled="row.username === 'admin'">{{ row.enabled ? $t('common.disable') : $t('common.enable') }}</el-button>
          <el-button size="small" type="danger" plain @click="remove(row)" :disabled="row.username === 'admin'">{{ $t('common.delete') }}</el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="createDlg" :title="$t('users.createTitle')" width="440">
      <el-form label-width="110px">
        <el-form-item :label="$t('users.username')"><el-input v-model="createForm.username" /></el-form-item>
        <el-form-item :label="$t('common.displayName')"><el-input v-model="createForm.displayName" /></el-form-item>
        <el-form-item :label="$t('common.role')">
          <el-select v-model="createForm.role" style="width: 100%">
            <el-option value="Viewer" :label="$t('role.Viewer')" />
            <el-option value="Operator" :label="$t('role.Operator')" />
            <el-option value="Admin" :label="$t('role.Admin')" />
          </el-select>
        </el-form-item>
        <el-form-item :label="$t('users.initPwd')"><el-input v-model="createForm.password" show-password :placeholder="$t('users.initPwdPlaceholder')" /></el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="createDlg = false">{{ $t('common.cancel') }}</el-button>
        <el-button type="primary" @click="doCreate">{{ $t('common.confirm') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { onMounted, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { api } from '../api.js'
import { fmtDate } from '../i18n.js'

const { t } = useI18n()
const users = ref([])
const loading = ref(false)
const createDlg = ref(false)
const createForm = ref({ username: '', displayName: '', role: 'Operator', password: '' })
const fmt = (d) => fmtDate(d)

async function reload() {
  loading.value = true
  try {
    users.value = await api.get('/api/users')
  } catch (e) {
    ElMessage.error(e.message)
  } finally {
    loading.value = false
  }
}

function openCreate() {
  createForm.value = { username: '', displayName: '', role: 'Operator', password: '' }
  createDlg.value = true
}

async function doCreate() {
  try {
    await api.post('/api/users', createForm.value)
    ElMessage.success(t('users.created'))
    createDlg.value = false
    reload()
  } catch (e) { ElMessage.error(e.message) }
}

async function changeRole(row, role) {
  try {
    await api.put('/api/users/' + row.id, { role })
    ElMessage.success(t('users.roleUpdated'))
    reload()
  } catch (e) { ElMessage.error(e.message) }
}

async function resetPassword(row) {
  try {
    const { value } = await ElMessageBox.prompt(t('users.resetPrompt', { name: row.username }), t('users.resetTitle'), { inputType: 'password' })
    if (!value || value.length < 8) return ElMessage.warning(t('users.pwdLen'))
    await api.put('/api/users/' + row.id, { password: value, resetMustChange: true })
    ElMessage.success(t('users.pwdReset'))
  } catch (e) { if (e !== 'cancel') ElMessage.error(e.message) }
}

async function toggleEnabled(row) {
  try {
    await api.put('/api/users/' + row.id, { enabled: !row.enabled })
    ElMessage.success(t('users.updated'))
    reload()
  } catch (e) { ElMessage.error(e.message) }
}

async function remove(row) {
  try {
    await ElMessageBox.confirm(t('users.deleteConfirm', { name: row.username }), t('users.deleteTitle'), { type: 'warning' })
    await api.del('/api/users/' + row.id)
    ElMessage.success(t('users.deleted'))
    reload()
  } catch (e) { if (e !== 'cancel') ElMessage.error(e.message) }
}

onMounted(reload)
</script>
