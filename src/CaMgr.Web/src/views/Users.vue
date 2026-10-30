<template>
  <div class="page">
    <div class="page-title">{{ $t('users.title') }}</div>

    <el-tabs v-model="tab" type="card" class="user-tabs">
      <el-tab-pane :label="$t('users.tabList')" name="list">
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
            <el-option value="Auditor" :label="$t('role.Auditor')" />
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
      <el-table-column :label="$t('common.action')" width="330" class-name="nowrap-cell">
        <template #default="{ row }">
          <div style="display: flex; gap: 6px; flex-wrap: nowrap; align-items: center">
            <el-tooltip v-if="row.source === 'Ad'" :content="$t('users.adPwdHint')">
            <el-button size="small" disabled>{{ $t('users.resetPwd') }}</el-button>
          </el-tooltip>
          <el-button v-else size="small" @click="resetPassword(row)">{{ $t('users.resetPwd') }}</el-button>
            <el-button size="small" :type="row.enabled ? 'warning' : 'success'" plain @click="toggleEnabled(row)"
              :disabled="row.username === 'admin'">{{ row.enabled ? $t('common.disable') : $t('common.enable') }}</el-button>
            <el-button size="small" type="danger" plain @click="remove(row)" :disabled="row.username === 'admin'">{{ $t('common.delete') }}</el-button>
          </div>
        </template>
      </el-table-column>
    </el-table>
      </el-tab-pane>

      <el-tab-pane :label="$t('apitokens.tab')" name="tokens">
        <div class="filter-bar">
          <el-button type="primary" @click="tokenDlg = true"><el-icon><Plus /></el-icon>&nbsp;{{ $t('apitokens.create') }}</el-button>
          <span style="color:#909399;font-size:13px">{{ $t('apitokens.hint') }}</span>
        </div>
        <el-table :data="apiTokens" v-loading="tokensLoading" stripe>
          <el-table-column prop="id" label="#" width="60" />
          <el-table-column prop="name" :label="$t('apitokens.name')" min-width="180" />
          <el-table-column prop="createdBy" :label="$t('apitokens.createdBy')" min-width="110" />
          <el-table-column :label="$t('pgp.validity')" width="200">
            <template #default="{ row }">{{ fmt(row.createdAt) }} → {{ fmt(row.expiresAt) }}</template>
          </el-table-column>
          <el-table-column :label="$t('apitokens.lastUsed')" width="160">
            <template #default="{ row }">{{ fmt(row.lastUsedAt) }}</template>
          </el-table-column>
          <el-table-column :label="$t('common.status')" width="90">
            <template #default="{ row }">
              <el-tag :type="row.enabled && !row.expired ? 'success' : 'danger'" size="small">
                {{ row.enabled && !row.expired ? $t('common.enable') : $t('apitokens.invalid') }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column :label="$t('common.action')" width="110">
            <template #default="{ row }">
              <el-button size="small" type="danger" plain @click="revokeToken(row)">{{ $t('common.delete') }}</el-button>
            </template>
          </el-table-column>
        </el-table>
      </el-tab-pane>

      <el-tab-pane :label="$t('users.tabAd')" name="ldap">
        <el-card shadow="never">
          <template #header>{{ $t('ldap.title') }}</template>
      <el-form label-width="210px">
        <el-form-item :label="$t('ldap.enabled')">
          <el-switch v-model="ldap.enabled" />
        </el-form-item>
        <el-form-item :label="$t('ldap.host')">
          <el-input v-model="ldap.host" :placeholder="$t('ldap.hostPh')" style="width: 300px" />
        </el-form-item>
        <el-form-item :label="$t('ldap.domain')">
          <el-input v-model="ldap.domain" placeholder="HOME" style="width: 300px" />
        </el-form-item>
        <el-form-item :label="$t('ldap.adminGroup')">
          <el-input v-model="ldap.adminGroup" placeholder="CA-Manager-Admins" style="width: 300px" />
        </el-form-item>
        <el-form-item :label="$t('ldap.operatorGroup')">
          <el-input v-model="ldap.operatorGroup" placeholder="CA-Manager-Operators" style="width: 300px" />
        </el-form-item>
        <el-form-item :label="$t('ldap.allowUnmapped')">
          <el-switch v-model="ldap.allowUnmapped" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :loading="ldapSaving" @click="saveLdap">{{ $t('ldap.save') }}</el-button>
        </el-form-item>
      </el-form>
        </el-card>
      </el-tab-pane>
    </el-tabs>

    <el-dialog v-model="createDlg" :title="$t('users.createTitle')" width="440">
      <el-form label-width="110px">
        <el-form-item :label="$t('users.username')"><el-input v-model="createForm.username" /></el-form-item>
        <el-form-item :label="$t('common.displayName')"><el-input v-model="createForm.displayName" /></el-form-item>
        <el-form-item :label="$t('common.role')">
          <el-select v-model="createForm.role" style="width: 100%">
            <el-option value="Viewer" :label="$t('role.Viewer')" />
            <el-option value="Operator" :label="$t('role.Operator')" />
            <el-option value="Auditor" :label="$t('role.Auditor')" />
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

    <el-dialog v-model="tokenDlg" :title="$t('apitokens.create')" width="440">
      <el-form label-width="110px">
        <el-form-item :label="$t('apitokens.name')"><el-input v-model="tokenName" placeholder="web-server-01" /></el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="tokenDlg = false">{{ $t('common.cancel') }}</el-button>
        <el-button type="primary" :loading="tokenSaving" @click="createToken">{{ $t('apitokens.create') }}</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="createdToken.show" title="API Token" width="520" :close-on-click-modal="false">
      <el-alert type="warning" :closable="false" style="margin-bottom: 10px" :title="$t('apitokens.onceWarning')" />
      <el-input :model-value="createdToken.plaintext" readonly class="mono" style="font-size: 13px">
        <template #append>
          <el-button @click="copyToken">{{ $t('pgp.copyPub') }}</el-button>
        </template>
      </el-input>
      <template #footer>
        <el-button type="primary" @click="createdToken.show = false">{{ $t('common.confirm') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { api } from '../api.js'
import { fmtDate } from '../i18n.js'

const { t } = useI18n()
const tab = ref('list')
const users = ref([])
const loading = ref(false)
const createDlg = ref(false)
const createForm = ref({ username: '', displayName: '', role: 'Operator', password: '' })
const ldap = reactive({ enabled: false, host: '', domain: '', adminGroup: '', operatorGroup: '', allowUnmapped: true })
const ldapSaving = ref(false)
const apiTokens = ref([])
const tokensLoading = ref(false)
const tokenDlg = ref(false)
const tokenName = ref('')
const tokenSaving = ref(false)
const createdToken = reactive({ plaintext: '', expiresAt: '', show: false })
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

async function loadTokens() {
  tokensLoading.value = true
  try { apiTokens.value = await api.get('/api/apitokens') } catch {} finally { tokensLoading.value = false }
}

async function createToken() {
  if (!tokenName.value.trim()) return ElMessage.warning(t('apitokens.nameRequired'))
  tokenSaving.value = true
  try {
    const r = await api.post('/api/apitokens', { name: tokenName.value.trim() })
    createdToken.plaintext = r.plaintext
    createdToken.expiresAt = fmt(r.info.expiresAt)
    createdToken.show = true
    tokenDlg.value = false
    tokenName.value = ''
    loadTokens()
  } catch (e) { ElMessage.error(e.message) } finally { tokenSaving.value = false }
}

async function revokeToken(row) {
  try {
    await ElMessageBox.confirm(t('apitokens.revokeConfirm', { name: row.name }), t('apitokens.revoke'), { type: 'warning' })
    await api.post('/api/apitokens/revoke', { id: row.id })
    ElMessage.success(t('apitokens.revoked'))
    loadTokens()
  } catch (e) { if (e !== 'cancel') ElMessage.error(e.message) }
}

async function loadLdap() {
  try {
    const st = await api.get('/api/settings')
    ldap.enabled = st['ldap.enabled'] === 'true'
    ldap.host = st['ldap.host'] || ''
    ldap.domain = st['ldap.domain'] || ''
    ldap.adminGroup = st['ldap.adminGroup'] || ''
    ldap.operatorGroup = st['ldap.operatorGroup'] || ''
    ldap.allowUnmapped = st['ldap.allowUnmapped'] !== 'false'
  } catch { /* settings may be empty on fresh installs */ }
}

async function saveLdap() {
  ldapSaving.value = true
  try {
    await api.put('/api/settings', {
      'ldap.enabled': ldap.enabled ? 'true' : 'false',
      'ldap.host': ldap.host,
      'ldap.domain': ldap.domain,
      'ldap.adminGroup': ldap.adminGroup,
      'ldap.operatorGroup': ldap.operatorGroup,
      'ldap.allowUnmapped': ldap.allowUnmapped ? 'true' : 'false',
    })
    ElMessage.success(t('ldap.saved'))
  } catch (e) { ElMessage.error(e.message) } finally { ldapSaving.value = false }
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

onMounted(() => { reload(); loadLdap(); loadTokens() })
</script>
