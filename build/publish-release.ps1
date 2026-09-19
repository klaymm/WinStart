param(
    [string]$Git = 'git',
    [string]$MinimumVersion = '1.0.0',
    [string]$Repository = 'klaymm/WinStart',
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $PSScriptRoot 'dist'
$setup = Join-Path $dist 'WinStartSetup.exe'
$manifestPath = Join-Path $dist 'update-manifest.json'
$utf8 = New-Object Text.UTF8Encoding $false

# ---------- Версия и заметки ----------

$props = [xml](Get-Content (Join-Path $root 'Directory.Build.props') -Raw -Encoding UTF8)
$version = @($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0].Trim()
if (-not $version) { throw 'Version not found in Directory.Build.props' }
$preRelease = $version.Contains('-')

if (-not (Test-Path $setup)) { throw "Installer not found: $setup" }

function Read-Changelog([string]$file) {
    $loc = Get-Content (Join-Path $root "src\WinStart.App\Localization\$file") -Raw -Encoding UTF8 | ConvertFrom-Json
    $items = @()
    for ($n = 1; ; $n++) {
        $text = $loc."changelog.$version.$n"
        if (-not $text) { break }
        $items += $text
    }
    return , $items
}

$ru = Read-Changelog 'ru.json'
$en = Read-Changelog 'en.json'
if ($ru.Count -eq 0 -or $ru.Count -ne $en.Count) { throw "Changelog for $version is missing or ru/en counts differ" }

$body = "## Что нового`n`n" + (($ru | ForEach-Object { "- $_" }) -join "`n") + "`n`n---`n`n" + (($en | ForEach-Object { "- $_" }) -join "`n")

# ---------- Манифест ----------

$sha = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
$manifest = [ordered]@{
    version        = $version
    minimumVersion = $MinimumVersion
    installers     = @([ordered]@{ architecture = 'x64'; file = 'WinStartSetup.exe'; sha256 = $sha })
}
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 5), $utf8)

"Version:    $version$(if ($preRelease) { ' (beta)' })"
"SHA-256:    $sha"
"Manifest:   $manifestPath"

if ($DryRun) {
    ''
    $body
    return
}

& $Git rev-parse --verify --quiet "refs/tags/v$version" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Tag v$version does not exist locally" }

# ---------- GitHub ----------

$credIn = Join-Path $env:TEMP 'winstart-cred-in.txt'
[IO.File]::WriteAllText($credIn, "protocol=https`nhost=github.com`n`n", $utf8)
$previousInteractive = $env:GCM_INTERACTIVE
try {
    $env:GCM_INTERACTIVE = 'Never'
    $lines = cmd /d /c "`"$Git`" -c credential.helper= -c credential.helper=manager credential fill < `"$credIn`""
}
finally {
    Remove-Item $credIn -ErrorAction SilentlyContinue
    $env:GCM_INTERACTIVE = $previousInteractive
}
$token = ($lines | Where-Object { $_ -like 'password=*' } | Select-Object -First 1) -replace '^password=', ''
if (-not $token) { throw 'No stored GitHub credential' }

$headers = @{
    Authorization          = "Bearer $token"
    Accept                 = 'application/vnd.github+json'
    'X-GitHub-Api-Version' = '2022-11-28'
    'User-Agent'           = 'WinStart-release'
}

function Invoke-Retry([scriptblock]$action) {
    for ($i = 1; $i -le 3; $i++) {
        try { return & $action } catch { if ($i -eq 3) { throw }; Start-Sleep -Seconds 10 }
    }
}

$release = $null
try { $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository/releases/tags/v$version" -Headers $headers } catch { }

if ($release) {
    "Release already exists: $($release.html_url)"
}
else {
    $payload = @{
        tag_name    = "v$version"
        name        = "WinStart $version"
        body        = $body
        draft       = $false
        prerelease  = $preRelease
        make_latest = $(if ($preRelease) { 'false' } else { 'true' })
    } | ConvertTo-Json
    $release = Invoke-Retry { Invoke-RestMethod -Method Post -Uri "https://api.github.com/repos/$Repository/releases" `
        -Headers $headers -ContentType 'application/json; charset=utf-8' `
        -Body ([Text.Encoding]::UTF8.GetBytes($payload)) }
    "Release created: $($release.html_url)"
}

$uploadBase = $release.upload_url -replace '\{.*\}$', ''
foreach ($asset in @(
        @{ Name = 'WinStartSetup.exe'; Path = $setup; Type = 'application/octet-stream' },
        @{ Name = 'update-manifest.json'; Path = $manifestPath; Type = 'application/json' })) {
    if ($release.assets | Where-Object name -eq $asset.Name) {
        "Already uploaded: $($asset.Name)"
        continue
    }
    $uploaded = Invoke-Retry { Invoke-RestMethod -Method Post -Uri "$uploadBase`?name=$($asset.Name)" -Headers $headers `
        -ContentType $asset.Type -InFile $asset.Path -TimeoutSec 3600 }
    "Uploaded: $($uploaded.name) $([math]::Round($uploaded.size / 1KB)) KB"
}
