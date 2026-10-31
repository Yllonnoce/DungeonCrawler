@echo off
setlocal EnableExtensions
title DungeonCrawler
rem DungeonCrawler launcher. The Start Menu and Desktop shortcuts open this: a two-way boot menu, Play or Debug.
rem   Play   asks GitHub whether a newer release of the game is out (a second or two; nothing about you is sent), offers
rem          to install it, then starts the game. When GitHub cannot be reached, the game simply starts.
rem   Debug  starts the game with Godot's --verbose and any extra flags you give (src/Core/DebugTools.cs lists them:
rem          --floor=3, --seed=1, --face-enemy, --screenshot=PATH, --quit-after=SECONDS ...) and keeps a console with the
rem          game's live output: the console wrapper DungeonCrawler.console.exe when the build ships one, otherwise the
rem          Godot log followed here as it grows.
rem   Launch.bat                menu: Play / Debug / Quit (Play after 10 seconds)
rem   Launch.bat play           play, with the release check, no menu
rem   Launch.bat debug [flags]  debug, no menu, no release check:   Launch.bat debug --floor=3 --seed=1
rem   Launch.bat update         check for a newer release and install it; do not start the game
rem Only the installed copy (%LOCALAPPDATA%\Programs\DungeonCrawler) is checked for releases; a copy built from source by
rem build.bat is left to update.bat. The VERSION file beside this script names the build, as does each release archive.
rem DUNGEONCRAWLER_REPO, DUNGEONCRAWLER_TAG and DUNGEONCRAWLER_API point the check elsewhere (a fork, one release, a test).
set "HERE=%~dp0"
set "GAME=%HERE%DungeonCrawler.exe"
set "CONSOLE=%HERE%DungeonCrawler.console.exe"
set "LOG=%APPDATA%\Godot\app_userdata\DungeonCrawler\logs\godot.log"
set "DEST=%LOCALAPPDATA%\Programs\DungeonCrawler"
set "REPO=Yllonnoce/DungeonCrawler"
if not "%DUNGEONCRAWLER_REPO%"=="" set "REPO=%DUNGEONCRAWLER_REPO%"
set "API=https://api.github.com/repos/%REPO%/releases/latest"
if not "%DUNGEONCRAWLER_TAG%"=="" set "API=https://api.github.com/repos/%REPO%/releases/tags/%DUNGEONCRAWLER_TAG%"
if not "%DUNGEONCRAWLER_API%"=="" set "API=%DUNGEONCRAWLER_API%"
set "PAGE=https://github.com/%REPO%/releases"
set "WORK=%TEMP%\DungeonCrawlerUpdate"
set "MODE=%~1"
set "FLAGS="

if not exist "%GAME%" (echo DungeonCrawler.exe is not next to this script.& pause & exit /b 1)
if /i "%MODE%"=="play" goto check
if /i "%MODE%"=="update" goto check
if /i "%MODE%"=="debug" goto debug_args
if not "%MODE%"=="" (echo usage: Launch.bat [play ^| debug [flags] ^| update]& exit /b 2)

rem ---- the boot menu
echo DungeonCrawler
echo   1^) Play    ^(checks for a newer release first^)
echo   2^) Debug   ^(a console with the game's output; extra flags^)
echo   3^) Quit
choice /c 123 /n /t 10 /d 1 /m "Choose [1-3] (Play in 10 seconds): "
if errorlevel 3 exit /b 0
if errorlevel 2 goto debug_ask
set "MODE=play"
goto check

:debug_ask
set /p FLAGS=Extra flags, Enter for none (for example --floor=3 --seed=1):
goto debug

:debug_args
rem everything after the word "debug", with its = signs intact (%1 %2 ... would split --floor=3 in two)
set "FLAGS=%*"
set "FLAGS=%FLAGS:*debug=%"

:debug
echo.
if exist "%CONSOLE%" goto debug_console
echo Debug: DungeonCrawler.exe --verbose -- %FLAGS%
echo This build has no console wrapper, so the Godot log is followed here while the game runs:
echo   %LOG%
start "" /d "%HERE%" "%GAME%" --verbose -- %FLAGS%
powershell -NoProfile -ExecutionPolicy Bypass -Command "$log='%LOG%'; Start-Sleep -Milliseconds 1500; $pos=0; do { if (Test-Path $log) { $s=[IO.File]::Open($log,'Open','Read','ReadWrite'); if ($s.Length -lt $pos) { $pos=0 }; $null=$s.Seek($pos,'Begin'); $r=New-Object IO.StreamReader($s); while (($l=$r.ReadLine()) -ne $null) { $l }; $pos=$s.Position; $r.Close() }; $alive=[bool](Get-Process DungeonCrawler -ErrorAction SilentlyContinue); Start-Sleep -Milliseconds 300 } while ($alive)"
echo.
echo The game has ended. The whole log is %LOG%
pause
exit /b 0

:debug_console
echo Debug: DungeonCrawler.console.exe --verbose -- %FLAGS%
echo The game's output follows; this window stays open when the game ends.
"%CONSOLE%" --verbose -- %FLAGS%
echo.
echo The game ended with code %errorlevel%.
pause
exit /b 0

:check
rem ---- which build is this, and is it the installed copy?
set "VERSION="
set "SOURCE="
if exist "%HERE%VERSION" for /f "usebackq tokens=1,2" %%a in ("%HERE%VERSION") do (set "VERSION=%%a" & set "SOURCE=%%b")
if /i not "%HERE:~0,-1%"=="%DEST%" (
  echo Not installed yet: Setup.bat installs the game, and the installed copy checks for new releases.
  goto done_check
)
if /i "%SOURCE%"=="source" (
  echo Built from source ^(%VERSION%^): update.bat keeps it current.
  goto done_check
)

