param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = $PSScriptRoot
$bin = Join-Path $root 'bin'
$version = '0.2.3'
$engine = Join-Path $bin 'SeamlessMice.exe'
$native = Join-Path $bin 'SeamlessMice.Native.exe'
if (-not (Test-Path -LiteralPath $native)) { throw 'Run build-native.cmd before packaging.' }
if ((Get-FileHash -LiteralPath $engine).Hash -ne (Get-FileHash -LiteralPath $native).Hash) {
    throw 'SeamlessMice.exe is not the native build. Run build-native.cmd again before packaging.'
}
$destination = Join-Path $root ('dist\SeamlessMice-' + $version)
New-Item -ItemType Directory -Force -Path $destination | Out-Null
$files = @('SeamlessMice.exe','SeamlessMice.Settings.exe','SeamlessMice.Core.dll')
foreach ($name in $files) {
    $source = Join-Path $bin $name
    $target = Join-Path $destination $name
    # Leave identical binaries in place, including a native engine already running from dist.
    if ((Test-Path -LiteralPath $target) -and (Get-FileHash -LiteralPath $source).Hash -eq (Get-FileHash -LiteralPath $target).Hash) { continue }
    Copy-Item -LiteralPath $source -Destination $target -Force
}
foreach ($name in @('LICENSE','README.md','PERFORMANCE.md')) { Copy-Item -LiteralPath (Join-Path $root $name) -Destination $destination -Force }
$archive = Join-Path $root ('dist\SeamlessMice-' + $version + '-windows-x64.zip')
# Include only explicitly selected files, never a user's calibration or previous configuration.
$contents = @($files + @('LICENSE','README.md','PERFORMANCE.md')) | ForEach-Object { Join-Path $destination $_ }
Compress-Archive -LiteralPath $contents -DestinationPath $archive -Force
Get-Item -LiteralPath $archive | Select-Object FullName,Length
Get-FileHash -LiteralPath $archive -Algorithm SHA256 | Select-Object Algorithm,Hash
