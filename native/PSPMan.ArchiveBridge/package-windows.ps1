param(
    [Parameter(Mandatory=$true)][string]$SourceArchive,
    [ValidateSet('x64','x86','arm64')][string]$Platform = 'x64'
)
$ErrorActionPreference = 'Stop'
$expected = 'a4e374c8f864f51df48c2cfd0e1413ec3b2ea030deae852d6a655402ebb61ac4'
if ((Get-FileHash $SourceArchive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'Source archive integrity mismatch.' }
$rid = "win-$Platform"
$package = Join-Path $PSScriptRoot "artifacts/runtimes/$rid/native"
New-Item -ItemType Directory -Force "$package/bridge-source" | Out-Null
Copy-Item "$PSScriptRoot/out/7z.dll", "$PSScriptRoot/out/pspman_archive_bridge.dll" $package
Copy-Item $SourceArchive "$package/7z2606-src.tar.xz"
Copy-Item "$PSScriptRoot/licenses/*.txt" $package
Copy-Item "$PSScriptRoot/../../LICENSE" "$package/LICENSE.PSPMan.txt"
$bridgeSources = @('bridge.cpp','bridge.h','bridge.def','build-linux.sh','build-windows.ps1','README.md','source.lock.json','ArchiveRuntime.targets','package-linux.sh','package-windows.ps1')
foreach ($name in $bridgeSources) { Copy-Item "$PSScriptRoot/$name" "$package/bridge-source/$name" }
$names = @('7z.dll','pspman_archive_bridge.dll','7z2606-src.tar.xz','LICENSE.7zip.txt','COPYING.LGPL.txt','LICENSE.unRAR.txt','LICENSE.PSPMan.txt')
$names += $bridgeSources | ForEach-Object { "bridge-source/$_" }
$files = [ordered]@{}
foreach ($name in $names) { $files[$name] = (Get-FileHash "$package/$name" -Algorithm SHA256).Hash.ToLowerInvariant() }
$manifest = [ordered]@{ Abi=1; Version=2606; Rid=$rid; SourceSha256=$expected; Files=$files }
[IO.File]::WriteAllText("$package/runtime.json", ($manifest | ConvertTo-Json -Depth 4), [Text.UTF8Encoding]::new($false))
Write-Output "Packaged $rid"
