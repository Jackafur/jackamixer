# Jackamixer uninstaller: stops the hotkey listener and removes both shortcuts.
# The folder itself (exe, settings) is left alone; delete it yourself afterwards.
$dir = $PSScriptRoot
$exe = Join-Path $dir 'Jackamixer.exe'
if (Test-Path $exe) { Start-Process $exe -ArgumentList '--quit' -Wait }

$programs = [Environment]::GetFolderPath('Programs')
$startup = [Environment]::GetFolderPath('Startup')
Remove-Item (Join-Path $programs 'Jackamixer.lnk') -ErrorAction SilentlyContinue
Remove-Item (Join-Path $startup 'Jackamixer (hotkey).lnk') -ErrorAction SilentlyContinue
Write-Host 'Jackamixer removed. You can delete this folder now.'
