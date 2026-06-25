@echo off
setlocal enabledelayedexpansion

echo ==========================================
echo    ZPackGUI -- Build Script
echo ==========================================
echo.

set "CSC="
set "FB=C:\Windows\Microsoft.NET\Framework64"
for %%V in (4.8 4.7.2 4.7.1 4.7 4.6.2 4.6.1 4.6 4.5.2 4.5.1 4.5 4.0) do (
    if "!CSC!"=="" for /d %%D in ("%FB%\v%%V*") do (
        if exist "%%D\csc.exe" set "CSC=%%D\csc.exe"
    )
)
if "!CSC!"=="" (
    set "FB=C:\Windows\Microsoft.NET\Framework"
    for %%V in (4.8 4.7.2 4.7.1 4.7 4.6.2 4.6.1 4.6 4.5.2 4.5.1 4.5 4.0) do (
        if "!CSC!"=="" for /d %%D in ("%FB%\v%%V*") do (
            if exist "%%D\csc.exe" set "CSC=%%D\csc.exe"
        )
    )
)
if "!CSC!"=="" (
    echo [ERROR] Khong tim thay csc.exe
    pause & exit /b 1
)
echo [INFO] Compiler: !CSC!

if not exist "%~dp0ZPackGUI.cs"   ( echo [ERROR] Thieu ZPackGUI.cs   & pause & exit /b 1 )
if not exist "%~dp0ZPackTool.cs"  ( echo [ERROR] Thieu ZPackTool.cs  & pause & exit /b 1 )
if not exist "%~dp0UclNative.cs"  ( echo [ERROR] Thieu UclNative.cs  & pause & exit /b 1 )

echo [INFO] Dang bien dich...

"!CSC!" ^
  /target:winexe ^
  /optimize+ ^
  /platform:x86 ^
  /main:GuiProgram ^
  /out:"%~dp0ZPackGUI.exe" ^
  /r:System.Windows.Forms.dll ^
  /r:System.Drawing.dll ^
  "%~dp0ZPackGUI.cs" "%~dp0ZPackTool.cs" "%~dp0UclNative.cs"

if errorlevel 1 (
    echo.
    echo [ERROR] Bien dich that bai!
    pause & exit /b 1
)

echo.
echo [OK] Build xong: ZPackGUI.exe
echo Nho dat ucl.dll cung thu muc voi ZPackGUI.exe truoc khi chay.
echo.
pause
endlocal
