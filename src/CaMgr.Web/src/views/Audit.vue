<template>
  <div class="page">
    <div class="page-title">{{ $t('audit.title') }}</div>

    <div class="filter-bar">
      <el-select v-model="action" clearable :placeholder="$t('audit.actionType')" style="width: 180px" @change="reload(true)">
        <el-option v-for="a in actions" :key="a" :value="a" :label="actionName(a)" />
      </el-select>
      <el-input v-model="user" :placeholder="$t('audit.userContains')" clearable style="width: 170px" @keyup.enter="reload(true)" @clear="reload(true)" />
      <el-date-picker v-model="range" type="datetimerange" value-format="YYYY-MM-DDTHH:mm:ss"
        :start-placeholder="$t('audit.start')" :end-placeholder="$t('audit.end')" style="width: 340px" @change="reload(true)" />
      <el-button @click="reload(true)">{{ $t('common.search') }}</el-button>
      <el-button @click="exportCsv"><el-icon><Download /></el-icon>&nbsp;{{ $t('common.export') }}</el-button>
    </div>

    <el-table :data="items" v-loading="loading" stripe>
      <el-table-column :label="$t('common.time')" width="170">
        <template #default="{ row }">{{ fmt(row.at) }}</template>
      </el-table-column>
      <el-table-column prop="username" :label="$t('common.user')" width="120" />
      <el-table-column prop="ip" :label="$t('common.ip')" width="130" />
      <el-table-column :label="$t('audit.actionType')" width="120">
        <template #default="{ row }">
          <el-tag :type="row.success ? '' : 'danger'" size="small">{{ actionName(row.action) }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column prop="objectType" :label="$t('common.objectType')" width="100" />
      <el-table-column prop="objectId" :label="$t('common.object')" min-width="150" show-overflow-tooltip />
      <el-table-column prop="detail" :label="$t('common.detailLabel')" min-width="220" show-overflow-tooltip />
      <el-table-column :label="$t('common.result')" width="90">
        <template #default="{ row }">{{ row.success ? $t('common.success') : $t('common.failed') }}</template>
      </el-table-column>
    </el-table>

    <el-pagination style="margin-top: 12px" layout="prev, pager, next, total" :total="total"
      :page-size="pageSize" :current-page="page" @current-change="onPage" />
  </div>
</template>

<script setup>
import { onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { api } from '../api.js'
import { fmtDate } from '../i18n.js'

const { t } = useI18n()
const items = ref([])
const actions = ref([])
const action = ref('')
const user = ref('')
const range = ref(null)
const loading = ref(false)
const page = ref(1)
const pageSize = 50
const total = ref(0)

const fmt = (d) => fmtDate(d)
const actionName = (a) => {
  const key = `actionNames.${a}`
  return t(key) === key ? a : t(key)
}

async function reload(reset) {
  if (reset) page.value = 1
  loading.value = true
  try {
    const params = new URLSearchParams({ page: String(page.value), pageSize: String(pageSize) })
    if (action.value) params.set('action', action.value)
    if (user.value) params.set('user', user.value)
    if (range.value?.length === 2) { params.set('from', range.value[0]); params.set('to', range.value[1]) }
    const r = await api.get('/api/audit?' + params.toString())
    items.value = r.items
    total.value = r.total
  } catch (e) {
    ElMessage.error(e.message)
  } finally {
    loading.value = false
  }
}

function onPage(p) { page.value = p; reload(false) }

function exportCsv() {
  const params = new URLSearchParams()
  if (action.value) params.set('action', action.value)
  if (user.value) params.set('user', user.value)
  if (range.value?.length === 2) { params.set('from', range.value[0]); params.set('to', range.value[1]) }
  api.download('/api/audit/export?' + params.toString(), 'audit-log.csv')
}

onMounted(async () => {
  reload(true)
  try { actions.value = await api.get('/api/audit/actions') } catch {}
})
</script>
