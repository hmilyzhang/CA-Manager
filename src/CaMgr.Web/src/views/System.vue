<template>
  <div class="page">
    <div class="page-title">{{ $t('system.title') }}</div>

    <el-alert type="info" :closable="false" style="margin-bottom: 14px" :title="$t('system.hint')" />

    <el-card shadow="never">
      <template #header>
        {{ $t('system.current') }}
        <el-tag v-for="u in currentUrls" :key="u" size="small" style="margin-left: 6px"
          :type="u.startsWith('https') ? 'success' : 'warning'">{{ u }}</el-tag>
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

        <template v-if="form.mode !== 'http'">
          <el-form-item :label="$t('system.httpsPort')">
            <el-input-number v-model="form.httpsPort" :min="1" :max="65535" />
          </el-form-item>
          <el-form-item :label="$t('system.certificate')">
            <el-select v-model="form.thumbprint" filterable style="width: 100%; max-width: 640px"
              :placeholder="$t('system.pickCert')" :loading="loading">
              <el-option v-for="c in certs" :key="c.thumbprint" :value="c.thumbprint"
                :label="`${c.subject}  ·  ${c.notAfter.slice(0, 10)}`" />
            </el-select>
            <div v-if="certThumbInfo" class="mono" style="font-size:12px;color:#909399;margin-top:4px">
              {{ certThumbInfo }}
            </div>
          </el-form-item>
        </template>

        <el-form-item v-if="form.mode === 'both'">
          <el-alert type="warning" :closable="false" :title="$t('system.bothNote')" />
        </el-form-item>

        <el-form-item>
          <el-button type="primary" :loading="applying" :disabled="!canApply" @click="apply">
            {{ $t('system.apply') }}
          </el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card v-if="certs.length === 0 && form.mode !== 'http'" shadow="never" style="margin-top: 14px" class="danger-zone">
      <span style="color:#909399;font-size:13px">{{ $t('system.noCerts') }}</span>
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
const loading = ref(false)
const applying = ref(false)
const form = reactive({ mode: 'http', httpPort: 8443, httpsPort: 8443, thumbprint: '' })

const certThumbInfo = computed(() => {
  const c = certs.value.find(x => x.thumbprint === form.thumbprint)
  return c ? `${t('common.notAfter')}: ${new Date(c.notAfter).toLocaleString()}` : ''
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
    const c = await api.get('/api/system/certs')
    certs.value = c.certs || []
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
