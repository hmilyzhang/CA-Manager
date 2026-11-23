# CA-Manager release script: bump versions -> build the deploy package from source ->
# verify it -> commit -> tag -> push -> create the GitLab & GitHub releases with the zip attached.
#
# Usage:
#   powershell -File scripts\release.ps1 -Version v1.8.2 -Notes "what changed"
#   powershell -File scripts\release.ps1 -Version v1.8.2 -PackageOnly   # build + verify the zip only
#
# Notes:
# - The package is assembled from the source tree into dist\CA-Mgr (never copied from the
#   running publish\ folder), so no admin rights are needed and nothing machine-specific
#   (data\, appsettings.Production.json, certificates) can leak into the zip.
# - The local installation is NOT touched; update it separately (update.ps1 or the deploy zip).
# - Only the version-related files are committed. The working tree must be clean of other
#   tracked changes before releasing.
param(
    [Parameter(Mandatory = $true)][string]$Version,   # e.g. v1.8.2
    [string]$Notes = '',
    [switch]$PackageOnly                              # build + verify the zip, skip git / releases
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$ver = $Version.TrimStart('v')

$gitlabBase = 'http://10.3.0.159:2080/api/v4'
$gitlabProj = 'mylab%2Fca-manager'
$githubRepo = 'hmilyzhang/CA-Manager'
$pkgName = "CA-Manager-Deploy-$Version.zip"
$zipPath = Join-Path $root $pkgName
$dist = Join-Path $root 'dist\CA-Mgr'
$pkgPath = Join-Path $root 'src\CaMgr.Web\package.json'

function Step($n, $msg) { Write-Host "[$n] $msg" -ForegroundColor Cyan }
function Die($msg) { throw $msg }

# GitLab API token: env GITLAB_TOKEN, or from the token embedded in the gitlab remote url
$token = $env:GITLAB_TOKEN
if (-not $token) {
    $ErrorActionPreference = 'Continue'   # 2>$null under 'Stop' turns stderr into a terminating error
    $remoteUrl = git remote get-url gitlab 2>$null
    $ErrorActionPreference = 'Stop'
    if ($remoteUrl -match 'oauth2:([^@]+)@') { $token = $Matches[1] }
}

# ---------- 0. preflight ----------
Step 0 'preflight'
if ($Version -notmatch '^v\d+\.\d+\.\d+$') { Die "use a version like v1.8.2 (got '$Version')" }
if (Test-Path $zipPath) { Die "$pkgName already exists - delete it or pick another version" }
if (-not $PackageOnly -and (git tag -l $Version)) { Die "tag $Version already exists locally" }
if (-not $token -and -not $PackageOnly) { Die 'GitLab token not found (set GITLAB_TOKEN or keep it in the gitlab remote url)' }
$dirty = @(git status --porcelain | Where-Object { $_ -notmatch '^\?\?' })
if ($dirty.Count -gt 0 -and -not $PackageOnly) {
    Die ("refusing to release with uncommitted tracked changes - commit or stash first:`n    " + ($dirty -join "`n    "))
}
$untracked = @(git status --porcelain | Where-Object { $_ -match '^\?\?' })
if ($untracked.Count -gt 0) {
    Write-Warning ("untracked files are left alone and NOT committed:`n    " + ($untracked -join "`n    "))
}
$dotnet = Join-Path $root '.dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

# ---------- 1. bump the version files ----------
# A ConvertTo-Json round-trip would reformat package.json, and Set-Content -Encoding UTF8
# would prepend a BOM that breaks the JSON.parse in vite.config.js - so edit the text
# in place and write UTF-8 without BOM.
Step 1 'bumping version files...'
$pkgText = [IO.File]::ReadAllText($pkgPath)
$old = ''
if ($pkgText -match '"version"\s*:\s*"([^"]+)"') { $old = $Matches[1] }
if (-not $old) { Die 'cannot find the version field in package.json' }
if ($old -eq $ver) {
    Write-Host "  package.json is already at $ver"
} else {
    $newText = $pkgText -replace ('"version"\s*:\s*"' + [regex]::Escape($old) + '"'), ('"version": "' + $ver + '"')
    [IO.File]::WriteAllText($pkgPath, $newText)
    Write-Host "  package.json: $old -> $ver"
}
# "CA-Manager v<old>" appears only in the document-control rows and the notices header,
# never in revision-history entries - a literal replace is safe
foreach ($f in (Join-Path $root 'THIRD-PARTY-NOTICES.md'), (Join-Path $root 'docs\bau-sop.md'), (Join-Path $root 'docs\bau-sop.zh-CN.md')) {
    if (-not (Test-Path $f)) { continue }
    $t = [IO.File]::ReadAllText($f)
    if ($t.Contains("CA-Manager v$old")) {
        [IO.File]::WriteAllText($f, $t.Replace("CA-Manager v$old", "CA-Manager v$ver"))
        Write-Host "  $(Split-Path $f -Leaf): CA-Manager v$old -> CA-Manager v$ver"
    }
}

# ---------- 2. frontend ----------
# PS 5.1 gotcha: with $ErrorActionPreference = 'Stop', merging native stderr via 2>&1
# turns harmless stderr lines (npm notices, build warnings) into terminating errors -
# so relax it around the native builds and judge them by their exit codes
Step 2 'building frontend...'
Push-Location (Join-Path $root 'src\CaMgr.Web')
$ErrorActionPreference = 'Continue'
& npm run build 2>&1 | ForEach-Object { Write-Host "  $_" }
$feExit = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
Pop-Location
if ($feExit -ne 0) { Die "frontend build failed (exit $feExit)" }
$wwwroot = Join-Path $root 'src\CaMgr.Api\wwwroot'
$layout = Get-ChildItem (Join-Path $wwwroot 'assets\MainLayout-*.js') | Select-Object -First 1
if (-not $layout -or -not (Select-String -Path $layout.FullName -Pattern ('"' + $ver + '"') -Quiet)) {
    Die "the built sidebar chunk does not carry version $ver - check the package.json bump"
}

# ---------- 3. backend into the staging folder ----------
Step 3 'publishing backend into dist\CA-Mgr\publish...'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
$ErrorActionPreference = 'Continue'
& $dotnet publish (Join-Path $root 'src\CaMgr.Api\CaMgr.Api.csproj') `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:UseSharedCompilation=false -nodeReuse:false `
    -o (Join-Path $dist 'publish') 2>&1 | Out-Null
$beExit = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
if ($beExit -ne 0) { Die "dotnet publish failed (exit $beExit)" }

# ---------- 4. stage docs + deployment scripts ----------
Step 4 'staging docs and deployment scripts...'
Copy-Item (Join-Path $root 'README.md'), (Join-Path $root 'README.zh-CN.md'), (Join-Path $root 'THIRD-PARTY-NOTICES.md') $dist
New-Item -ItemType Directory -Force (Join-Path $dist 'docs') | Out-Null
Copy-Item (Join-Path $root 'docs\*.md') (Join-Path $dist 'docs')
New-Item -ItemType Directory -Force (Join-Path $dist 'scripts') | Out-Null
foreach ($s in 'install-service.ps1', 'uninstall-service.ps1', 'api-example.ps1', 'migrate-service-name.ps1', 'set-display-name.ps1') {
    Copy-Item (Join-Path $root "scripts\$s") (Join-Path $dist 'scripts')
}

# ---------- 5. create the zip ----------
# .NET ZipArchive with forward-slash entry names - Compress-Archive (PS 5.1) writes
# backslash separators that break extraction with non-Windows tools
Step 5 "creating $pkgName..."
Add-Type -AssemblyName System.IO.Compression | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem | Out-Null
$zip = [System.IO.Compression.ZipFile]::Open($zipPath, 'Create')
try {
    # entries start at CA-Mgr/ - i.e. relative to the dist\ folder, not to dist\CA-Mgr
    $stageRoot = (Get-Item $dist).Parent.FullName
    Get-ChildItem $dist -Recurse -File | Sort-Object FullName | ForEach-Object {
        $rel = $_.FullName.Substring($stageRoot.Length + 1).Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $rel, 'Optimal') | Out-Null
    }
} finally { $zip.Dispose() }
Write-Host ("  {0}  ({1:N1} MB, {2} files staged)" -f $pkgName, ((Get-Item $zipPath).Length / 1MB), (Get-ChildItem $dist -Recurse -File).Count)

