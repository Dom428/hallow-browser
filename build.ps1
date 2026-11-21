# Builds HallowBrowser.exe with the C# compiler that ships with Windows (.NET Framework 4.8) - nothing to install.
# The only outside parts are Microsoft's WebView2 SDK DLLs in lib\ (NuGet Microsoft.Web.WebView2, see README) and, for the
# package, Hallow Shield's Chrome build in package\shield (tools\get-shield.ps1 fetches it from Shield's GitHub release, sha256-checked).
#   .\build.ps1          the app (x64)
#   .\build.ps1 -Tests   also tests\BrowserTests.exe (unit tests; "BrowserTests.exe --integration <exe> <work folder>" drives the
#                        real browser off-screen against local test pages, a stand-in Hallow Keys and a fake game)
param([switch]$Tests)
$ErrorActionPreference = 'Stop'
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$wpf = "$fw\WPF"
$src = Join-Path $PSScriptRoot 'src'
$lib = Join-Path $PSScriptRoot 'lib'
$out = Join-Path $PSScriptRoot 'HallowBrowser.exe'
$refs = "$wpf\PresentationFramework.dll", "$wpf\PresentationCore.dll", "$wpf\WindowsBase.dll", "$fw\System.Xaml.dll",
    "$fw\System.Windows.Forms.dll", "$fw\System.Drawing.dll", "$fw\System.Runtime.Serialization.dll", "$fw\System.Core.dll",
    "$fw\System.IO.Compression.dll", "$fw\System.IO.Compression.FileSystem.dll", "$fw\System.Web.Extensions.dll", "$fw\System.Xml.dll", "$fw\System.Security.dll",
    "$lib\Microsoft.Web.WebView2.Core.dll", "$lib\Microsoft.Web.WebView2.Wpf.dll",
    "$wpf\UIAutomationProvider.dll", "$wpf\UIAutomationTypes.dll", "$wpf\UIAutomationClient.dll"   # 1.9: the tab strip's UI Automation peers; the tests' UIA tree walk
# 1.6: the optional UI sounds and the ambience loop are synthesised here (tools\gen-sounds.ps1 - no recorded or downloaded sounds) and embedded
$snd = Join-Path $PSScriptRoot 'obj\sounds'
& (Join-Path $PSScriptRoot 'tools\gen-sounds.ps1') -Out $snd | Out-Null
$sounds = @(Get-ChildItem $snd -Filter *.wav | ForEach-Object { "/resource:$($_.FullName),HallowBrowser.Sounds.$($_.Name)" })
if ($sounds.Count -ne 5) { throw "gen-sounds.ps1 made $($sounds.Count) sounds, not 5" }
$common = @('/nologo', '/optimize+', '/platform:x64', "/win32icon:$src\hallow.ico", "/resource:$src\Theme.xaml,HallowBrowser.Theme.xaml") + $sounds + ($refs | ForEach-Object { "/reference:$_" })
$files = Get-ChildItem $src -Filter *.cs | ForEach-Object FullName
& "$fw\csc.exe" @($common + @('/target:winexe', "/out:$out") + $files)
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
foreach ($d in 'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.Wpf.dll', 'WebView2Loader.dll') { Copy-Item "$lib\$d" $PSScriptRoot -Force }
"Built $out ($([math]::Round((Get-Item $out).Length / 1KB)) KB)"
if ($Tests) {
    $tdir = Join-Path $PSScriptRoot 'tests'
    $t = Join-Path $tdir 'BrowserTests.exe'
    & "$fw\csc.exe" @($common + @('/target:exe', "/out:$t", '/main:HallowBrowser.Tests.Run') + $files + (Get-ChildItem $tdir -Filter *.cs | ForEach-Object FullName))
    if ($LASTEXITCODE -ne 0) { throw "Test build failed" }
    foreach ($d in 'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.Wpf.dll', 'WebView2Loader.dll') { Copy-Item "$lib\$d" $tdir -Force }
    "Built $t"
}
