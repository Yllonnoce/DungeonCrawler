@echo off
setlocal
rem DungeonCrawler setup for the current user (no administrator rights needed).
rem   Setup.bat                menu: Install / Uninstall
rem   Setup.bat install        install (asks about a Desktop shortcut)
rem   Setup.bat uninstall      remove the install and its shortcuts
rem Install copies this folder to %LOCALAPPDATA%\Programs\DungeonCrawler and adds a Start Menu shortcut, plus a Desktop
rem shortcut if you want one. Saves and settings stay in %APPDATA%\Godot\app_userdata\DungeonCrawler.
set "HERE=%~dp0"
set "DEST=%LOCALAPPDATA%\Programs\DungeonCrawler"
set "STARTMENU=%APPDATA%\Microsoft\Windows\Start Menu\Programs\DungeonCrawler.lnk"
set "DESKTOP=%USERPROFILE%\Desktop\DungeonCrawler.lnk"

if /i "%~1"=="install" goto install
if /i "%~1"=="uninstall" goto uninstall
if not "%~1"=="" echo usage: Setup.bat [install ^| uninstall] & exit /b 2

echo DungeonCrawler setup
if exist "%DEST%\DungeonCrawler.exe" echo   (an install exists at %DEST%)
echo   1^) Install
echo   2^) Uninstall
echo   3^) Quit
choice /c 123 /n /m "Choose [1-3]: "
if errorlevel 3 echo Nothing done.& exit /b 0
if errorlevel 2 goto uninstall
goto install

:install
echo Installing to %DEST% ...
if not exist "%DEST%" mkdir "%DEST%"
robocopy "%HERE%." "%DEST%" /E /NFL /NDL /NJH /NJS /NP >nul
if errorlevel 8 echo Copy failed.& exit /b 1
call :shortcut "%STARTMENU%"
choice /c YN /n /m "Put a DungeonCrawler shortcut on the Desktop? [Y/N] "
if errorlevel 2 goto installed
call :shortcut "%DESKTOP%"
echo Desktop shortcut: %DESKTOP%
:installed
echo Installed. Start it from the Start Menu (DungeonCrawler) or run "%DEST%\DungeonCrawler.exe".
echo To remove it later run "%DEST%\Setup.bat" uninstall
echo SmartScreen may warn the first time because the build is unsigned: "More info" then "Run anyway".
exit /b 0

:uninstall
del /q "%STARTMENU%" 2>nul
del /q "%DESKTOP%" 2>nul
if /i "%HERE:~0,-1%"=="%DEST%" (
  rem running from inside the install folder: delete it after this script ends
  start "" /min cmd /c "ping -n 2 127.0.0.1 >nul & rmdir /s /q "%DEST%""
) else (
  rmdir /s /q "%DEST%" 2>nul
)
echo Removed DungeonCrawler. Saves and settings stay in %%APPDATA%%\Godot\app_userdata\DungeonCrawler; delete that folder too if you want them gone.
exit /b 0

:shortcut
powershell -NoProfile -ExecutionPolicy Bypass -Command "$s=(New-Object -ComObject WScript.Shell).CreateShortcut('%~1'); $s.TargetPath='%DEST%\DungeonCrawler.exe'; $s.WorkingDirectory='%DEST%'; $s.Description='A first-person party dungeon crawler'; $s.Save()"
exit /b 0
