# Hallow Browser uninstaller (run from Windows Settings > Apps, or directly).
# Your browsing data (cookies, history, site data) is kept unless you say to remove it.
$dest = Join-Path $env:LOCALAPPDATA 'Programs\Hallow Browser'
Add-Type -AssemblyName System.Windows.Forms
if ([System.Windows.Forms.MessageBox]::Show("Uninstall Hallow Browser?", 'Uninstall Hallow Browser', 'YesNo', 'Question') -ne 'Yes') { return }
foreach ($p in Get-Process HallowBrowser -ErrorAction SilentlyContinue) { try { [void]$p.CloseMainWindow() } catch { } }
Start-Sleep -Seconds 2
Get-Process HallowBrowser -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 600
$data = [System.Windows.Forms.MessageBox]::Show("Also remove your Hallow Browser data - cookies, history, site data, open tabs and settings?`n`nNo = keep it (in %LOCALAPPDATA%\HallowBrowser and %APPDATA%\HallowBrowser) for a later reinstall.", 'Uninstall Hallow Browser', 'YesNo', 'Question')
foreach ($dir in [Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Desktop')) { Remove-Item (Join-Path $dir 'Hallow Browser.lnk') -ErrorAction SilentlyContinue }
# 1.6: the Start menu shortcuts of sites you installed as apps (only the .lnk files the browser made there)
$apps = Join-Path ([Environment]::GetFolderPath('Programs')) 'Hallow Browser Apps'
if (Test-Path $apps) { Get-ChildItem $apps -Filter *.lnk | Remove-Item -ErrorAction SilentlyContinue; if (-not (Get-ChildItem $apps -ErrorAction SilentlyContinue)) { Remove-Item $apps -ErrorAction SilentlyContinue } }
# 1.12: the browser removes its own registration (ProgIDs, OpenWithProgids, Capabilities) and tells Windows; the lines after it are a fallback
$exe = Join-Path $dest 'HallowBrowser.exe'
if (Test-Path $exe) { try { Start-Process $exe -ArgumentList '--unregister' -Wait } catch { } }
Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\HallowBrowser' -Recurse -ErrorAction SilentlyContinue
Remove-Item 'HKCU:\Software\Clients\StartMenuInternet\HallowBrowser' -Recurse -ErrorAction SilentlyContinue
Remove-Item 'HKCU:\Software\Classes\HallowBrowserURL' -Recurse -ErrorAction SilentlyContinue
Remove-Item 'HKCU:\Software\Classes\HallowBrowserHTML' -Recurse -ErrorAction SilentlyContinue
foreach ($x in '.htm', '.html', '.shtml', '.xht', '.xhtml', '.svg', '.pdf', '.webp') { Remove-ItemProperty "HKCU:\Software\Classes\$x\OpenWithProgids" -Name 'HallowBrowserHTML' -ErrorAction SilentlyContinue }
Remove-ItemProperty 'HKCU:\Software\RegisteredApplications' -Name 'Hallow Browser' -ErrorAction SilentlyContinue
if ($data -eq 'Yes') {
    Remove-Item (Join-Path $env:APPDATA 'HallowBrowser') -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $env:LOCALAPPDATA 'HallowBrowser') -Recurse -Force -ErrorAction SilentlyContinue
}
Start-Process cmd.exe -ArgumentList "/c timeout /t 2 /nobreak >nul & rmdir /s /q `"$dest`"" -WindowStyle Hidden
[System.Windows.Forms.MessageBox]::Show('Hallow Browser is uninstalled.', 'Hallow Browser') | Out-Null
