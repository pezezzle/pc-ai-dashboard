$ErrorActionPreference = 'Stop'

$executable = Join-Path $PSScriptRoot 'artifacts\app\PcAiDashboard.exe'
$icon = Join-Path $PSScriptRoot 'assets\branding\pc-ai-dashboard.ico'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw 'Build the app first with .\Build.ps1 -Publish.'
}
if (-not (Test-Path -LiteralPath $icon -PathType Leaf)) {
    throw "The dashboard icon is missing: $icon"
}

$desktop = [Environment]::GetFolderPath('Desktop')
$shortcutPath = Join-Path $desktop 'PC AI Dashboard.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $executable
$shortcut.WorkingDirectory = Split-Path -Parent $executable
$shortcut.IconLocation = "$icon,0"
$shortcut.Description = 'Start PC / AI Dashboard'
$shortcut.WindowStyle = 1
$shortcut.Save()

Write-Host "Desktop shortcut created: $shortcutPath"
