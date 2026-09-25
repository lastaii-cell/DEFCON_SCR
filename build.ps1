<#
.SYNOPSIS
    Builds DefconSaver and produces a single-file DefconSaver.scr.

.PARAMETER SelfContained
    Bundle the .NET runtime into the .scr (~59 MB, runs on any Windows x64 machine).
    Without it the .scr is ~2.4 MB and needs the .NET 8 Desktop Runtime installed.

.PARAMETER OutDir
    Where to put the finished screensaver. Defaults to .\dist
#>
[CmdletBinding()]
param(
    [switch]$SelfContained,
    [string]$OutDir = (Join-Path $PSScriptRoot 'dist')
)

$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force }

    $pubArgs = @(
        'publish', '-c', 'Release', '-r', 'win-x64', '--nologo',
        '-p:PublishSingleFile=true', '-p:DebugType=none',
        '-o', $OutDir
    )
    if ($SelfContained) {
        $pubArgs += @('--self-contained', 'true', '-p:EnableCompressionInSingleFile=true')
    } else {
        $pubArgs += @('--self-contained', 'false')
    }

    Write-Host "Publishing ($(if ($SelfContained) {'self-contained'} else {'framework-dependent'}))..." -ForegroundColor Cyan
    dotnet @pubArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

    $exe = Join-Path $OutDir 'DefconSaver.exe'
    $scr = Join-Path $OutDir 'DefconSaver.scr'
    if (-not (Test-Path $exe)) { throw "Expected $exe but it was not produced." }
    if (Test-Path $scr) { Remove-Item $scr -Force }
    Move-Item $exe $scr

    # A single-file publish can still drop a stray .pdb or .xml next to it.
    Get-ChildItem $OutDir -File | Where-Object { $_.Name -ne 'DefconSaver.scr' } | Remove-Item -Force

    $size = '{0:N1} MB' -f ((Get-Item $scr).Length / 1MB)
    Write-Host ""
    Write-Host "Built $scr ($size)" -ForegroundColor Green
    Write-Host ""
    Write-Host "Try it:      .\dist\DefconSaver.scr /w        (in a window)"
    Write-Host "Settings:    .\dist\DefconSaver.scr /c"
    Write-Host "Install:     .\install.ps1                    (sets it as your screensaver)"
}
finally {
    Pop-Location
}
