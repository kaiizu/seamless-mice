param([switch]$SkipTests)
# Builds the managed editor, shared core, test harnesses and the managed baseline engine.
# Run build-native.cmd to build the shipped native engine and place it in bin\SeamlessMice.exe.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The Windows .NET Framework 4.x C# compiler was not found.' }
$output = Join-Path $root 'bin'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$core = @('src\Geometry.cs','src\Native.cs','src\Displays.cs','src\Config.cs') | ForEach-Object { Join-Path $root $_ }
function Compile([string[]]$CompilerArgs) {
    & $compiler /nologo /optimize+ /warn:4 /warnaserror+ /platform:x64 @CompilerArgs "$root\src\AssemblyInfo.cs"
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed with exit code $LASTEXITCODE" }
}
Compile @('/target:exe', "/out:$output\SeamlessMice.DrawAssets.exe", '/reference:System.Drawing.dll', "$root\tools\DrawAssets.cs")
& "$output\SeamlessMice.DrawAssets.exe" "$root\assets"
if ($LASTEXITCODE -ne 0) { throw 'Asset generation failed.' }
Compile (@('/target:library', "/out:$output\SeamlessMice.Core.dll") + $core)
Compile @('/target:winexe', '/unsafe+', "/win32icon:$root\assets\app.ico", "/win32manifest:$root\src\app.manifest", "/out:$output\SeamlessMice.exe", "/reference:$output\SeamlessMice.Core.dll", "$root\src\Engine.cs", "$root\src\Native.cs")
Compile @('/target:winexe', "/win32icon:$root\assets\app.ico", "/resource:$root\assets\logo.png,SeamlessMice.Logo.png", "/win32manifest:$root\src\app.manifest", "/out:$output\SeamlessMice.Settings.exe", "/reference:$output\SeamlessMice.Core.dll", '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', "$root\src\Settings.cs", "$root\src\Theme.cs", "$root\src\NumericField.cs", "$root\src\ScreenPicker.cs", "$root\src\LayoutCanvas.cs", "$root\src\EditorChecks.cs", "$root\src\Native.cs")
Compile @('/target:exe', "/out:$output\SeamlessMice.Check.exe", "/reference:$output\SeamlessMice.Core.dll", "$root\tests\Checks.cs", "$root\src\Native.cs")
Compile @('/target:exe', "/out:$output\SeamlessMice.Observe.exe", '/reference:System.Web.Extensions.dll', "$root\tests\Observe.cs")
if (-not $SkipTests) {
    & "$output\SeamlessMice.Check.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Checks failed.' }
}
Get-ChildItem -LiteralPath $output -File | Where-Object { $_.Extension -in '.exe','.dll' } | Select-Object Name,Length
