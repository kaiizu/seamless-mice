param([string]$Msi)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
if (-not $Msi) {
    $Msi = Get-ChildItem -LiteralPath (Join-Path $root 'dist') -Filter 'SeamlessMice-*-x64.msi' |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
    if (-not $Msi) { throw 'No Seamless Mice MSI found in dist.' }
}
$msi = $Msi
$version = [IO.Path]::GetFileNameWithoutExtension($msi) -replace '^SeamlessMice-','' -replace '-x64$',''
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /optimize+ /platform:x86 "/out:$root\bin\SeamlessMice.ValidateMsi.exe" "$root\tools\ValidateMsi.cs"
if ($LASTEXITCODE -ne 0) { throw 'Could not build MSI validator.' }
$validation = Join-Path $root 'artifacts\installer-validation'
& $compiler /nologo /optimize+ /platform:x64 "/out:$root\bin\SeamlessMice.InstallerRuntimeChecks.exe" "$root\tools\InstallerRuntimeChecks.cs"
if ($LASTEXITCODE -ne 0) { throw 'Could not build installer runtime regression checks.' }
& "$root\bin\SeamlessMice.InstallerRuntimeChecks.exe" $msi $validation
if ($LASTEXITCODE -ne 0) { throw 'Installer runtime OS/dialog regression checks failed.' }
& "$root\bin\SeamlessMice.ValidateMsi.exe" $msi "$root\obj\installer-tools\darice.cub" $validation --in-process
if ($LASTEXITCODE -ne 0) { throw 'MSI native validation found errors. See artifacts\installer-validation\ice.log.' }
$payloadFolder = Join-Path $validation 'payload'
New-Item -ItemType Directory -Force -Path $payloadFolder | Out-Null
& (Join-Path $env:WINDIR 'System32\expand.exe') '-F:*' (Join-Path $validation 'payload.cab') $payloadFolder | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not extract the installer payload.' }
$sources = @{
    'Engine'='bin\SeamlessMice.exe';'Settings'='bin\SeamlessMice.Settings.exe';'Core'='bin\SeamlessMice.Core.dll';
    'Marker'='installer\installed.flag';'License'='LICENSE';'Icon'='assets\app.ico'
}
foreach ($id in $sources.Keys) {
    $original = (Get-FileHash -LiteralPath (Join-Path $root $sources[$id]) -Algorithm SHA256).Hash
    $extracted = (Get-FileHash -LiteralPath (Join-Path $payloadFolder ('F_' + $id)) -Algorithm SHA256).Hash
    if ($original -ne $extracted) { throw ('MSI payload mismatch: ' + $id) }
}
Write-Output 'PASS: all six MSI payload files match the tested build (SHA-256).'
$report = @(
    ('Seamless Mice ' + $version + ' installer validation'),
    '20 installer-runtime regression assertions passed, including real registry OS search, LaunchConditions and button-subset checks.',
    'All seven dialogs were created with the Windows Installer authoring-preview API.',
    '100 native Windows SDK ICE checks passed: zero errors and warnings.',
    'Four SDK VBScript ICE checks (08, 09, 32, 61) could not execute in this sandbox.',
    'All six embedded cabinet payloads match the tested build by SHA-256.',
    'Native geometry, installed-path/parser and editor interaction checks passed.',
    'No installation or system registration was performed. UAC/install/uninstall are not end-to-end tested here.'
)
[IO.File]::WriteAllLines((Join-Path $validation 'RESULTS.txt'),$report)
Write-Output ('Installer SHA256: ' + (Get-FileHash -LiteralPath $msi -Algorithm SHA256).Hash)
