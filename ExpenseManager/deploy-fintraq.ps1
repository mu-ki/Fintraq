# Deploy ExpenseManager to fintraq.runasp.net via Web Deploy (MSDeploy).
# Requires: Windows, MSBuild (Visual Studio or Build Tools), Web Deploy.
# Run from ExpenseManager folder: .\deploy-fintraq.ps1

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptDir

$publishSettingsPath = Join-Path $scriptDir "fintraq.runasp.net-WebDeploy.publishSettings"
if (-not (Test-Path $publishSettingsPath)) {
    Write-Error "Missing: fintraq.runasp.net-WebDeploy.publishSettings"
    exit 1
}

[xml]$xml = Get-Content $publishSettingsPath -Encoding UTF8
$ns = @{ d = "http://schemas.microsoft.com/developer/msbuild/2003" }
$profile = $xml.publishData.publishProfile
$password = $profile.userPWD

if (-not $password) {
    Write-Error "Could not read userPWD from publish settings."
    exit 1
}

$msbuild = "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
if (-not (Test-Path $msbuild)) {
    $msbuild = "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe"
}
if (-not (Test-Path $msbuild)) {
    $msbuild = "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
}
if (-not (Test-Path $msbuild)) {
    $msbuild = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
}
if (-not (Test-Path $msbuild)) {
    Write-Error "MSBuild not found. Install Visual Studio 2022 or Build Tools with Web Deploy."
    exit 1
}

$projectPath = Join-Path $scriptDir "ExpenseManager.csproj"
Write-Host "Deploying to fintraq.runasp.net (site56963)..." -ForegroundColor Cyan
& $msbuild $projectPath `
    /p:Configuration=Release `
    /p:DeployOnBuild=true `
    /p:PublishProfile=fintraq-WebDeploy `
    /p:Password=$password `
    /p:AllowUntrustedCertificate=true

if ($LASTEXITCODE -ne 0) {
    Write-Error "Deploy failed."
    exit 1
}
Write-Host "Deploy succeeded. Site: http://fintraq.runasp.net/" -ForegroundColor Green
