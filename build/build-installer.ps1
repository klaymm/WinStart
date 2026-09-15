$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$app = Join-Path $root "src\WinStart.App\WinStart.App.csproj"
$installer = Join-Path $root "src\WinStart.Installer\WinStart.Installer.csproj"
$publishDir = Join-Path $root "build\publish\app"
$payloadDir = Join-Path $root "src\WinStart.Installer\Payload"
$payloadZip = Join-Path $payloadDir "app.zip"

Write-Host "==> 1/4  Publishing app..." -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish $app -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -o $publishDir | Out-Null

Write-Host "==> 2/4  Verifying bundled Tools..." -ForegroundColor Cyan
$tools = Join-Path $publishDir "Tools"
if (-not (Test-Path $tools)) {
    throw "Tools folder missing from publish output - check the Tools\**\* item in WinStart.App.csproj."
}
$toolCount = (Get-ChildItem $tools -Recurse -File | Measure-Object).Count
Write-Host "    Tools: $toolCount files"

Write-Host "==> 3/4  Packing payload..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $payloadDir | Out-Null
if (Test-Path $payloadZip) { Remove-Item $payloadZip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $publishDir, $payloadZip,
    [System.IO.Compression.CompressionLevel]::Optimal, $false)
$zipMb = [math]::Round((Get-Item $payloadZip).Length / 1MB, 1)
Write-Host "    app.zip: $zipMb MB"

Write-Host "==> 4/4  Publishing installer as a single exe..." -ForegroundColor Cyan

foreach ($d in @("bin", "obj")) {
    $p = Join-Path $root "src\WinStart.Installer\$d"
    if (Test-Path $p) { Remove-Item $p -Recurse -Force }
}

$dist = Join-Path $root "build\dist"
if (Test-Path $dist) { Get-ChildItem $dist -Force | Remove-Item -Recurse -Force }

dotnet publish $installer -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None -p:GenerateDocumentationFile=false -o $dist | Out-Null

$setup = Join-Path $dist "WinStartSetup.exe"
if (Test-Path $setup) {
    $mb = [math]::Round((Get-Item $setup).Length / 1MB, 1)
    Write-Host ""
    Write-Host "Done: $setup ($mb MB)" -ForegroundColor Green
} else {
    throw "WinStartSetup.exe was not produced."
}
