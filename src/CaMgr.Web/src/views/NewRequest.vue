<template>
  <div class="page">
    <div class="page-title">{{ $t('newReq.title') }}</div>

    <el-row :gutter="14">
      <el-col :span="14">
        <el-card shadow="never">
          <template #header>{{ $t('newReq.cardTitle') }}</template>
          <el-tabs v-model="mode">
            <el-tab-pane :label="$t('newReq.tabSelf')" name="self">
              <el-form label-width="130px" style="margin-top: 8px">
                <el-form-item label="CN">
                  <el-input v-model="form.cn" placeholder="www.example.com" style="width: 320px" />
                </el-form-item>
                <el-form-item label="SAN">
                  <div style="width: 100%">
                    <div style="display:flex; gap:8px">
                      <el-input v-model="sanInput" :placeholder="$t('newReq.sanPh')" style="width: 320px"
                        @keyup.enter="addSan" />
                      <el-button @click="addSan">{{ $t('newReq.sanAdd') }}</el-button>
                    </div>
                    <div style="margin-top:8px; display:flex; gap:6px; flex-wrap:wrap">
                      <el-tag v-for="(s, i) in form.san" :key="i" closable @close="form.san.splice(i, 1)">{{ s }}</el-tag>
                      <span v-if="!form.san.length" style="color:#c0c4cc;font-size:12px">{{ $t('newReq.sanEmpty') }}</span>
                    </div>
                  </div>
                </el-form-item>
                <el-form-item :label="$t('newReq.keyAlg')">
                  <el-select v-model="form.keyAlgorithm" style="width: 220px">
                    <el-option value="RSA2048" label="RSA 2048" />
                    <el-option value="RSA4096" label="RSA 4096" />
                    <el-option value="ECDSA_P256" label="ECDSA P-256" />
                  </el-select>
                </el-form-item>
                <el-form-item :label="$t('newReq.template')">
                  <el-select v-if="templates.length" v-model="form.template" filterable :placeholder="$t('newReq.selectTemplate')" style="width: 320px">
                    <el-option v-for="tpl in templates" :key="tpl.name" :label="`${tpl.name}${tpl.displayName ? ' — ' + tpl.displayName : ''}`" :value="tpl.name" />
                  </el-select>
                  <el-input v-else :model-value="$t('newReq.noTemplates')" disabled style="width: 320px" />
                </el-form-item>
                <template v-if="isViewer">
                  <el-form-item :label="$t('newReq.pfxPwd')">
                    <el-input v-model="form.pfxPassword" type="password" show-password style="width: 320px" :placeholder="$t('login.pwdLen')" />
                  </el-form-item>
                  <el-form-item :label="$t('newReq.pfxPwd2')">
                    <el-input v-model="form.pfxPassword2" type="password" show-password style="width: 320px" />
                  </el-form-item>
                  <el-form-item>
                    <el-button type="primary" :loading="submitting" @click="submitSelf">{{ $t('newReq.submitForApproval') }}</el-button>
                  </el-form-item>
                </template>
                <el-form-item v-else>
                  <el-button type="primary" :loading="submitting" @click="submitSelf">{{ $t('newReq.genAndIssue') }}</el-button>
                </el-form-item>
              </el-form>
            </el-tab-pane>

            <el-tab-pane :label="$t('newReq.tabPaste')" name="paste">
              <el-form label-width="130px" style="margin-top: 8px">
                <el-form-item :label="$t('newReq.template')">
                  <el-select v-if="templates.length" v-model="template" filterable :placeholder="$t('newReq.selectTemplate')" style="width: 100%">
                    <el-option v-for="tpl in templates" :key="tpl.name" :label="`${tpl.name}${tpl.displayName ? ' — ' + tpl.displayName : ''}`" :value="tpl.name" />
                  </el-select>
                  <el-input v-else :model-value="$t('newReq.noTemplates')" disabled style="width: 100%" />
                </el-form-item>
                <el-form-item :label="$t('newReq.csr')">
                  <el-input v-model="csr" type="textarea" :rows="12" class="mono" style="font-size: 12px"
                    :placeholder="$t('newReq.csrPlaceholder')" />
                </el-form-item>
                <el-form-item>
                  <el-button type="primary" :loading="submitting" @click="submit">{{ $t('newReq.submitToCa') }}</el-button>
                  <el-button @click="$router.push('/requests')">{{ $t('newReq.viewQueue') }}</el-button>
                </el-form-item>
              </el-form>
            </el-tab-pane>
          </el-tabs>
        </el-card>
      </el-col>

      <el-col :span="10">
        <el-card v-if="selfResult" shadow="never" style="margin-bottom: 14px"
          :class="{ 'danger-zone': selfResult.disposition !== 3 && selfResult.disposition !== 4 }">
          <template #header>{{ $t('newReq.result') }}</template>
          <el-result :icon="selfIcon" :title="selfResult.message" :sub-title="selfResult.detail || ''">
            <template #extra>
              <div v-if="selfResult.requestId" style="color:#606266;margin-bottom:6px">
                {{ $t('newReq.reqId') }}：<b>{{ selfResult.requestId }}</b>
              </div>
              <template v-if="selfResult.pendingApproval">
                <div style="color:#606266">{{ $t('newReq.approvalMsg') }}</div>
                <el-button @click="$router.push('/approvals')">{{ $t('nav.approvals') }}</el-button>
              </template>
              <template v-else-if="selfResult.disposition === 3">
                <div style="margin: 8px 0; padding: 10px; background:#f0f9eb; border-radius: 6px">
                  <div style="font-weight:600; margin-bottom: 4px">{{ $t('newReq.pfxPwd') }}</div>
                  <div class="mono" style="font-size: 16px; letter-spacing: 1px">{{ selfResult.pfxPassword }}</div>
                  <div style="color:#e6a23c; font-size:12px; margin-top:4px">{{ $t('newReq.pwdOnce') }}</div>
                </div>
                <el-button type="primary" @click="dlPfx">{{ $t('newReq.dlPfx') }}</el-button>
                <el-button @click="$router.push('/certificates/' + selfResult.requestId)">{{ $t('newReq.viewDetail') }}</el-button>
              </template>
              <template v-else-if="selfResult.csrPem">
                <div style="color:#909399;font-size:13px;margin-bottom:8px">{{ $t('newReq.pendingHint') }}</div>
                <el-button @click="dlText(selfResult.csrPem, 'request.csr')">{{ $t('newReq.dlCsr') }}</el-button>
                <el-button type="warning" plain @click="dlText(selfResult.keyPem, 'private-key.pem')">{{ $t('newReq.dlKey') }}</el-button>
              </template>
            </template>
          </el-result>
        </el-card>

        <el-card v-if="pasteResult" shadow="never" style="margin-bottom: 14px"
          :class="{ 'danger-zone': pasteResult.disposition !== 3 && pasteResult.disposition !== 5 }">
          <template #header>{{ $t('newReq.result') }}</template>
          <el-result :icon="pasteIcon" :title="pasteResult.message" :sub-title="pasteResult.detail || ''">
            <template #extra>
              <div v-if="pasteResult.requestId" style="color:#606266">{{ $t('newReq.reqId') }}：<b>{{ pasteResult.requestId }}</b></div>
              <el-button v-if="pasteResult.disposition === 3" type="primary" @click="downloadCert">{{ $t('newReq.dlCert') }}</el-button>
              <el-button v-if="pasteResult.requestId" @click="$router.push('/certificates/' + pasteResult.requestId)">{{ $t('newReq.viewDetail') }}</el-button>
            </template>
          </el-result>
        </el-card>

        <el-card shadow="never">
          <template #header>{{ $t('newReq.howto') }}</template>
          <div style="font-size: 13px; color: #606266; line-height: 1.8">
            <p style="margin: 4px 0"><b>{{ $t('newReq.selfTitle') }}</b></p>
            <p style="margin: 4px 0">{{ $t('newReq.selfDesc') }}</p>
            <p style="margin: 4px 0"><b>{{ $t('newReq.pasteTitle') }}</b></p>
            <p style="margin: 4px 0">{{ $t('newReq.pasteDesc') }}</p>
            <p style="margin: 4px 0"><b>{{ $t('newReq.opensslTitle') }}</b></p>
            <pre class="mono" style="background:#f5f7fa;padding:8px;border-radius:6px;font-size:11px">openssl req -new -newkey rsa:2048 -nodes \
  -keyout server.key -out server.csr \
  -subj "/CN=www.example.com"</pre>
          </div>
        </el-card>
      </el-col>
    </el-row>
  </div>
