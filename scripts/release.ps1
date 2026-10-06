# CA-Manager release script: commit -> tag -> push -> GitLab release (+ GitHub tag)
# Usage:
#   powershell -File scripts\release.ps1 -Version v1.5.0 -Notes "what changed"
#   (run from repo root or scripts folder; requires git and the gitlab token)
param(
    [Parameter(Mandatory = $true)][string]$Version,   # e.g. v1.5.0
    [string]$Notes = ''
)
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# gitlab token: env GITLAB_TOKEN, or read from git remote url
$remote = git remote get-url gitlab
$token = $env:GITLAB_TOKEN
if (-not $token -and $remote -match 'oauth2:([^@]+)@') { $token = $Matches[1] }
if (-not $token) { throw "GitLab token not found (set GITLAB_TOKEN or keep it in the gitlab remote url)" }

# 1. commit pending changes
$dirty = git status --porcelain
if ($dirty) {
    Write-Host "Committing pending changes..." -ForegroundColor Cyan
    git add -A
    git commit -m "CA-Manager $Version"
}

# 2. tag
git tag -f $Version
if ($LASTEXITCODE -ne 0) { throw "tag failed" }

# 3. push both remotes (gitlab full / github app source)
Write-Host "Pushing gitlab..." -ForegroundColor Cyan
git push gitlab main --tags
if ($LASTEXITCODE -ne 0) { throw "gitlab push failed" }
Write-Host "Pushing github..." -ForegroundColor Cyan
git push github main --tags
if ($LASTEXITCODE -ne 0) { Write-Warning "github push failed (deploy key added yet?)" }

# 4. GitLab release
if ($Notes -eq '') { $Notes = "CA-Manager $Version" }
$esc = $Notes.Replace('"', '\"')
$proj = [uri]::EscapeDataString('mylab/ca-manager')
$body = "{`"tag_name`":`"$Version`",`"name`":`"CA-Manager $Version`",`"description`":`"$esc`"}"
curl.exe -s -X POST "http://10.3.0.159:2080/api/v4/projects/$proj/releases" `
    -H "PRIVATE-TOKEN: $token" -H "Content-Type: application/json" -d $body | Out-Null
Write-Host "GitLab release $Version created." -ForegroundColor Green

# 5. GitHub release: tag is pushed; create the Release object when a token is configured:
#    gh release create $Version --title "CA-Manager $Version" --notes "..."   (requires gh auth)
Write-Host "GitHub tag $Version pushed. Create the Release in UI or with: gh release create $Version" -ForegroundColor Yellow
