#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Sets up Monk Mode to auto-start with administrator privileges on login.
.DESCRIPTION
    Creates a Windows Task Scheduler task that runs Monk Mode at user login
    with elevated (admin) privileges. This enables DNS blocking and other
    system-level features.
.NOTES
    Run this script as Administrator (right-click > Run as Administrator)
#>

$ErrorActionPreference = "Stop"

# Configuration
$TaskName = "MonkMode-AutoStart"
$TaskDescription = "Starts Monk Mode with administrator privileges at user login"
$ExePath = Join-Path $PSScriptRoot "bin\Debug\net8.0-windows\MonkMode.exe"

# Check if exe exists
if (-not (Test-Path $ExePath)) {
    # Try release path
    $ExePath = Join-Path $PSScriptRoot "bin\Release\net8.0-windows\MonkMode.exe"
    if (-not (Test-Path $ExePath)) {
        Write-Host "❌ MonkMode.exe not found. Please build the project first." -ForegroundColor Red
        Write-Host "   Run: dotnet build" -ForegroundColor Yellow
        exit 1
    }
}

Write-Host ""
Write-Host "🧘 Monk Mode Auto-Start Setup" -ForegroundColor Cyan
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray
Write-Host ""

# Remove existing task if present
$existingTask = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($existingTask) {
    Write-Host "→ Removing existing task..." -ForegroundColor Yellow
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

# Create the scheduled task
Write-Host "→ Creating scheduled task..." -ForegroundColor White

# Action: Run MonkMode.exe
$Action = New-ScheduledTaskAction -Execute $ExePath

# Trigger: At user logon
$Trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME

# Principal: Run with highest privileges (admin)
$Principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -RunLevel Highest -LogonType Interactive

# Settings
$Settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -StartWhenAvailable `
    -ExecutionTimeLimit (New-TimeSpan -Hours 0) `
    -RestartCount 3 `
    -RestartInterval (New-TimeSpan -Minutes 1)

# Register the task
$Task = Register-ScheduledTask `
    -TaskName $TaskName `
    -Description $TaskDescription `
    -Action $Action `
    -Trigger $Trigger `
    -Principal $Principal `
    -Settings $Settings

Write-Host ""
Write-Host "✅ Success! Monk Mode will now auto-start on login." -ForegroundColor Green
Write-Host ""
Write-Host "   Task Name: $TaskName" -ForegroundColor DarkGray
Write-Host "   Exe Path:  $ExePath" -ForegroundColor DarkGray
Write-Host "   Trigger:   At logon ($env:USERNAME)" -ForegroundColor DarkGray
Write-Host "   Privileges: Administrator" -ForegroundColor DarkGray
Write-Host ""
Write-Host "To remove auto-start later, run:" -ForegroundColor Yellow
Write-Host "   Unregister-ScheduledTask -TaskName '$TaskName' -Confirm:`$false" -ForegroundColor DarkGray
Write-Host ""
Write-Host "Press any key to exit..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
