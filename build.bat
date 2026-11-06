@echo off
setlocal
cd /d "%~dp0"

echo ==================================================
echo   Liuguang Launcher - one click build
echo   (no .NET SDK required, uses in-box compiler)
echo ==================================================
echo.

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" goto NO_CSC

set "ICONOPT="
if exist "icon.ico" set "ICONOPT=/win32icon:icon.ico"
set "MANOPT="
if exist "app.manifest" set "MANOPT=/win32manifest:app.manifest"

set "RESOPT="
if exist "wallpaper.jpg" set "RESOPT=/resource:wallpaper.jpg,Liuguang.default_wallpaper.jpg"
if not exist "wallpaper.jpg" if exist ".workbuddy\default_wallpaper.jpg" set "RESOPT=/resource:.workbuddy\default_wallpaper.jpg,Liuguang.default_wallpaper.jpg"

echo [1/2] Compiling sources ...
del /q build_log.txt 2>nul
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /utf8output /out:"LiuguangLauncher.exe" %ICONOPT% %MANOPT% %RESOPT% /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll src\*.cs > build_log.txt 2>&1
set "RC=%ERRORLEVEL%"
type build_log.txt
if not "%RC%"=="0" goto FAILED
del /q build_log.txt 2>nul

echo [2/2] Writing output name ...
rem This step used to only print a WARN when renaming failed and then continue.
rem If the old exe was running, the new build stayed in LiuguangLauncher.exe
rem while the user kept double-clicking the stale one - looks like
rem "build succeeded but it still won't run". Now we stop loudly instead.
powershell -NoProfile -ExecutionPolicy Bypass -Command "$n = -join @([char]0x6D41,[char]0x5149,[char]0x542F,[char]0x52A8,[char]0x5668) + '.exe'; $src = 'LiuguangLauncher.exe'; $locked = $false; if (Test-Path $n) { try { Remove-Item $n -Force -ErrorAction Stop } catch { $locked = $true } }; if ($locked) { Write-Host ('  !! CANNOT replace ' + $n + ' - it is still running.'); Write-Host '  !! Close the launcher window, then run build.bat again.'; Write-Host ('  !! The new build is already here: ' + $src); exit 2 }; Move-Item -Force $src $n; Write-Host ('  OK -> ' + $n + '   built at ' + (Get-Item $n).LastWriteTime.ToString('HH:mm:ss'))"
set "RC2=%ERRORLEVEL%"
if not "%RC2%"=="0" goto RENAME_FAILED

echo.
echo Done. Run the exe in this folder (check its timestamp is just now).
echo.
pause
exit /b 0

:RENAME_FAILED
echo.
echo Build succeeded, but the output could not be renamed.
echo The launcher is probably still running - close it and run build.bat again.
echo Or just run LiuguangLauncher.exe directly, it is the new build.
echo.
pause
exit /b 1

:FAILED
echo.
echo Build FAILED. Full log saved to build_log.txt
echo NOTE: the exe in this folder is still the OLD build. Do not test it.
echo Please send that file back.
echo.
pause
exit /b 1

:NO_CSC
echo.
echo Cannot find csc.exe. Windows 10/11 normally ships it with .NET Framework 4.x.
echo Install .NET Framework 4.8 and run this again.
echo.
pause
exit /b 1
