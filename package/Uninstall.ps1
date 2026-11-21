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
Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\HallowBrowser' -Recurse -ErrorAction SilentlyContinue
Remove-Item 'HKCU:\Software\Clients\StartMenuInternet\HallowBrowser' -Recurse -ErrorAction SilentlyContinue
Remove-Item 'HKCU:\Software\Classes\HallowBrowserURL' -Recurse -ErrorAction SilentlyContinue
Remove-ItemProperty 'HKCU:\Software\RegisteredApplications' -Name 'Hallow Browser' -ErrorAction SilentlyContinue
if ($data -eq 'Yes') {
    Remove-Item (Join-Path $env:APPDATA 'HallowBrowser') -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $env:LOCALAPPDATA 'HallowBrowser') -Recurse -Force -ErrorAction SilentlyContinue
}
Start-Process cmd.exe -ArgumentList "/c timeout /t 2 /nobreak >nul & rmdir /s /q `"$dest`"" -WindowStyle Hidden
[System.Windows.Forms.MessageBox]::Show('Hallow Browser is uninstalled.', 'Hallow Browser') | Out-Null
