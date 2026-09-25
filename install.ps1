<#
.SYNOPSIS
    Makes DefconSaver your active screensaver.

.DESCRIPTION
    Writes three values under HKCU\Control Panel\Desktop:
        SCRNSAVE.EXE        the full path to DefconSaver.scr
        ScreenSaveActive    1
        ScreenSaveTimeOut   the idle delay, in seconds

    Per-user only: no administrator rights needed, and nothing outside your own
    profile is touched. Use -Revert to undo it.

    Windows' Screen Saver dropdown only lists .scr files living in
    %SystemRoot%\System32, so this sets the saver directly instead. To get it
    into the dropdown as well, copy dist\DefconSaver.scr into System32 from an
    elevated prompt.

.PARAMETER Path
    The .scr to install. Defaults to .\dist\DefconSaver.scr

.PARAMETER TimeoutMinutes
    Idle minutes before it kicks in. Defaults to 5.

.PARAMETER Revert
    Turn the screensaver off again and clear the entry.
#>
[CmdletBinding()]
param(
    [string]$Path = (Join-Path $PSScriptRoot 'dist\DefconSaver.scr'),
    [int]$TimeoutMinutes = 5,
    [switch]$Revert
)

$ErrorActionPreference = 'Stop'
$key = 'HKCU:\Control Panel\Desktop'

if ($Revert) {
    Set-ItemProperty -Path $key -Name 'ScreenSaveActive' -Value '0'
    Remove-ItemProperty -Path $key -Name 'SCRNSAVE.EXE' -ErrorAction SilentlyContinue
    Write-Host "Screensaver turned off and the DEFCON entry removed." -ForegroundColor Green
    Write-Host "Sign out and back in, or open Lock screen settings, for Windows to notice."
    return
}

$full = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path
if ([IO.Path]::GetExtension($full) -ne '.scr') {
    throw "$full is not a .scr file. Run .\build.ps1 first."
}

$seconds = [Math]::Max(60, $TimeoutMinutes * 60)

Write-Host "About to set your screensaver to:" -ForegroundColor Cyan
Write-Host "  $full"
Write-Host "  idle timeout: $TimeoutMinutes minute(s)"
Write-Host ""

Set-ItemProperty -Path $key -Name 'SCRNSAVE.EXE'      -Value $full
Set-ItemProperty -Path $key -Name 'ScreenSaveActive'  -Value '1'
Set-ItemProperty -Path $key -Name 'ScreenSaveTimeOut' -Value "$seconds"

Write-Host "Done." -ForegroundColor Green
Write-Host "Windows caches these, so it may take a sign-out (or a visit to"
Write-Host "Settings > Personalisation > Lock screen > Screen saver) to take effect."
Write-Host ""
Write-Host "Undo with:  .\install.ps1 -Revert"