rem ---- ask GitHub for the newest release and compare
where curl >nul 2>&1 || (echo No curl on this PC, so no update check.& goto done_check)
where tar >nul 2>&1 || (echo No tar on this PC, so no update check.& goto done_check)
if not exist "%WORK%" mkdir "%WORK%"
set "JSON=%WORK%\release.json"
echo Checking for a newer DungeonCrawler ...
curl -fsSL --max-time 8 -H "Accept: application/vnd.github+json" -H "User-Agent: DungeonCrawler-launcher" -o "%JSON%" "%API%" || (echo GitHub could not be reached. Playing the installed version.& goto done_check)
set "URL="
for /f "usebackq tokens=1,* delims=:" %%a in (`findstr /c:"browser_download_url" "%JSON%" ^| findstr /c:"windows-x86_64.zip"`) do if not defined URL set "URL=%%b"
if not defined URL (echo No game release is published yet.& goto done_check)
set "URL=%URL:"=%"
set "URL=%URL:,=%"
set "URL=%URL: =%"
set "FILE=%URL:*/releases/download/=%"
for /f "tokens=1,* delims=/" %%a in ("%FILE%") do set "FILE=%%b"
rem the archive is named DungeonCrawler-<version>-windows-x86_64.zip
set "LATEST=%FILE:DungeonCrawler-=%"
set "LATEST=%LATEST:-windows-x86_64.zip=%"
if /i "%LATEST%"=="%VERSION%" (echo Up to date ^(%VERSION%^).& goto done_check)
if "%VERSION%"=="" (echo A release is out: %LATEST%. This copy carries no version mark, so it may be older.) else (echo A newer DungeonCrawler is out: %LATEST% ^(this is %VERSION%^).)
if /i "%MODE%"=="update" goto update
choice /c YN /n /t 30 /d N /m "Install it now? [Y/N] (plays the current one in 30 seconds) "
if errorlevel 2 goto done_check

:update
set "SUMS="
for /f "usebackq tokens=1,* delims=:" %%a in (`findstr /c:"browser_download_url" "%JSON%" ^| findstr /c:"/SHA256SUMS"`) do if not defined SUMS set "SUMS=%%b"
if defined SUMS set "SUMS=%SUMS:"=%"
if defined SUMS set "SUMS=%SUMS:,=%"
if defined SUMS set "SUMS=%SUMS: =%"
echo Downloading %FILE% ^(about 500 MB^) ...
curl -fL -# -o "%WORK%\%FILE%.part" "%URL%" || (echo The download failed. Playing the installed version; the release is at %PAGE% & goto done_check)
move /y "%WORK%\%FILE%.part" "%WORK%\%FILE%" >nul
if not defined SUMS goto unpack
curl -fsSL -o "%WORK%\SHA256SUMS" "%SUMS%" || goto unpack
set "EXPECTED="
for /f "usebackq tokens=1" %%a in (`findstr /i /c:"%FILE%" "%WORK%\SHA256SUMS"`) do set "EXPECTED=%%a"
set "ACTUAL="
for /f "usebackq skip=1 tokens=1" %%a in (`certutil -hashfile "%WORK%\%FILE%" SHA256`) do if not defined ACTUAL set "ACTUAL=%%a"
if not defined EXPECTED goto unpack
if /i not "%EXPECTED%"=="%ACTUAL%" (del "%WORK%\%FILE%" & echo The download is damaged ^(checksum mismatch^). Playing the installed version.& goto done_check)
echo Checksum OK.

:unpack
echo Unpacking ...
if exist "%WORK%\DungeonCrawler" rmdir /s /q "%WORK%\DungeonCrawler"
mkdir "%WORK%\DungeonCrawler"
tar -xf "%WORK%\%FILE%" -C "%WORK%\DungeonCrawler" || (echo Unpacking failed. Playing the installed version.& goto done_check)
del "%WORK%\%FILE%"
set "SETUP=%WORK%\DungeonCrawler\Setup.bat"
if not exist "%SETUP%" (echo Setup.bat is missing from the archive.& goto done_check)
rem keep the Desktop shortcut if there is one, do not add one if there is not
set "DESKTOP_OPT=--no-desktop"
if exist "%USERPROFILE%\Desktop\DungeonCrawler.lnk" set "DESKTOP_OPT=--desktop"
rem The new Setup.bat overwrites this very script, so the install runs in a window of its own once this one has closed.
echo Installing %LATEST% ...
if /i "%MODE%"=="update" (
  start "DungeonCrawler update" cmd /c "ping -n 2 127.0.0.1 >nul & call "%SETUP%" install %DESKTOP_OPT% & rmdir /s /q "%WORK%" & echo. & echo This window closes in 15 seconds. & timeout /t 15 >nul"
) else (
  start "DungeonCrawler update" cmd /c "ping -n 2 127.0.0.1 >nul & call "%SETUP%" install %DESKTOP_OPT% & rmdir /s /q "%WORK%" & start "" /d "%DEST%" "%DEST%\DungeonCrawler.exe""
)
exit /b 0

:done_check
if /i "%MODE%"=="update" exit /b 0
:play
start "" /d "%HERE%" "%GAME%"
exit /b 0
