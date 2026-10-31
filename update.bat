@echo off
setlocal EnableExtensions
rem Update DungeonCrawler to the newest code: pull the repository, rebuild and reinstall over the current install,
rem keeping your choice about the Desktop shortcut. Double-click it inside your copy of the repository, or anywhere when
rem the copy is %USERPROFILE%\DungeonCrawler (where build.bat puts it).
rem Those who installed a downloaded release update with get.bat instead.
set "TOOLS=%LOCALAPPDATA%\DungeonCrawlerTools"
if exist "%~dp0DungeonCrawler.csproj" (
  cd /d "%~dp0"
) else (
  set "SRC=%USERPROFILE%\DungeonCrawler"
  if not "%DUNGEONCRAWLER_SRC%"=="" set "SRC=%DUNGEONCRAWLER_SRC%"
  if not exist "%SRC%\DungeonCrawler.csproj" (echo No copy of the repository here or in %USERPROFILE%\DungeonCrawler. Run build.bat first, or: git clone https://github.com/Yllonnoce/DungeonCrawler.git & goto fail)
  cd /d "%SRC%"
)
if not exist ".git" (echo %CD% is not a git clone, so it cannot be updated. Clone it: git clone https://github.com/Yllonnoce/DungeonCrawler.git & goto fail)
where git >nul 2>&1 || if exist "%TOOLS%\git\cmd\git.exe" set "PATH=%TOOLS%\git\cmd;%PATH%"
where git >nul 2>&1 || (echo git is not on this PC. Run build.bat once; it fetches a portable git. & goto fail)
echo == Pulling the newest code into %CD%
for /f %%h in ('git rev-parse --short HEAD') do set "BEFORE=%%h"
git pull --ff-only || (echo The pull failed. If you changed files in this folder, put them back: git checkout -- . & goto fail)
for /f %%h in ('git rev-parse --short HEAD') do set "AFTER=%%h"
if "%BEFORE%"=="%AFTER%" (echo Already up to date ^(%AFTER%^); rebuilding anyway.) else (echo Updated %BEFORE% -^> %AFTER%)
rem keep the Desktop shortcut if there is one, do not add one if there is not
set "DESKTOP_OPT=--no-desktop"
if exist "%USERPROFILE%\Desktop\DungeonCrawler.lnk" set "DESKTOP_OPT=--desktop"
call build.bat install %DESKTOP_OPT%
exit /b %errorlevel%

:fail
echo.
pause
exit /b 1
