# Puts Hallow Shield's Chrome build into package\shield (shipped inside Hallow Browser) and .\shield (for running from the repo).
# Only from Shield's own GitHub release, and only if the zip matches the pinned sha256 (GitHub's digest for that asset).
#   .\tools\get-shield.ps1 [-Zip <already downloaded hallow-shield-chrome-1.7.zip>]
param([string]$Zip)
$ErrorActionPreference = 'Stop'
$ver = '1.7'
$sha = '8d9cf2f4f57dca0bed93e35d5a314662b1d5dde2689faeecf3422bba026005ba'
$root = Split-Path $PSScriptRoot
$tmp = Join-Path ([IO.Path]::GetTempPath()) ('hallow-shield-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $tmp | Out-Null
try {
    if (-not $Zip) { $Zip = Join-Path $tmp "hallow-shield-chrome-$ver.zip"; gh release download "v$ver" -R Dom428/hallow-shield -p "hallow-shield-chrome-$ver.zip" -D $tmp }
    $h = (Get-FileHash $Zip -Algorithm SHA256).Hash.ToLower()
    if ($h -ne $sha) { throw "Shield zip sha256 is $h, expected $sha - not used" }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    foreach ($dest in (Join-Path $root 'package\shield'), (Join-Path $root 'shield')) {
        if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
        $z = [IO.Compression.ZipFile]::OpenRead($Zip)
        try {
            foreach ($e in $z.Entries) {
                if ($e.FullName.EndsWith('/') -or $e.FullName.StartsWith('bridge/')) { continue }   # the family bridge is for desktop browsers
                $f = [IO.Path]::GetFullPath((Join-Path $dest $e.FullName))
                if (-not $f.StartsWith([IO.Path]::GetFullPath($dest) + '\')) { continue }
                New-Item -ItemType Directory -Force (Split-Path $f) | Out-Null
                [IO.Compression.ZipFileExtensions]::ExtractToFile($e, $f, $true)
            }
        } finally { $z.Dispose() }
    }
    "Hallow Shield $ver (sha256 $sha) -> package\shield and .\shield"
} finally { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }
