param([Parameter(Mandatory = $true)][string]$AppDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$AppDirectory = (Resolve-Path $AppDirectory).Path
foreach ($name in @('Desktop', 'Runner')) {
    $marker = Join-Path ([System.IO.Path]::GetTempPath()) ('cms-smoke-' + [guid]::NewGuid().ToString('N') + '.txt')
    $exe = Join-Path $AppDirectory "ContextMenuStudio.$name.exe"
    if (-not (Test-Path $exe)) { throw "Missing executable: $exe" }
    try {
        $process = Start-Process -FilePath $exe -ArgumentList @('--smoke-test', ('"' + $marker + '"')) -PassThru
        if (-not $process.WaitForExit(30000)) { $process.Kill(); throw "$name did not render and exit within 30 seconds." }
        if ($process.ExitCode -ne 0 -or -not (Test-Path $marker)) { throw "$name launch check failed (exit $($process.ExitCode))." }
        Write-Host "PASS ${name}: $(Get-Content $marker -Raw)"
    } finally { if (Test-Path $marker) { Remove-Item $marker -Force } }
}
