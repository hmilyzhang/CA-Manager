<template>
  <div class="page">
    <div class="page-title">{{ $t('system.title') }}</div>

    <el-alert type="info" :closable="false" style="margin-bottom: 14px" :title="$t('system.hint')" />

    <el-card shadow="never">
      <template #header>
        {{ $t('system.current') }}
        <el-tag :type="currentUrl?.startsWith('https') ? 'success' : 'warning'" size="small" style="margin-left: 8px">
          {{ currentUrl || 'HTTP' }}
        </el-tag>
      </template>
      <span style="color:#909399;font-size:13px">{{ $t('system.currentHint') }}</span>
    </el-card>

    <el-card shadow="never" style="margin-top: 14px">
      <template #header>{{ $t('system.certs') }}</template>
      <div class="filter-bar">
        <span style="color:#909399;font-size:13px">{{ $t('system.port') }}</span>
        <el-input-number v-model="port" :min="1" :max="65535" />
      </div>
      <el-table :data="certs" v-loading="loading" stripe>
        <el-table-column width="55">
          <template #default="{ row }">
            <el-radio :model-value="selected" :value="row.thumbprint" @change="selected = row.thumbprint">&nbsp;</el-radio>
          </template>
        </el-table-column>
        <el-table-column prop="subject" :label="$t('common.subject')" min-width="220" show-overflow-tooltip />
        <el-table-column :label="$t('common.notAfter')" width="110">
          <template #default="{ row }">{{ new Date(row.notAfter).toLocaleDateString() }}</template>
        </el-table-column>
        <el-table-column label="SAN" min-width="150" show-overflow-tooltip>
          <template #default="{ row }"><span class="mono" style="font-size:12px">{{ (row.san || []).join(', ') || '—' }}</span></template>
        </el-table-column>
        <el-table-column prop="thumbprint" :label="$t('certs.detail.sha1')" min-width="180">
          <template #default="{ row }"><span class="mono" style="font-size:12px">{{ row.thumbprint }}</span></template>
        </el-table-column>
      </el-table>
      <div style="margin-top: 12px">
        <el-button type="primary" :disabled="!selected" :loading="binding" @click="bind">
          {{ $t('system.bind') }}
        </el-button>
        <span v-if="selected" style="margin-left: 10px; color:#909399; font-size:13px">{{ $t('system.bindHint') }}</span>
      </div>
    </el-card>
  </div>
</template>

<script setup>
import { onMounted, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { api } from '../api.js'

const { t } = useI18n()
const certs = ref([])
const currentUrl = ref(null)
const selected = ref('')
const port = ref(8443)
const loading = ref(false)
const binding = ref(false)

async function load() {
  loading.value = true
  try {
    const d = await api.get('/api/system/certs')
    certs.value = d.certs || []
    currentUrl.value = d.current
    if (d.current) {
      const m = d.current.match(/:(\d+)$/)
      if (m) port.value = Number(m[1])
    }
  } catch (e) {
    ElMessage.error(e.message)
  } finally {
    loading.value = false
  }
}

async function bind() {
  try {
    await ElMessageBox.confirm(
      t('system.bindConfirm', { port: port.value }),
      t('system.bind'), { type: 'warning' })
  } catch { return }
  binding.value = true
  try {
    const r = await api.post('/api/system/https', { thumbprint: selected.value, port: port.value })
    ElMessage.success(r.message)
  } catch (e) {
    ElMessage.error(e.message)
  } finally {
    binding.value = false
  }
}

onMounted(load)
</script>
