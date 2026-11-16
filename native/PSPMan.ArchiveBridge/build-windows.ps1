param(
    [Parameter(Mandatory=$true)][string]$SourceRoot,
    [string]$OutputDirectory = "$PSScriptRoot/out",
    [ValidateSet('x64','x86','arm64')][string]$Platform = 'x64'
)
$ErrorActionPreference = 'Stop'
# Run from the Visual Studio developer shell for the chosen target architecture.
$SourceRoot = (Resolve-Path $SourceRoot).Path
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$OutputDirectory = (Resolve-Path $OutputDirectory).Path
$bundle = Join-Path $SourceRoot 'CPP/7zip/Bundles/Format7zF'
Push-Location $bundle
try {
    & nmake /f makefile "PLATFORM=$Platform"
    if ($LASTEXITCODE -ne 0) { throw '7-Zip native engine build failed.' }
    Copy-Item (Join-Path $bundle "$Platform/7z.dll") (Join-Path $OutputDirectory '7z.dll')
} finally { Pop-Location }
Push-Location $OutputDirectory
try {
    & cl /nologo /std:c++17 /EHsc /MD /LD /W4 "/I$SourceRoot" `
        "$PSScriptRoot/bridge.cpp" /link oleaut32.lib uuid.lib "/DEF:$PSScriptRoot/bridge.def" /OUT:pspman_archive_bridge.dll
    if ($LASTEXITCODE -ne 0) { throw 'PSPMan native bridge build failed.' }
    Copy-Item "$SourceRoot/DOC/License.txt" LICENSE.7zip.txt
    Copy-Item "$SourceRoot/DOC/copying.txt" COPYING.LGPL.txt
    Copy-Item "$SourceRoot/DOC/unRarLicense.txt" LICENSE.unRAR.txt
} finally { Pop-Location }
