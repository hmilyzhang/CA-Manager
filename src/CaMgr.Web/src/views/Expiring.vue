<template>
  <div class="page">
    <div class="page-title">{{ $t('expiring.title') }}</div>

    <el-alert v-if="caCertDays !== null && caCertDays < 90" type="warning" show-icon :closable="false" style="margin-bottom: 14px"
      :title="$t('expiring.caCertWarn', { date: caCertExpiry, days: caCertDays })" />

    <div class="filter-bar">
      <el-radio-group v-model="days" @change="reload">
        <el-radio-button :value="30">{{ $t('expiring.d30') }}</el-radio-button>
        <el-radio-button :value="60">{{ $t('expiring.d60') }}</el-radio-button>
        <el-radio-button :value="90">{{ $t('expiring.d90') }}</el-radio-button>
      </el-radio-group>
      <span style="color:#909399;font-size:13px">{{ $t('expiring.summary', { n: items.length }) }}</span>
    </div>

    <el-table :data="items" v-loading="loading" stripe @row-click="goDetail" row-class-name="clickable">
      <el-table-column prop="requestId" :label="$t('common.requestId')" width="90" />
      <el-table-column prop="commonName" :label="$t('common.commonName')" min-width="200" show-overflow-tooltip />
      <el-table-column prop="template" :label="$t('common.template')" min-width="150" />
      <el-table-column prop="requester" :label="$t('common.requester')" min-width="130" show-overflow-tooltip />
      <el-table-column :label="$t('common.notAfter')" width="170">
        <template #default="{ row }">{{ fmt(row.notAfter) }}</template>
      </el-table-column>
      <el-table-column :label="$t('expiring.daysLeft')" width="110">
        <template #default="{ row }">
          <el-tag :type="leftDays(row) < 15 ? 'danger' : 'warning'" size="small">{{ leftDays(row) }} {{ $t('common.days') }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="" width="90">
        <template #default="{ row }">
          <el-button size="small" @click.stop="goDetail(row)">{{ $t('common.detail') }}</el-button>
        </template>
      </el-table-column>
    </el-table>
  </div>
</template>

<script setup>
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { api } from '../api.js'
import { fmtDate } from '../i18n.js'

const router = useRouter()
const days = ref(30)
const items = ref([])
const loading = ref(false)
const caCertExpiry = ref(null)
const caCertDays = ref(null)

const fmt = (d) => fmtDate(d)
const leftDays = (row) => Math.max(0, Math.floor((new Date(row.notAfter) - new Date()) / 86400000))

async function reload() {
  loading.value = true
  try {
    const r = await api.get(`/api/certificates?status=expiring&expiringDays=${days.value}&limit=500`)
    items.value = r.items
  } catch (e) {
    ElMessage.error(e.message)
  } finally {
    loading.value = false
  }
}

function goDetail(row) { router.push('/certificates/' + row.requestId) }

onMounted(async () => {
  reload()
  try {
    const d = await api.get('/api/dashboard')
    caCertExpiry.value = d.caCertificateExpiry
    caCertDays.value = d.caCertificateExpiry ? Math.floor((new Date(d.caCertificateExpiry) - new Date()) / 86400000) : null
  } catch {}
})
</script>
