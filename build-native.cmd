@echo off
setlocal
cd /d "%~dp0"
where cl.exe >nul 2>nul
if errorlevel 1 call :find_tools
cd /d "%~dp0"
where cl.exe >nul 2>nul
if errorlevel 1 (
  echo A native compiler is required. Run this from an x64 Visual Studio Developer Command Prompt.
  echo Install the Desktop development with C++ workload, including a Windows SDK.
  exit /b 1
)
if not exist obj mkdir obj
if not exist bin mkdir bin
call "%~dp0build.cmd"
if errorlevel 1 exit /b 1
cl.exe /nologo /std:c++17 /O2 /MT /W4 /WX /EHsc /DNDEBUG /Foobj\checks.obj /Febin\SeamlessMice.NativeChecks.exe native\checks.cpp
if errorlevel 1 exit /b 1
bin\SeamlessMice.NativeChecks.exe
if errorlevel 1 exit /b 1
cl.exe /nologo /std:c++17 /O2 /MT /W4 /WX /EHsc /D_CRT_SECURE_NO_WARNINGS /Foobj\runtime-checks.obj /Febin\SeamlessMice.RuntimeChecks.exe native\runtime-checks.cpp /link /SUBSYSTEM:CONSOLE user32.lib shell32.lib wtsapi32.lib crypt32.lib
if errorlevel 1 exit /b 1
if not exist artifacts\runtime-checks mkdir artifacts\runtime-checks
bin\SeamlessMice.RuntimeChecks.exe "%~dp0artifacts\runtime-checks"
if errorlevel 1 exit /b 1
pushd native
rc.exe /nologo /fo ..\obj\engine.res engine.rc
if errorlevel 1 (
  popd
  exit /b 1
)
popd
cl.exe /nologo /std:c++17 /O2 /MT /W4 /WX /EHsc /D_CRT_SECURE_NO_WARNINGS /Foobj\engine.obj /Febin\SeamlessMice.Native.exe native\engine.cpp obj\engine.res /link /SUBSYSTEM:WINDOWS /MANIFEST:NO user32.lib shell32.lib wtsapi32.lib crypt32.lib
if errorlevel 1 exit /b 1
copy /y bin\SeamlessMice.exe bin\SeamlessMice.Managed.exe >nul
if errorlevel 1 exit /b 1
copy /y bin\SeamlessMice.Native.exe bin\SeamlessMice.exe >nul
if errorlevel 1 exit /b 1
echo Native engine built. The previous C# engine is bin\SeamlessMice.Managed.exe.
echo Run bin\SeamlessMice.Check.exe --engine-smoke to measure it and check its lifecycle.
exit /b 0

:find_tools
set "sm_vswhere=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%sm_vswhere%" exit /b 1
for /f "usebackq delims=" %%I in (`"%sm_vswhere%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "sm_vs_install=%%I"
if not defined sm_vs_install exit /b 1
call "%sm_vs_install%\Common7\Tools\VsDevCmd.bat" -arch=x64 -host_arch=x64 >nul 2>nul
exit /b %ERRORLEVEL%