# ---------- 6. verify the package ----------
Step 6 'verifying the package...'
$problems = @()
$archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $names = @($archive.Entries | ForEach-Object FullName)
    foreach ($f in 'publish/data', 'appsettings.Production.json', 'initial-admin-password.txt', 'node_modules', '__pycache__') {
        if (@($names | Where-Object { $_ -like "*$f*" }).Count -gt 0) { $problems += "contains $f" }
    }
    foreach ($r in 'CA-Mgr/README.md', 'CA-Mgr/README.zh-CN.md', 'CA-Mgr/THIRD-PARTY-NOTICES.md',
             'CA-Mgr/docs/deploy.md', 'CA-Mgr/docs/deploy.zh-CN.md', 'CA-Mgr/docs/operations.md', 'CA-Mgr/docs/operations.zh-CN.md',
             'CA-Mgr/docs/bau-sop.md', 'CA-Mgr/docs/bau-sop.zh-CN.md',
             'CA-Mgr/scripts/install-service.ps1', 'CA-Mgr/scripts/uninstall-service.ps1', 'CA-Mgr/scripts/api-example.ps1',
             'CA-Mgr/publish/CaMgr.Api.exe', 'CA-Mgr/publish/CaMgr.Api.dll', 'CA-Mgr/publish/appsettings.json', 'CA-Mgr/publish/wwwroot/index.html') {
        if ($names -notcontains $r) { $problems += "missing $r" }
    }
    $streamsToClose = @()
    $readEntry = {
        param($entryName)
        $s = $archive.GetEntry($entryName).Open()
        $streamsToClose += $s
        $sr = New-Object IO.StreamReader($s)
        $text = $sr.ReadToEnd()
        $sr.Close()
        return $text
    }
    $baseText = & $readEntry 'CA-Mgr/publish/appsettings.json'
    if ($baseText -match 'Listeners' -or $baseText -match 'Thumbprint') {
        $problems += 'base appsettings.json carries listener configuration'
    }
    $entryBundles = @($names | Where-Object { $_ -match '^CA-Mgr/publish/wwwroot/assets/index-[^/]+\.js$' })
    if ($entryBundles.Count -ne 1) { $problems += "expected exactly one entry bundle, found $($entryBundles.Count)" }
    $layoutEntries = @($names | Where-Object { $_ -match '^CA-Mgr/publish/wwwroot/assets/MainLayout-[^/]+\.js$' })
    if ($layoutEntries.Count -ne 1) {
        $problems += "expected exactly one layout chunk, found $($layoutEntries.Count)"
    } elseif ((& $readEntry $layoutEntries[0]) -notmatch ('"' + [regex]::Escape($ver) + '"')) {
        $problems += "layout chunk does not carry version $ver"
    }
    $streamsToClose | ForEach-Object { $_.Close() }
} finally { $archive.Dispose() }
if ($problems.Count -gt 0) { Die ("package verification failed:`n    " + ($problems -join "`n    ")) }
Write-Host '  package verification: OK' -ForegroundColor Green

