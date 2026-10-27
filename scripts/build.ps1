param(
    [ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64',
    [switch]$FrameworkDependent,
    [switch]$Launch
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install the .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0 and reopen your terminal.' }
    $sdk = & dotnet --version
    if ($LASTEXITCODE -ne 0 -or $sdk -notmatch '^10\.') { throw 'A .NET 10 SDK is required; global.json selects the 10.0 feature band.' }
    & dotnet build ContextMenuStudio.sln -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    & dotnet run --project tests/ContextMenuStudio.Tests -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed. See the errors above.' }
    $output = Join-Path $projectRoot 'artifacts/app'
    $staging = Join-Path $projectRoot 'artifacts/staging'
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
    $selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
    $extraPublishArgs = @()
    if ($selfContained -eq 'true') {
        $nugetPackages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $HOME '.nuget\packages' }
        $desktopPackDir = Join-Path $nugetPackages "microsoft.windowsdesktop.app.runtime.$Runtime"
        if (Test-Path $desktopPackDir) {
            $cachedVersion = Get-ChildItem $desktopPackDir -Directory |
                Where-Object { $_.Name -match '^\d+\.\d+\.\d+' } |
                Sort-Object { [version]($_.Name -replace '-.*$') } -Descending |
                Select-Object -First 1 -ExpandProperty Name
            if ($cachedVersion) {
                $extraPublishArgs += "-p:RuntimeFrameworkVersion=$cachedVersion"
            }
        }
    }
    foreach ($name in @('Runner', 'Desktop')) {
        & dotnet publish "src/ContextMenuStudio.$name/ContextMenuStudio.$name.csproj" -c Release -r $Runtime --self-contained $selfContained -p:PublishSingleFile=false -p:PublishTrimmed=false -o $staging --nologo @extraPublishArgs
        if ($LASTEXITCODE -ne 0) { throw "Publishing $name failed." }
    }
    Copy-Item (Join-Path $projectRoot 'third-party') (Join-Path $staging 'third-party') -Recurse -Force
    & (Join-Path $PSScriptRoot 'smoke-test.ps1') -AppDirectory $staging
    if (Test-Path $output) { Remove-Item $output -Recurse -Force }
    Move-Item $staging $output
    Write-Host "Build and launch checks passed. Application: $output" -ForegroundColor Green
    if ($Launch) { Start-Process (Join-Path $output 'ContextMenuStudio.Desktop.exe') }
} finally { Pop-Location }
