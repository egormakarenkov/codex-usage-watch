param([switch]$NoLaunch)
$ErrorActionPreference = 'Stop'
$sourceExe = Join-Path $PSScriptRoot 'CodexUsageWatch.exe'
if (-not (Test-Path -LiteralPath $sourceExe)) { throw 'Build CodexUsageWatch.exe first using build.ps1.' }
$destination = Join-Path $env:LOCALAPPDATA 'Programs\CodexUsageWatch'
$installedExe = Join-Path $destination 'CodexUsageWatch.exe'
$existing = Get-Process CodexUsageWatch -ErrorAction SilentlyContinue
if ($existing) { throw 'Exit Codex Usage Watch using its tray menu before installing an update.' }
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item -LiteralPath $sourceExe -Destination $installedExe -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $destination 'README.md') -Force
$shell = New-Object -ComObject WScript.Shell
$startupShortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Startup')) 'Codex Usage Watch.lnk'))
$startupShortcut.TargetPath = $installedExe
$startupShortcut.Arguments = '--background'
$startupShortcut.WorkingDirectory = $destination
$startupShortcut.Description = 'Windows and Telegram alerts when Codex weekly allowance reaches 2% remaining'
$startupShortcut.WindowStyle = 7
$startupShortcut.Save()
$startMenuShortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Programs')) 'Codex Usage Watch.lnk'))
$startMenuShortcut.TargetPath = $installedExe
$startMenuShortcut.WorkingDirectory = $destination
$startMenuShortcut.Description = 'Codex weekly usage alerts and Telegram setup'
$startMenuShortcut.Save()
Write-Output "Installed: $installedExe"
Write-Output "Starts at Windows sign-in. Settings: $env:LOCALAPPDATA\CodexUsageWatch"
if (-not $NoLaunch) { Start-Process -FilePath $installedExe }