if ($PackageOnly) {
    Write-Host "`nPackageOnly: $zipPath built and verified. Version files may now be modified - review and commit them yourself." -ForegroundColor Yellow
    return
}

# ---------- 7. commit only the version-related files ----------
Step 7 'committing the version bump...'
git add src/CaMgr.Web/package.json THIRD-PARTY-NOTICES.md docs/bau-sop.md docs/bau-sop.zh-CN.md
if ($LASTEXITCODE -ne 0) { Die 'git add failed' }
git diff --cached --quiet
if ($LASTEXITCODE -ne 0) {
    git commit -m "CA-Manager $Version - release" | Out-Host
    if ($LASTEXITCODE -ne 0) { Die 'git commit failed' }
} else {
    Write-Host '  nothing staged (version already committed) - skipping the commit'
}

# ---------- 8. tag ----------
Step 8 "tagging $Version..."
git tag -a $Version -m "CA-Manager $Version"
if ($LASTEXITCODE -ne 0) { Die 'git tag failed' }

# ---------- 9. push ----------
Step 9 'pushing main + tag...'
git push gitlab main $Version
if ($LASTEXITCODE -ne 0) { Die 'gitlab push failed' }
git push github main $Version
if ($LASTEXITCODE -ne 0) { Write-Warning 'github push failed - continuing with the GitLab release' }

