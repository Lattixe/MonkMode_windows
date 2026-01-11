@echo off
:: Monk Mode Auto-Start Setup
:: This script requests admin privileges and runs the PowerShell setup

:: Check for admin privileges
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo Requesting administrator privileges...
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

:: Run the PowerShell script
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Setup-AutoStart.ps1"
