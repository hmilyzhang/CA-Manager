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

    <el-card v-for="ep in examples" :key="ep.key" shadow="never" style="margin-bottom: 14px">
      <template #header>{{ ep.title }}</template>

      <h4 class="h4">{{ $t('apidocs.request') }}</h4>
      <el-tabs type="border-card" class="code-tabs">
        <el-tab-pane v-for="lang in langs" :key="lang.key" :label="lang.label">
          <pre class="mono code">{{ lang.snippets[ep.key].request }}</pre>
        </el-tab-pane>
      </el-tabs>

      <h4 class="h4">{{ $t('apidocs.response') }}</h4>
      <pre class="mono code">{{ ep.response }}</pre>

      <h4 class="h4">{{ $t('apidocs.fields') }}</h4>
      <el-table :data="ep.fields" size="small">
        <el-table-column prop="name" label="Field" width="160" />
        <el-table-column prop="desc" :label="$t('apidocs.desc')" />
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
import { ref } from 'vue'

const endpoints = [
  { method: 'POST', path: '/api/automation/certificate', desc: 'apidocs.epRequest' },
  { method: 'POST', path: '/api/automation/renew', desc: 'apidocs.epRenew' },
  { method: 'GET', path: '/api/automation/status/{requestId}', desc: 'apidocs.epStatus' },
]

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
    fields: [
      { name: 'commonName', desc: 'CN of the certificate (required)' },
      { name: 'san', desc: 'additional DNS / IP entries (optional, CN is always included)' },
      { name: 'keyAlgorithm', desc: 'RSA2048 | RSA4096 (default) | ECDSA_P256' },
      { name: 'template', desc: 'CA template name, e.g. WebServer' },
      { name: 'pfxPassword', desc: 'protects the returned PFX (min 8 chars, required)' },
      { name: 'issued', desc: 'true = PFX is present in pfxBase64' },
      { name: 'disposition', desc: '3=issued, 5=pending (manager approval), 2=denied by policy' },
    ],
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
    fields: [
      { name: 'serialNumber', desc: 'serial number of the certificate to renew (required)' },
      { name: 'pfxPassword', desc: 'password for the new PFX (min 8 chars, required)' },
      { name: 'renewedFrom', desc: 'serial of the old certificate' },
      { name: 'note', desc: 'the old certificate is kept - revoke it manually if needed' },
    ],
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
    fields: [
      { name: 'requestId', desc: 'id in the path' },
      { name: 'status', desc: 'issued | revoked | pending | denied | failed | expired' },
      { name: 'disposition', desc: 'raw AD CS disposition code' },
    ],
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
</style>