# ---------- 10. GitLab release ----------
Step 10 'GitLab release...'
$api = "$gitlabBase/projects/$gitlabProj"
$tmp = Join-Path $env:TEMP ('camgr-release-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $tmp | Out-Null
$relNotes = if ($Notes) { $Notes } else { "CA-Manager $Version" }

$relBody = @{ tag_name = $Version; name = "CA-Manager $Version"; description = $relNotes } | ConvertTo-Json
[IO.File]::WriteAllText((Join-Path $tmp 'rel.json'), $relBody)
$exist = & curl.exe -s -o (Join-Path $tmp 'exist.json') -w '%{http_code}' -H "PRIVATE-TOKEN: $token" "$api/releases/$Version"
if ($exist -eq '200') {
    Write-Warning "release $Version already exists on GitLab - updating its notes only"
    & curl.exe -s -X PUT "$api/releases/$Version" -H "PRIVATE-TOKEN: $token" -H "Content-Type: application/json" --data-binary "@$(Join-Path $tmp 'rel.json')" | Out-Null
} else {
    & curl.exe -s -o (Join-Path $tmp 'create.json') -X POST "$api/releases" -H "PRIVATE-TOKEN: $token" -H "Content-Type: application/json" --data-binary "@$(Join-Path $tmp 'rel.json')"
    if ($LASTEXITCODE -ne 0) { Die 'GitLab release creation failed' }
}
& curl.exe -s -o (Join-Path $tmp 'pkg.json') -X PUT "$api/packages/generic/ca-manager-deploy/$ver/$pkgName" `
    -H "PRIVATE-TOKEN: $token" -H "Content-Type: application/octet-stream" --data-binary "@$zipPath"
if ($LASTEXITCODE -ne 0) { Die 'GitLab package upload failed' }
$pkgUrl = "$api/packages/generic/ca-manager-deploy/$ver/$pkgName"
$linkBody = @{ name = "$pkgName (Windows x64, self-contained)"; url = $pkgUrl; link_type = 'package' } | ConvertTo-Json
[IO.File]::WriteAllText((Join-Path $tmp 'link.json'), $linkBody)
& curl.exe -s -o (Join-Path $tmp 'link-resp.json') -X POST "$api/releases/$Version/assets/links" `
    -H "PRIVATE-TOKEN: $token" -H "Content-Type: application/json" --data-binary "@$(Join-Path $tmp 'link.json')"
if ($LASTEXITCODE -ne 0) { Die 'GitLab asset link failed' }
$glRel = (& curl.exe -s -H "PRIVATE-TOKEN: $token" "$api/releases/$Version") | ConvertFrom-Json
if ($glRel.assets.links | Where-Object { $_.url -eq $pkgUrl }) {
    Write-Host "  GitLab release $Version created with $pkgName attached." -ForegroundColor Green
} else {
    Write-Warning 'the GitLab release exists but the asset link was not found - check the release page'
}

# ---------- 11. GitHub release ----------
Step 11 'GitHub release...'
$gh = $env:GITHUB_TOKEN
if (-not $gh) { $gh = git config --get ca-manager.githubToken }
if ($gh) {
    $ghBody = @{ tag_name = $Version; name = "CA-Manager $Version"; body = $relNotes } | ConvertTo-Json
    [IO.File]::WriteAllText((Join-Path $tmp 'gh.json'), $ghBody)
    & curl.exe -s -o (Join-Path $tmp 'gh-resp.json') -X POST "https://api.github.com/repos/$githubRepo/releases" `
        -H "Authorization: Bearer $gh" -H "Accept: application/vnd.github+json" -H "Content-Type: application/json" --data-binary "@$(Join-Path $tmp 'gh.json')"
    $relId = (Get-Content (Join-Path $tmp 'gh-resp.json') -Raw | ConvertFrom-Json).id
    if (-not $relId) {
        Write-Warning "GitHub release creation failed: $(Get-Content (Join-Path $tmp 'gh-resp.json') -Raw)"
    } else {
        & curl.exe -s -o (Join-Path $tmp 'gh-asset.json') -X POST "https://uploads.github.com/repos/$githubRepo/releases/$relId/assets?name=$pkgName" `
            -H "Authorization: Bearer $gh" -H "Content-Type: application/zip" --data-binary "@$zipPath"
        $asset = Get-Content (Join-Path $tmp 'gh-asset.json') -Raw | ConvertFrom-Json
        if ($asset.state -eq 'uploaded') {
            Write-Host ("  GitHub release created, asset uploaded ({0:N1} MB)." -f ($asset.size / 1MB)) -ForegroundColor Green
        } else {
            Write-Warning "GitHub asset upload failed: $($asset.message)"
        }
    }
} else {
    Write-Warning 'no GitHub token (git config ca-manager.githubToken <token>) - only the tag was pushed; create the release manually'
}
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "`nDone. Package: $zipPath" -ForegroundColor Green
