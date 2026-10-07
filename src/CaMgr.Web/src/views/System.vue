<template>
  <div class="page">
    <div class="page-title">{{ $t('system.title') }}</div>

    <el-alert type="info" :closable="false" style="margin-bottom: 14px" :title="$t('system.hint')" />

    <el-card shadow="never">
      <template #header>
        {{ $t('system.current') }}
        <el-tag v-for="u in currentUrls" :key="u" size="small" style="margin-left: 6px"
          :type="u.startsWith('https') ? 'success' : 'warning'">{{ u }}</el-tag>
        <el-tag v-if="!currentUrls.length" type="warning" size="small">HTTP</el-tag>
      </template>
      <span style="color:#909399;font-size:13px">{{ $t('system.currentHint') }}</span>
    </el-card>

    <el-card shadow="never" style="margin-top: 14px">
      <template #header>{{ $t('system.mode') }}</template>
      <el-form label-width="130px">
        <el-form-item :label="$t('system.mode')">
          <el-radio-group v-model="form.mode">
            <el-radio value="http">{{ $t('system.mHttp') }}</el-radio>
            <el-radio value="https">{{ $t('system.mHttps') }}</el-radio>
            <el-radio value="both">{{ $t('system.mBoth') }}</el-radio>
          </el-radio-group>
        </el-form-item>

        <el-form-item v-if="form.mode !== 'https'" :label="$t('system.httpPort')">
          <el-input-number v-model="form.httpPort" :min="1" :max="65535" />
        </el-form-item>

        <el-form-item v-if="form.mode !== 'http'" :label="$t('system.httpsPort')">
          <el-input-number v-model="form.httpsPort" :min="1" :max="65535" />
        </el-form-item>

        <el-form-item v-if="form.mode === 'both'">
          <el-alert type="warning" :closable="false" :title="$t('system.bothNote')" />
        </el-form-item>

        <el-form-item v-if="form.mode !== 'http' && !form.thumbprint">
          <el-alert type="warning" :closable="false" :title="$t('system.pickFromList')" />
        </el-form-item>

        <el-form-item>
          <el-button type="primary" :loading="applying" :disabled="!canApply" @click="apply">
            {{ $t('system.apply') }}
          </el-button>
          <span v-if="form.mode !== 'http' && form.thumbprint" style="margin-left: 10px; color:#909399; font-size:13px">
            {{ selectedSummary }}
          </span>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never" style="margin-top: 14px">
      <template #header>{{ $t('system.certs') }}</template>
      <el-table :data="certs" v-loading="loading" stripe @row-click="(r) => (form.thumbprint = r.thumbprint)" row-class-name="clickable">
        <el-table-column width="45">
          <template #default="{ row }">
            <el-radio :model-value="form.thumbprint" :value="row.thumbprint" @change="form.thumbprint = row.thumbprint">&nbsp;</el-radio>
          </template>
        </el-table-column>
        <el-table-column prop="subject" :label="$t('common.subject')" min-width="220" show-overflow-tooltip />
        <el-table-column :label="$t('common.notAfter')" width="105">
          <template #default="{ row }">{{ new Date(row.notAfter).toLocaleDateString() }}</template>
        </el-table-column>
        <el-table-column label="SAN" min-width="150" show-overflow-tooltip>
          <template #default="{ row }"><span class="mono" style="font-size:12px">{{ (row.san || []).join(', ') || '—' }}</span></template>
        </el-table-column>
        <el-table-column prop="thumbprint" :label="$t('certs.detail.sha1')" min-width="180">
          <template #default="{ row }"><span class="mono" style="font-size:12px">{{ row.thumbprint }}</span></template>
        </el-table-column>
      </el-table>
      <div v-if="!loading && certs.length === 0" style="color:#909399;padding:16px;text-align:center;font-size:13px">
        {{ $t('system.noCerts') }}
      </div>
      <div v-if="form.mode === 'http' && certs.length" style="margin-top: 10px; color:#909399; font-size:12px">
        {{ $t('system.pickInHttpsMode') }}
      </div>
    </el-card>

    <el-card v-if="excluded.length" shadow="never" style="margin-top: 14px">
      <template #header>{{ $t('system.excluded') }}</template>
      <el-table :data="excluded" size="small">
        <el-table-column prop="subject" :label="$t('common.subject')" min-width="200" show-overflow-tooltip />
        <el-table-column :label="$t('common.notAfter')" width="100">
          <template #default="{ row }">{{ new Date(row.notAfter).toLocaleDateString() }}</template>
        </el-table-column>
        <el-table-column :label="$t('system.excludeReason')" min-width="240">
          <template #default="{ row }">
            <el-tag type="info" size="small">{{ $t('system.r_' + row.reason) }}</el-tag>
          </template>
        </el-table-column>
      </el-table>
    </el-card>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { api } from '../api.js'

const { t } = useI18n()
const certs = ref([])
const excluded = ref([])
const currentUrls = ref([])
const loading = ref(false)
const applying = ref(false)
const form = reactive({ mode: 'http', httpPort: 8443, httpsPort: 8443, thumbprint: '' })

const selectedSummary = computed(() => {
  const c = certs.value.find(x => x.thumbprint === form.thumbprint)
  return c ? `${c.subject} · ${new Date(c.notAfter).toLocaleDateString()}` : ''
})
const canApply = computed(() =>
  form.mode === 'http' ? true : form.thumbprint !== '')

async function load() {
  loading.value = true
  try {
    const d = await api.get('/api/system/endpoints')
    form.mode = d.mode || 'http'
    form.httpPort = d.httpPort || 8443
    form.httpsPort = d.httpsPort || 8443
    currentUrls.value = d.currentUrls || []
    const c = await api.get('/api/system/certs')
    certs.value = c.certs || []
    excluded.value = c.excluded || []
  } catch (e) {
    ElMessage.error(e.message)
  } finally {
    loading.value = false
  }
}

async function apply() {
  const modeText = { http: t('system.mHttp'), https: t('system.mHttps'), both: t('system.mBoth') }[form.mode]
  try {
    await ElMessageBox.confirm(
      t('system.applyConfirm', { mode: modeText }), t('system.apply'),
      { type: 'warning' })
  } catch { return }
  applying.value = true
  try {
    const r = await api.post('/api/system/apply', {
      mode: form.mode,
      httpPort: form.mode === 'https' ? null : form.httpPort,
      httpsPort: form.mode === 'http' ? null : form.httpsPort,
      thumbprint: form.mode === 'http' ? null : form.thumbprint,
    })
    ElMessage.success(r.message)
  } catch (e) {
    ElMessage.error(e.message)
  } finally {
    applying.value = false
  }
}

onMounted(load)
</script>
