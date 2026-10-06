# CA-Manager release script: build deploy zip -> commit -> tag -> push -> attach package to GitLab release
# Usage:
#   powershell -File scripts\release.ps1 -Version v1.5.0 -Notes "what changed"
param(
    [Parameter(Mandatory = $true)][string]$Version,   # e.g. v1.5.0
    [string]$Notes = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# gitlab token: env GITLAB_TOKEN, or from the gitlab remote url
$remote = git remote get-url gitlab
$token = $env:GITLAB_TOKEN
if (-not $token -and $remote -match 'oauth2:([^@]+)@') { $token = $Matches[1] }
if (-not $token) { throw "GitLab token not found (set GITLAB_TOKEN or keep it in the gitlab remote url)" }
$ver = $Version.TrimStart('v')

# ---- 0. bump the sidebar version ----
$layout = Join-Path $root 'src\CaMgr.Web\src\layout\MainLayout.vue'
(Get-Content $layout -Raw) -replace 'CA-Manager v[\d.]+', "CA-Manager $Version" | Set-Content $layout -Encoding UTF8

# ---- 1. build the deploy package ----
Write-Host "[1/6] building deploy package..." -ForegroundColor Cyan
& (Join-Path $PSScriptRoot 'publish.ps1')
$zip = Join-Path $root "CA-Manager-Deploy-$Version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $root 'dist\CA-Mgr') -DestinationPath $zip -Force
Write-Host ("  package: {0} ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))

# ---- 2. commit pending changes ----
Write-Host "[2/6] committing..." -ForegroundColor Cyan
if (git status --porcelain) {
    git add -A
    git commit -m "CA-Manager $Version"
}

# ---- 3. tag ----
Write-Host "[3/6] tagging $Version..." -ForegroundColor Cyan
git tag -f $Version

# ---- 4. push both remotes ----
Write-Host "[4/6] pushing..." -ForegroundColor Cyan
git push gitlab main --tags
if ($LASTEXITCODE -ne 0) { throw "gitlab push failed" }
git push github main --tags
if ($LASTEXITCODE -ne 0) { Write-Warning "github push failed" }

# ---- 5. GitLab release + package asset ----
Write-Host "[5/6] creating GitLab release..." -ForegroundColor Cyan
if ($Notes -eq '') { $Notes = "CA-Manager $Version" }
$esc = $Notes.Replace('"', '\"').Replace("`n", '\n')
$proj = [uri]::EscapeDataString('mylab/ca-manager')
$body = "{`"tag_name`":`"$Version`",`"name`":`"CA-Manager $Version`",`"description`":`"$esc`"}"
curl.exe -s -X POST "http://10.3.0.159:2080/api/v4/projects/$proj/releases" `
    -H "PRIVATE-TOKEN: $token" -H "Content-Type: application/json" -d $body | Out-Null

$zipName = Split-Path $zip -Leaf
curl.exe -s -X PUT "http://10.3.0.159:2080/api/v4/projects/$proj/packages/generic/ca-manager-deploy/$ver/$zipName" `
    -H "PRIVATE-TOKEN: $token" -H "Content-Type: application/octet-stream" --data-binary "@$zip" | Out-Null
$pkgUrl = "http://10.3.0.159:2080/api/v4/projects/$proj/packages/generic/ca-manager-deploy/$ver/$zipName"
$linkBody = "{`"name`":`"$zipName (Windows x64, self-contained)`",`"url`":`"$pkgUrl`",`"link_type`":`"package`"}"
curl.exe -s -X POST "http://10.3.0.159:2080/api/v4/projects/$proj/releases/$Version/assets/links" `
    -H "PRIVATE-TOKEN: $token" -H "Content-Type: application/json" -d $linkBody | Out-Null
Write-Host "  GitLab release $Version with deploy package created." -ForegroundColor Green

# ---- 6. GitHub release (tag already pushed) ----
Write-Host "[6/6] GitHub..." -ForegroundColor Cyan
$ghToken = $env:GITHUB_TOKEN
if (-not $ghToken) { $ghToken = git config --get ca-manager.githubToken }
$zipName = Split-Path $zip -Leaf
if ($ghToken) {
    # create the Release object
    $ghBody = "{`"tag_name`":`"$Version`",`"name`":`"CA-Manager $Version`",`"body`":`"$esc`"}"
    $resp = curl.exe -s -X POST "https://api.github.com/repos/hmilyzhang/CA-Manager/releases" `
        -H "Authorization: Bearer $ghToken" -H "Accept: application/vnd.github+json" -H "Content-Type: application/json" -d $ghBody
    $relId = ($resp | ConvertFrom-Json).id
    if (-not $relId) { Write-Warning "GitHub release creation failed: $resp" }
    else {
        # upload the deploy zip as a release asset
        curl.exe -s -X POST "https://uploads.github.com/repos/hmilyzhang/CA-Manager/releases/$relId/assets?name=$zipName" `
            -H "Authorization: Bearer $ghToken" -H "Content-Type: application/zip" --data-binary "@$zip" | Out-Null
        Write-Host "  GitHub release $Version created with deploy package attached." -ForegroundColor Green
    }
} elseif (Get-Command gh -ErrorAction SilentlyContinue) {
    gh release create $Version $zip --title "CA-Manager $Version" --notes $Notes
} else {
    Write-Host "  Tag $Version pushed. Configure a GitHub PAT (git config ca-manager.githubToken <token>) to automate releases." -ForegroundColor Yellow
}
Write-Host "Done." -ForegroundColor Green
