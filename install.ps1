# Jackamixer installer: builds the exe if needed, adds a Start menu entry and a
# startup entry (the hidden hotkey listener), then starts it. Nothing leaves this PC.
# Run from the folder you want Jackamixer to live in:  powershell -ExecutionPolicy Bypass -File install.ps1
$ErrorActionPreference = 'Stop'
$dir = $PSScriptRoot
$exe = Join-Path $dir 'Jackamixer.exe'

if (-not (Test-Path $exe)) {
    & (Join-Path $dir 'build.cmd')
    if (-not (Test-Path $exe)) { throw 'Build failed, see the compiler output above.' }
}

$ws = New-Object -ComObject WScript.Shell
$programs = [Environment]::GetFolderPath('Programs')
$startup = [Environment]::GetFolderPath('Startup')

$menu = $ws.CreateShortcut((Join-Path $programs 'Jackamixer.lnk'))
$menu.TargetPath = $exe
$menu.WorkingDirectory = $dir
$menu.Description = 'Volume mixer with level meters'
$menu.Save()

$opts = $ws.CreateShortcut((Join-Path $programs 'Jackamixer Options.lnk'))
$opts.TargetPath = $exe
$opts.Arguments = '--options'
$opts.WorkingDirectory = $dir
$opts.Description = 'Jackamixer hotkey and background'
$opts.Save()

$auto = $ws.CreateShortcut((Join-Path $startup 'Jackamixer (hotkey).lnk'))
$auto.TargetPath = $exe
$auto.Arguments = '--background'
$auto.WorkingDirectory = $dir
$auto.Save()

Start-Process $exe -ArgumentList '--background'
Write-Host 'Jackamixer installed. Press Win+\ to open it. Start menu > Jackamixer Options changes the hotkey and background.'
