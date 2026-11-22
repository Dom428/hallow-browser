# Hallow Browser installer: copies the browser to your user folder (no admin needed) and adds Start menu + Desktop shortcuts.
# It registers Hallow Browser with Windows as a browser you CAN pick (per user), but it never makes itself your default browser:
# if you want that, choose it yourself in Windows Settings > Apps > Default apps (Hallow Browser's Settings > General > Default browser
# takes you there). 1.12: the registration is done by "HallowBrowser.exe --register" (URL and file ProgIDs, Capabilities,
# OpenWithProgids, RegisteredApplications - never your choice of default, which Windows protects).
#   Install.ps1 [-NoStart]   (-NoStart: don't open the browser afterwards)
param([switch]$NoStart)
$ErrorActionPreference = 'Stop'
$dest = Join-Path $env:LOCALAPPDATA 'Programs\Hallow Browser'
New-Item -ItemType Directory -Force $dest | Out-Null
$wasRunning = [bool](Get-Process HallowBrowser -ErrorAction SilentlyContinue)
# close it gently (it saves your tabs), then make sure it's gone
foreach ($p in Get-Process HallowBrowser -ErrorAction SilentlyContinue) { try { [void]$p.CloseMainWindow() } catch { } }
for ($i = 0; $i -lt 20 -and (Get-Process HallowBrowser -ErrorAction SilentlyContinue); $i++) { Start-Sleep -Milliseconds 250 }
Get-Process HallowBrowser -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
foreach ($f in 'HallowBrowser.exe', 'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.Wpf.dll', 'WebView2Loader.dll', 'README.txt', 'Uninstall.ps1', 'FontAwesome-LICENSE.txt', 'WebView2-LICENSE.txt', 'WebView2-NOTICE.txt') {
    Copy-Item (Join-Path $PSScriptRoot $f) $dest -Force
}
# Hallow Shield (built in): replaced as a whole so no old files stay behind
$shield = Join-Path $dest 'shield'
if (Test-Path $shield) { Remove-Item $shield -Recurse -Force }
Copy-Item (Join-Path $PSScriptRoot 'shield') $shield -Recurse -Force
$exe = Join-Path $dest 'HallowBrowser.exe'
# Windows Settings > Apps entry (per user, no admin)
$u = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\HallowBrowser'
New-Item $u -Force | Out-Null
$ver = (Get-Item $exe).VersionInfo.FileVersion -replace '\.0\.0$', ''
$vals = @{ DisplayName = 'Hallow Browser'; DisplayVersion = $ver; Publisher = 'Hallow'; DisplayIcon = $exe; InstallLocation = $dest
    UninstallString = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$(Join-Path $dest 'Uninstall.ps1')`""
    URLInfoAbout = 'https://github.com/Dom428/hallow-browser' }
foreach ($k in $vals.Keys) { Set-ItemProperty $u -Name $k -Value $vals[$k] }
Set-ItemProperty $u -Name NoModify -Value 1 -Type DWord; Set-ItemProperty $u -Name NoRepair -Value 1 -Type DWord
Set-ItemProperty $u -Name EstimatedSize -Value ([int]((Get-ChildItem $dest -Recurse -File | Measure-Object Length -Sum).Sum / 1KB)) -Type DWord
# "a browser you can pick" (Default apps lists it for http, https, .html, .htm, .pdf, .svg ...) - this does NOT change which
# browser opens your links: that stays your choice in Windows Settings. The browser writes it (and tells Windows to refresh).
$reg = Start-Process $exe -ArgumentList '--register' -Wait -PassThru
if ($reg.ExitCode -ne 0) { Write-Host "Note: Hallow Browser couldn't register itself with Windows (exit $($reg.ExitCode)). It still works; it may not be listed in Default apps." }
$w = New-Object -ComObject WScript.Shell
foreach ($dir in [Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Desktop')) {
    $s = $w.CreateShortcut((Join-Path $dir 'Hallow Browser.lnk'))
    $s.TargetPath = $exe; $s.WorkingDirectory = $dest; $s.Description = 'A fast browser for gamers with Hallow Shield built in'; $s.Save()
}
$rt = $null
foreach ($k in 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'HKCU:\Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}') { try { $rt = (Get-ItemProperty $k -ErrorAction Stop).pv; break } catch { } }
if (-not $rt) { Write-Host "Note: Microsoft Edge WebView2 Runtime wasn't found. Hallow Browser will show where to get it from Microsoft." }
if ($NoStart) { Write-Host "Hallow Browser is installed. It's in your Start menu and on your Desktop."; return }
# after an update it opens again with your tabs; a fresh install opens it too
Start-Process $exe
Write-Host "Hallow Browser is installed. It's in your Start menu and on your Desktop."
