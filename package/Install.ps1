# Hallow Browser installer: copies the browser to your user folder (no admin needed) and adds Start menu + Desktop shortcuts.
# It registers Hallow Browser with Windows as a browser you CAN pick (per user), but it never makes itself your default browser:
# if you want that, choose it yourself in Windows Settings > Apps > Default apps.
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
# "a browser you can pick" (Default apps lists it) - this does NOT change which browser opens your links
$cls = 'HKCU:\Software\Classes\HallowBrowserURL'
New-Item "$cls\shell\open\command" -Force | Out-Null
Set-ItemProperty $cls -Name '(default)' -Value 'Hallow Browser web address'
Set-ItemProperty $cls -Name 'URL Protocol' -Value ''
New-Item "$cls\DefaultIcon" -Force | Out-Null; Set-ItemProperty "$cls\DefaultIcon" -Name '(default)' -Value "`"$exe`",0"
Set-ItemProperty "$cls\shell\open\command" -Name '(default)' -Value "`"$exe`" `"%1`""
$cap = 'HKCU:\Software\Clients\StartMenuInternet\HallowBrowser'
New-Item "$cap\Capabilities\URLAssociations" -Force | Out-Null
New-Item "$cap\shell\open\command" -Force | Out-Null
New-Item "$cap\DefaultIcon" -Force | Out-Null
Set-ItemProperty $cap -Name '(default)' -Value 'Hallow Browser'
Set-ItemProperty "$cap\DefaultIcon" -Name '(default)' -Value "`"$exe`",0"
Set-ItemProperty "$cap\shell\open\command" -Name '(default)' -Value "`"$exe`""
Set-ItemProperty "$cap\Capabilities" -Name 'ApplicationName' -Value 'Hallow Browser'
Set-ItemProperty "$cap\Capabilities" -Name 'ApplicationDescription' -Value 'A fast browser for gamers with Hallow Shield built in'
Set-ItemProperty "$cap\Capabilities" -Name 'ApplicationIcon' -Value "`"$exe`",0"
foreach ($p in 'http', 'https') { Set-ItemProperty "$cap\Capabilities\URLAssociations" -Name $p -Value 'HallowBrowserURL' }
New-Item 'HKCU:\Software\RegisteredApplications' -Force -ErrorAction SilentlyContinue | Out-Null
Set-ItemProperty 'HKCU:\Software\RegisteredApplications' -Name 'Hallow Browser' -Value 'Software\Clients\StartMenuInternet\HallowBrowser\Capabilities'
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