</template>

<script setup>
import { computed, onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { api } from '../api.js'
import { store, roleAtLeast } from '../store.js'

const { t } = useI18n()
const templates = ref([])
const mode = ref('self')
const submitting = ref(false)

// self-service form
const isViewer = computed(() => !roleAtLeast(store.user?.role, 'Operator'))
const form = ref({ cn: '', san: [], keyAlgorithm: 'RSA2048', template: '', pfxPassword: '', pfxPassword2: '' })
const sanInput = ref('')
const selfResult = ref(null)

// paste mode
const template = ref('')
const csr = ref('')
const pasteResult = ref(null)

function addSan() {
  const v = sanInput.value.trim().toLowerCase()
  if (!v) return
  if (!form.value.san.includes(v)) form.value.san.push(v)
  sanInput.value = ''
}

const selfIcon = computed(() => {
  if (!selfResult.value) return 'info'
  return selfResult.value.disposition === 3 ? 'success' : selfResult.value.disposition === 5 ? 'warning' : 'error'
})
const pasteIcon = computed(() => {
  if (!pasteResult.value) return 'info'
  return pasteResult.value.disposition === 3 ? 'success' : pasteResult.value.disposition === 5 ? 'warning' : 'error'
})

async function submitSelf() {
  if (!form.value.cn.trim()) return ElMessage.warning(t('newReq.needCn'))
  if (templates.value.length && !form.value.template) return ElMessage.warning(t('newReq.needTemplate'))
  if (isViewer.value) {
    if (!form.value.pfxPassword || form.value.pfxPassword.length < 8) return ElMessage.warning(t('login.pwdLen'))
    if (form.value.pfxPassword !== form.value.pfxPassword2) return ElMessage.warning(t('login.pwdMismatch'))
  }
  submitting.value = true
  selfResult.value = null
  try {
    selfResult.value = await api.post('/api/requests/self-service', {
      commonName: form.value.cn.trim(),
      san: form.value.san,
      keyAlgorithm: form.value.keyAlgorithm,
      template: form.value.template,
      pfxPassword: isViewer.value ? form.value.pfxPassword : null,
    })
  } catch (e) {
    ElMessage.error(e.message)
  } finally {
    submitting.value = false
  }
}

async function submit() {
  if (!csr.value.trim()) return ElMessage.warning(t('newReq.needCsr'))
  if (templates.value.length && !template.value) return ElMessage.warning(t('newReq.needTemplate'))
  submitting.value = true
  pasteResult.value = null
  try {
    pasteResult.value = await api.post('/api/requests/submit', { csr: csr.value, template: template.value })
    if (pasteResult.value.disposition === 3) ElMessage.success(t('newReq.submittedOk'))
    else if (pasteResult.value.disposition === 5) ElMessage.info(t('newReq.submittedPending'))
  } catch (e) {
    ElMessage.error(e.message)
  } finally {
    submitting.value = false
  }
}

function blobDownload(data, filename, type) {
  const a = document.createElement('a')
  a.href = URL.createObjectURL(new Blob([data], { type }))
  a.download = filename
  a.click()
  URL.revokeObjectURL(a.href)
}

function dlPfx() {
  const bin = atob(selfResult.value.pfxBase64)
  const bytes = new Uint8Array(bin.length)
  for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i)
  blobDownload(bytes, selfResult.value.fileName, 'application/x-pkcs12')
}

function dlText(text, filename) {
  blobDownload(text, filename, 'application/x-pem-file')
}

function downloadCert() {
  api.download('/api/certificates/' + pasteResult.value.requestId + '/download?format=cer', `cert-${pasteResult.value.requestId}.cer`)
}

onMounted(async () => {
  try {
    templates.value = await api.get('/api/templates')
  } catch (e) {
    ElMessage.error(e.message)
  }
})
</script>
