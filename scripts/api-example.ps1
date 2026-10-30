# CA-Manager API example: request a certificate and import it (Windows).
# Requires PowerShell 5.1+ (built into Windows).
#
# Usage:
#   .\api-example.ps1 -BaseUrl "https://ca-manager.corp.local:8443" -Token "cam_..." `
#       -CommonName "www.example.com" -San "www.example.com","10.0.0.5" `
#       -Template WebServer -PfxPassword "YourPfx#2026"
param(
    [Parameter(Mandatory = $true)][string]$BaseUrl,
    [Parameter(Mandatory = $true)][string]$Token,
    [Parameter(Mandatory = $true)][string]$CommonName,
    [string[]]$San = @(),
    [Parameter(Mandatory = $true)][string]$PfxPassword,
    [string]$Template = 'WebServer',
    [ValidateSet('RSA2048', 'RSA4096', 'ECDSA_P256')][string]$KeyAlgorithm = 'RSA4096',
    [switch]$ImportToLocalMachine
)

$ErrorActionPreference = 'Stop'
# ignore the internal-CA certificate warning on the API call
if (-not ('TrustAllCertsPolicy' -as [type])) {
    Add-Type @"
using System.Net;
using System.Security.Cryptography.X509Certificates;
public class TrustAllCertsPolicy : ICertificatePolicy {
    public bool CheckValidationResult(ServicePoint sp, X509Certificate cert, WebRequest req, int problem) { return true; }
}
"@
}
[System.Net.ServicePointManager]::CertificatePolicy = New-Object TrustAllCertsPolicy
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$headers = @{ Authorization = "Bearer $Token" }

# --- 1. request the certificate (self-service: key generated server-side, PFX returned) ---
$body = @{
    commonName   = $CommonName
    san          = $San
    keyAlgorithm = $KeyAlgorithm
    template     = $Template
    pfxPassword  = $PfxPassword
} | ConvertTo-Json

Write-Host "Requesting certificate for $CommonName..."
$r = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/automation/certificate" `
    -Headers $headers -ContentType 'application/json' -Body $body

if (-not $r.issued) {
    Write-Warning "Not issued (disposition $($r.disposition)): $($r.message)"
    Write-Host "Check the Approvals page or the CA queue, then re-run after approval."
    return
}
Write-Host "Issued: requestId $($r.requestId)"

# --- 2. save and import the PFX ---
$pfxFile = Join-Path $env:TEMP "$($r.fileName).pfx"
[IO.File]::WriteAllBytes($pfxFile, [Convert]::FromBase64String($r.pfxBase64))

if ($ImportToLocalMachine) {
    $imported = Import-PfxCertificate -FilePath $pfxFile -CertStoreLocation Cert:\LocalMachine\My -Password (ConvertTo-SecureString $PfxPassword -AsPlainText -Force)
    Write-Host "Imported to LocalMachine\My: $($imported.Thumbprint)"
} else {
    Write-Host "PFX saved to $pfxFile - import with:"
    Write-Host "  Import-PfxCertificate -FilePath $pfxFile -CertStoreLocation Cert:\LocalMachine\My -Password (ConvertTo-SecureString '$PfxPassword' -AsPlainText -Force)"
}

Remove-Item $pfxFile -Force -ErrorAction SilentlyContinue
Write-Host "Done. Deploy the PFX to your web server / load balancer and remove the local copy."
