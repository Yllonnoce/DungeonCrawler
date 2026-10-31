@echo off
setlocal EnableExtensions
rem Get DungeonCrawler onto this Windows PC with one double-click. No git, no build tools, no GitHub account.
rem
rem   Download this file (get.bat) and double-click it. Or in a Command Prompt:
rem     curl -fsSL -o get.bat https://raw.githubusercontent.com/Yllonnoce/DungeonCrawler/main/get.bat && get.bat
rem
rem It finds the newest game release on GitHub, downloads the Windows archive into Downloads\DungeonCrawler, checks it
rem against the release's SHA256SUMS, unpacks it and runs Setup.bat inside, which offers Install or Uninstall and can
rem put a shortcut on the Desktop. Run it again later to install the newest release over the old one.
rem SmartScreen may warn when the game first starts because the build is unsigned: "More info", then "Run anyway".
rem   get.bat install        install without the menu       get.bat uninstall
rem Needs Windows 10 (version 1803 or newer) or Windows 11, which carry curl and tar.
set "REPO=Yllonnoce/DungeonCrawler"
if not "%DUNGEONCRAWLER_REPO%"=="" set "REPO=%DUNGEONCRAWLER_REPO%"
set "API=https://api.github.com/repos/%REPO%/releases/latest"
if not "%DUNGEONCRAWLER_TAG%"=="" set "API=https://api.github.com/repos/%REPO%/releases/tags/%DUNGEONCRAWLER_TAG%"
set "PAGE=https://github.com/%REPO%/releases"
set "DIR=%USERPROFILE%\Downloads\DungeonCrawler"
if not exist "%USERPROFILE%\Downloads" set "DIR=%USERPROFILE%\DungeonCrawler-download"
set "JSON=%TEMP%\dungeoncrawler-release.json"

where curl >nul 2>&1 || (echo This needs the curl command, which Windows 10 version 1803 and newer carry. & goto fail)
where tar >nul 2>&1 || (echo This needs the tar command, which Windows 10 version 1803 and newer carry. & goto fail)

echo Looking up the newest DungeonCrawler release ...
curl -fsSL -H "Accept: application/vnd.github+json" -H "User-Agent: DungeonCrawler-get" -o "%JSON%" "%API%" || (echo Could not reach GitHub. Are you online? You can also download by hand from %PAGE% & goto fail)
set "URL="
for /f "usebackq tokens=1,* delims=:" %%a in (`findstr /c:"browser_download_url" "%JSON%" ^| findstr /c:"windows-x86_64.zip"`) do if not defined URL set "URL=%%b"
if not defined URL (echo No game release has been published yet ^(only server builds^). Check %PAGE% later. & goto fail)
set "URL=%URL:"=%"
set "URL=%URL:,=%"
set "URL=%URL: =%"
set "SUMS="
for /f "usebackq tokens=1,* delims=:" %%a in (`findstr /c:"browser_download_url" "%JSON%" ^| findstr /c:"/SHA256SUMS"`) do if not defined SUMS set "SUMS=%%b"
if defined SUMS set "SUMS=%SUMS:"=%"
if defined SUMS set "SUMS=%SUMS:,=%"
if defined SUMS set "SUMS=%SUMS: =%"
set "FILE=%URL:*/releases/download/=%"
for /f "tokens=1,* delims=/" %%a in ("%FILE%") do set "FILE=%%b"

if not exist "%DIR%" mkdir "%DIR%"
cd /d "%DIR%"
echo Downloading %FILE% (about 500 MB) to %DIR% ...
curl -fL -# -o "%FILE%.part" "%URL%" || (echo The download failed. Try again, or download by hand from %PAGE% & goto fail)
move /y "%FILE%.part" "%FILE%" >nul

if not defined SUMS goto unpack
curl -fsSL -o SHA256SUMS "%SUMS%" || goto unpack
set "EXPECTED="
for /f "usebackq tokens=1" %%a in (`findstr /i /c:"%FILE%" SHA256SUMS`) do set "EXPECTED=%%a"
set "ACTUAL="
for /f "usebackq skip=1 tokens=1" %%a in (`certutil -hashfile "%FILE%" SHA256`) do if not defined ACTUAL set "ACTUAL=%%a"
if not defined EXPECTED goto unpack
if /i not "%EXPECTED%"=="%ACTUAL%" (del "%FILE%" & echo The downloaded file is damaged ^(checksum mismatch^). Run this again. & goto fail)
echo Checksum OK.

:unpack
echo Unpacking ...
if exist DungeonCrawler rmdir /s /q DungeonCrawler
mkdir DungeonCrawler
tar -xf "%FILE%" -C DungeonCrawler || (echo Unpacking failed. & goto fail)
if not exist DungeonCrawler\Setup.bat (echo Setup.bat was not inside the archive; open %DIR%\DungeonCrawler and run DungeonCrawler.exe. & goto fail)
echo Starting the setup program (%DIR%\DungeonCrawler\Setup.bat) ...
echo.
call DungeonCrawler\Setup.bat %*
goto end

:fail
echo.
echo Nothing was installed.
if "%~1"=="" pause
exit /b 1

:end
echo.
if "%~1"=="" pause
exit /b 0
