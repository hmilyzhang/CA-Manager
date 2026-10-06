<template>
  <div class="page">
    <div class="page-title">{{ $t('templates.title') }}</div>
    <el-alert type="info" :closable="false" style="margin-bottom: 14px" :title="$t('templates.notice')" />

    <el-table :data="templates" v-loading="loading" stripe>
      <el-table-column prop="name" :label="$t('templates.name')" min-width="200" />
      <el-table-column prop="displayName" :label="$t('common.displayName')" min-width="180" />
      <el-table-column prop="oid" :label="$t('common.oid')" min-width="220" show-overflow-tooltip>
        <template #default="{ row }"><span class="mono" style="font-size: 12px">{{ row.oid }}</span></template>
      </el-table-column>
      <el-table-column prop="validityPeriod" :label="$t('common.validity')" width="110" />
      <el-table-column prop="purpose" :label="$t('common.purpose')" min-width="180" show-overflow-tooltip />
      <el-table-column v-if="isAdmin" :label="$t('common.action')" width="110">
        <template #default="{ row }">
          <el-button size="small" type="warning" plain @click="toggle(row, false)">{{ $t('common.disable') }}</el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-card v-if="isAdmin" shadow="never" style="margin-top: 14px">
      <template #header>{{ $t('templates.enableNew') }}</template>
      <div style="display: flex; gap: 10px">
        <el-input v-model="newTemplate" :placeholder="$t('templates.namePlaceholder')" style="width: 300px" />
        <el-button type="primary" @click="toggle({ name: newTemplate }, true)" :disabled="!newTemplate">{{ $t('common.enable') }}</el-button>
      </div>
    </el-card>
  </div>
</template>

<script setup>
import { computed, onMounted, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { api } from '../api.js'
import { store, roleAtLeast } from '../store.js'

const { t } = useI18n()
const isAdmin = computed(() => roleAtLeast(store.user?.role, 'Admin'))
const templates = ref([])
const loading = ref(false)
const newTemplate = ref('')

async function reload() {
  loading.value = true
  try {
    templates.value = await api.get('/api/templates')
  } catch (e) {
    ElMessage.error(e.message)
  } finally {
    loading.value = false
  }
}

async function toggle(row, enable) {
  const action = enable ? t('common.enable') : t('common.disable')
  try {
    await ElMessageBox.confirm(
      t(enable ? 'templates.toggleConfirmOn' : 'templates.toggleConfirmOff', { name: row.name }),
      t(enable ? 'templates.toggleTitleOn' : 'templates.toggleTitleOff'),
      { type: enable ? 'info' : 'warning' })
  } catch { return }
  try {
    await api.post('/api/templates/toggle', { templateName: row.name, enable })
    ElMessage.success(t(enable ? 'templates.enabledOk' : 'templates.disabledOk'))
    newTemplate.value = ''
    reload()
  } catch (e) {
    ElMessage.error(e.message)
  }
}

onMounted(reload)
</script>
