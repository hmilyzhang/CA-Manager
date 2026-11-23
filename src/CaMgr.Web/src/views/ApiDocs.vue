<template>
  <div class="page">
    <div class="page-title">{{ $t('apidocs.title') }}</div>

    <el-alert type="info" :closable="false" style="margin-bottom: 14px" :title="$t('apidocs.intro')" />

    <el-card shadow="never" style="margin-bottom: 14px">
      <template #header>{{ $t('apidocs.auth') }}</template>
      <p style="margin: 4px 0 10px; font-size: 13px; color: #606266">{{ $t('apidocs.authText') }}</p>
      <pre class="mono code">Authorization: Bearer cam_xxxxxxxxxxxxxxxxxxxxxxxxxxxx</pre>
      <p style="margin: 10px 0 0; font-size: 13px; color: #606266">
        {{ $t('apidocs.authNote') }}
        <el-tag size="small" type="success">Operator</el-tag>
        {{ $t('apidocs.authRole') }}
      </p>
    </el-card>

    <el-card shadow="never" style="margin-bottom: 14px">
      <template #header>{{ $t('apidocs.endpoints') }}</template>
      <el-table :data="endpoints" size="small">
        <el-table-column label=" " width="100">
          <template #default="{ row }"><el-tag size="small" :type="row.method === 'GET' ? 'success' : 'primary'">{{ row.method }}</el-tag></template>
        </el-table-column>
        <el-table-column label="Endpoint" min-width="280">
          <template #default="{ row }"><span class="mono" style="font-size:12px">{{ row.path }}</span></template>
        </el-table-column>
        <el-table-column :label="$t('apidocs.purpose')" min-width="260">
          <template #default="{ row }">{{ $t(row.desc) }}</template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-card shadow="never" style="margin-bottom: 14px">
      <template #header>
        {{ $t('apidocs.fullRef') }}
        <span class="ref-count">{{ allEndpoints.length }} {{ $t('apidocs.endpointsCount') }}</span>
      </template>
      <p style="margin: 4px 0 10px; font-size: 13px; color: #606266">{{ $t('apidocs.fullRefText') }}</p>
      <el-table :data="allEndpoints" size="small" row-key="id" :row-class-name="rowClass" class="ref-table">
        <el-table-column type="expand" width="36">
          <template #default="{ row }">
            <div v-if="hasDetail(row)" class="detail">
              <div v-if="row.query" class="query">
                <span class="k">{{ $t('apidocs.query') }}</span>
                <span class="mono">{{ row.query }}</span>
              </div>
              <template v-if="row.body">
                <div class="k">{{ $t('apidocs.request') }}</div>
                <pre class="mono code small">{{ row.body }}</pre>
              </template>
              <template v-if="row.resp">
                <div class="k">{{ $t('apidocs.response') }}</div>
                <pre class="mono code small">{{ row.resp }}</pre>
              </template>
              <p v-if="row.note" class="note">{{ pick(row.note) }}</p>
            </div>
          </template>
        </el-table-column>
        <el-table-column label=" " width="86">
          <template #default="{ row }"><el-tag size="small" :type="row.method === 'GET' ? 'success' : 'primary'">{{ row.method }}</el-tag></template>
        </el-table-column>
        <el-table-column label="Endpoint" min-width="250">
          <template #default="{ row }"><span class="mono" style="font-size:12px">{{ row.path }}</span></template>
        </el-table-column>
        <el-table-column :label="$t('apidocs.colGroup')" width="110">
          <template #default="{ row }">{{ $t('apidocs.' + row.group) }}</template>
        </el-table-column>
        <el-table-column :label="$t('apidocs.colAuth')" width="190">
          <template #default="{ row }">
            <el-tag v-for="role in row.roles" :key="role" size="small" effect="plain" :type="roleType(role)" class="role-tag">{{ $t(ROLE_LABEL_KEYS[role]) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column :label="$t('apidocs.desc')" min-width="240">
          <template #default="{ row }">{{ pick(row.desc) }}</template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-card v-for="ep in examples" :key="ep.key" shadow="never" style="margin-bottom: 14px">
      <template #header>{{ ep.title }}</template>

      <h4 class="h4">{{ $t('apidocs.request') }}</h4>
      <el-tabs type="border-card" class="code-tabs">
        <el-tab-pane v-for="lang in langs" :key="lang.key" :label="lang.label">
          <pre class="mono code">{{ snippets[ep.key][lang.key] }}</pre>
        </el-tab-pane>
      </el-tabs>

      <h4 class="h4">{{ $t('apidocs.response') }}</h4>
      <pre class="mono code">{{ ep.response }}</pre>

      <h4 class="h4">{{ $t('apidocs.fields') }}</h4>
      <el-table :data="ep.fields" size="small">
        <el-table-column prop="name" label="Field" width="160" />
        <el-table-column :label="$t('apidocs.desc')">
          <template #default="{ row }">{{ pick(row) }}</template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-card shadow="never" style="margin-bottom: 14px">
      <template #header>{{ $t('apidocs.errors') }}</template>
      <el-table :data="errors" size="small">
        <el-table-column label="HTTP" width="90"><template #default="{ row }">{{ row.code }}</template></el-table-column>
        <el-table-column :label="$t('apidocs.when')"><template #default="{ row }">{{ $t(row.when) }}</template></el-table-column>
      </el-table>
    </el-card>

    <el-card shadow="never">
      <template #header>{{ $t('apidocs.psTitle') }}</template>
      <p style="margin: 4px 0 10px; font-size: 13px; color: #606266">{{ $t('apidocs.psText') }}</p>
      <pre class="mono code">scripts\api-example.ps1</pre>
    </el-card>
  </div>
</template>

<script setup>
import { curLang } from '../i18n.js'
import { AUTOMATION_FIELDS, FULL_REFERENCE, ROLE_LABEL_KEYS } from './apidocs-data.js'

const endpoints = [
  { method: 'POST', path: '/api/automation/certificate', desc: 'apidocs.epRequest' },
  { method: 'POST', path: '/api/automation/renew', desc: 'apidocs.epRenew' },
  { method: 'GET', path: '/api/automation/status/{requestId}', desc: 'apidocs.epStatus' },
]

// full REST reference - one row per endpoint, kept in apidocs-data.js next to this view
const allEndpoints = FULL_REFERENCE
// bilingual text is { en, zh }; pick() also passes plain strings through
const pick = (text) => (text && typeof text === 'object' ? text[curLang()] : text) || ''
const hasDetail = (row) => !!(row.query || row.body || row.resp || row.note)
const rowClass = ({ row }) => (hasDetail(row) ? '' : 'no-detail')
const roleType = (role) => ({ admin: 'danger', operator: 'warning', token: 'success' }[role] || 'info')

const langs = [
  { key: 'curl', label: 'curl' },
  { key: 'php', label: 'PHP' },
  { key: 'java', label: 'Java' },
  { key: 'powershell', label: 'PowerShell' },
]

const requestCert = {
  curl: `curl -k -X POST "https://ca-manager:8443/api/automation/certificate" \\
  -H "Authorization: Bearer cam_xxxxxxxxxxxxxxxxxxxxxxxx" \\
  -H "Content-Type: application/json" \\
  -d '{
    "commonName": "www.example.com",
    "san": ["www.example.com", "10.0.0.5"],
    "keyAlgorithm": "RSA4096",
    "template": "WebServer",
    "pfxPassword": "YourPfx#2026"
  }'`,
  php: `<?php
$ch = curl_init("https://ca-manager:8443/api/automation/certificate");
curl_setopt_array($ch, [
    CURLOPT_RETURNTRANSFER => true,
    CURLOPT_POST           => true,
    CURLOPT_SSL_VERIFYPEER => false, // internal CA
    CURLOPT_HTTPHEADER     => [
        "Authorization: Bearer cam_xxxxxxxxxxxxxxxxxxxxxxxx",
        "Content-Type: application/json",
    ],
    CURLOPT_POSTFIELDS     => json_encode([
        "commonName"   => "www.example.com",
        "san"          => ["www.example.com", "10.0.0.5"],
        "keyAlgorithm" => "RSA4096",
        "template"     => "WebServer",
        "pfxPassword"  => "YourPfx#2026",
    ]),
]);
$r = json_decode(curl_exec($ch), true);
curl_close($ch);

if ($r["issued"]) {
    file_put_contents("cert.pfx", base64_decode($r["pfxBase64"]));
    echo "Issued, requestId={$r["requestId"]}\\n";
} else {
    echo "Not issued: {$r["message"]}\\n";
}`,
  java: `// Java 11+ (java.net.http), no extra dependencies
var client = java.net.http.HttpClient.newBuilder()
        .sslContext(trustAllSslContext())   // trust the internal CA, see note below
        .build();

var json = """
    {"commonName":"www.example.com",
     "san":["www.example.com","10.0.0.5"],
     "keyAlgorithm":"RSA4096",
     "template":"WebServer",
     "pfxPassword":"YourPfx#2026"}""";

var request = java.net.http.HttpRequest.newBuilder()
        .uri(URI.create("https://ca-manager:8443/api/automation/certificate"))
        .header("Authorization", "Bearer cam_xxxxxxxxxxxxxxxxxxxxxxxx")
        .header("Content-Type", "application/json")
        .POST(java.net.http.HttpRequest.BodyPublishers.ofString(json))
        .build();

var response = client.send(request, java.net.http.HttpResponse.BodyHandlers.ofString());
var json2 = javax.json.Json.createReader(new java.io.StringReader(response.body())).readObject();
if (json2.getBoolean("issued")) {
    byte[] pfx = java.util.Base64.getDecoder().decode(json2.getString("pfxBase64"));
    java.nio.file.Files.write(java.nio.file.Path.of("cert.pfx"), pfx);
}`,
  powershell: `$body = @{
    commonName   = "www.example.com"
    san          = @("www.example.com", "10.0.0.5")
    keyAlgorithm = "RSA4096"
    template     = "WebServer"
    pfxPassword  = "YourPfx#2026"
} | ConvertTo-Json

$r = Invoke-RestMethod -Method Post -Uri "https://ca-manager:8443/api/automation/certificate" \`
    -Headers @{ Authorization = "Bearer cam_xxxxxxxxxxxxxxxxxxxxxxxx" } \`
    -ContentType "application/json" -Body $body -SkipCertificateCheck

if ($r.issued) {
    [IO.File]::WriteAllBytes("cert.pfx", [Convert]::FromBase64String($r.pfxBase64))
}`,
}

const renew = {
  curl: `curl -k -X POST "https://ca-manager:8443/api/automation/renew" \\
  -H "Authorization: Bearer cam_xxxxxxxxxxxxxxxxxxxxxxxx" \\
  -H "Content-Type: application/json" \\
  -d '{
    "serialNumber": "7a000000213e3db47c1eadf31de000000000021",
    "pfxPassword": "NewPfx#2026"
  }'`,
  php: `<?php
$ch = curl_init("https://ca-manager:8443/api/automation/renew");
curl_setopt_array($ch, [
    CURLOPT_RETURNTRANSFER => true,
    CURLOPT_POST           => true,
    CURLOPT_SSL_VERIFYPEER => false,
    CURLOPT_HTTPHEADER     => [
        "Authorization: Bearer cam_xxxxxxxxxxxxxxxxxxxxxxxx",
        "Content-Type: application/json",
    ],
    CURLOPT_POSTFIELDS     => json_encode([
        "serialNumber" => "7a000000213e3db47c1eadf31de000000000021",
        "pfxPassword"  => "NewPfx#2026",
    ]),
]);
$r = json_decode(curl_exec($ch), true);`,
  java: `var json = """
    {"serialNumber":"7a000000213e3db47c1eadf31de000000000021",
     "pfxPassword":"NewPfx#2026"}""";

var request = java.net.http.HttpRequest.newBuilder()
        .uri(URI.create("https://ca-manager:8443/api/automation/renew"))
        .header("Authorization", "Bearer cam_xxxxxxxxxxxxxxxxxxxxxxxx")
        .header("Content-Type", "application/json")
        .POST(java.net.http.HttpRequest.BodyPublishers.ofString(json))
        .build();`,
  powershell: `$r = Invoke-RestMethod -Method Post -Uri "https://ca-manager:8443/api/automation/renew" \`
    -Headers @{ Authorization = "Bearer cam_xxxxxxxxxxxxxxxxxxxxxxxx" } \`
    -ContentType "application/json" \`
    -Body (@{ serialNumber = "7a000000213e3db47c1eadf31de000000000021"; pfxPassword = "NewPfx#2026" } | ConvertTo-Json) \`
    -SkipCertificateCheck`,
}

const status = {
  curl: `curl -k "https://ca-manager:8443/api/automation/status/72" \\
  -H "Authorization: Bearer cam_xxxxxxxxxxxxxxxxxxxxxxxx"`,
  php: `<?php
$ch = curl_init("https://ca-manager:8443/api/automation/status/72");
curl_setopt_array($ch, [
    CURLOPT_RETURNTRANSFER => true,
    CURLOPT_SSL_VERIFYPEER => false,
    CURLOPT_HTTPHEADER     => ["Authorization: Bearer cam_xxxxxxxxxxxxxxxxxxxxxxxx"],
]);
$r = json_decode(curl_exec($ch), true);`,
  java: `var request = java.net.http.HttpRequest.newBuilder()
        .uri(URI.create("https://ca-manager:8443/api/automation/status/72"))
        .header("Authorization", "Bearer cam_xxxxxxxxxxxxxxxxxxxxxxxx")
        .GET().build();`,
  powershell: `$r = Invoke-RestMethod -Uri "https://ca-manager:8443/api/automation/status/72" \`
    -Headers @{ Authorization = "Bearer cam_xxxxxxxxxxxxxxxxxxxxxxxx" } \`
    -SkipCertificateCheck`,
}

// request snippets per example and language: snippets[example key][language key]
const snippets = { request: requestCert, renew, status }

const examples = [
  {
    key: 'request',
    title: 'POST /api/automation/certificate',
    response: `{
  "requestId": 71,
  "disposition": 3,
  "issued": true,
  "pfxBase64": "MIIExQIBAzCC...  (PKCS12 with the private key)",
  "fileName": "www.example.com.pfx",
  "message": "已颁发 / Issued"
}`,
    fields: AUTOMATION_FIELDS.request,
  },
  {
    key: 'renew',
    title: 'POST /api/automation/renew',
    response: `{
  "requestId": 72,
  "disposition": 3,
  "issued": true,
  "renewedFrom": "7A00000021E3DB47C1EADF31DE000000000021",
  "pfxBase64": "...",
  "fileName": "www.example.com.pfx"
}`,
    fields: AUTOMATION_FIELDS.renew,
  },
  {
    key: 'status',
    title: 'GET /api/automation/status/{requestId}',
    response: `{
  "requestId": 71,
  "disposition": 2,
  "status": "denied",
  "issued": false,
  "commonName": "api.test.lab",
  "template": "User"
}`,
    fields: AUTOMATION_FIELDS.status,
  },
]

const errors = [
  { code: '401', when: 'apidocs.e401' },
  { code: '403', when: 'apidocs.e403' },
  { code: '400', when: 'apidocs.e400' },
  { code: '404', when: 'apidocs.e404' },
]
</script>

<style scoped>
.code {
  background: #1e2430;
  color: #d8dee9;
  padding: 12px;
  border-radius: 8px;
  font-size: 12px;
  line-height: 1.5;
  overflow-x: auto;
  white-space: pre-wrap;
  word-break: break-all;
}
.h4 { margin: 14px 0 8px; font-size: 14px; color: #303133; }
.ref-count { margin-left: 8px; font-size: 12px; color: #909399; font-weight: 400; }
.ref-table :deep(.no-detail .el-table__expand-icon) { display: none; }
.ref-table :deep(.el-table__expanded-cell) { padding: 12px 20px 16px 56px; background: #fafbfc; }
.role-tag { margin: 2px 4px 2px 0; }
.detail .k { margin: 8px 0 4px; font-size: 12px; font-weight: 600; color: #606266; }
.detail .query { display: flex; gap: 8px; align-items: baseline; font-size: 12px; }
.detail .note { margin: 10px 0 0; font-size: 13px; line-height: 1.6; color: #e6a23c; }
.code.small { padding: 10px 12px; max-height: 320px; }
</style>
