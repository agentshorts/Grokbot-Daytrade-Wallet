# Registers a Windows scheduled task that starts TradeMonitor at logon.
# Remove it with:
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts\install-startup-task.ps1 -Remove

[CmdletBinding()]
param(
    [switch]$Remove
)

$ErrorActionPreference = "Stop"
$TaskName = "TradeMonitor"

$onWindows = $false
if ($env:OS -eq "Windows_NT") { $onWindows = $true }
if (Test-Path variable:IsWindows) { $onWindows = [bool]$IsWindows }
if (-not $onWindows) {
    [Console]::Error.WriteLine("The startup task is a Windows scheduled task. This script only runs on Windows.")
    exit 1
}

if ($Remove) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
    Write-Host "Removed scheduled task $TaskName if it existed."
    exit 0
}

$script = Join-Path $PSScriptRoot "run.ps1"
if (-not (Test-Path -LiteralPath $script)) {
    [Console]::Error.WriteLine("scripts\run.ps1 was not found.")
    exit 1
}

$root = Split-Path -Parent $PSScriptRoot
$action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$script`"" -WorkingDirectory $root
$trigger = New-ScheduledTaskTrigger -AtLogOn
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew -ExecutionTimeLimit ([TimeSpan]::Zero)
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -Force | Out-Null
Write-Host "Registered scheduled task $TaskName. It runs scripts\run.ps1 at logon and keeps TradeMonitor on 127.0.0.1."
Write-Host "Remove it with: powershell -NoProfile -ExecutionPolicy Bypass -File scripts\install-startup-task.ps1 -Remove"
