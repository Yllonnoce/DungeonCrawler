@echo off
setlocal EnableExtensions
rem Build DungeonCrawler from its source code and install it, with one double-click. Made for players, not only
rem programmers: it fetches the tools it needs (Godot 4.7.2 .NET, its export templates, the .NET 8 SDK) into your own
rem user folder, so it needs no administrator rights, builds the game for this PC, then runs Setup.bat (Install /
rem Uninstall menu, optional Desktop shortcut).
rem
rem   From a copy of the repository:  double-click build.bat
rem   From anywhere, without git:     download build.bat alone and double-click it
rem       (puts the source in %USERPROFILE%\DungeonCrawler first; run it again later to update and rebuild)
rem
rem Needs Windows 10 (version 1803 or newer) or Windows 11, which carry curl and tar. The first run downloads about
rem 1.3 GB of tools and takes 20-40 minutes; later runs skip what is already in place. About 6 GB of disk in all.
rem Words after the command go to the setup program:  build.bat install      build.bat uninstall
set "REPO=Yllonnoce/DungeonCrawler"
if not "%DUNGEONCRAWLER_REPO%"=="" set "REPO=%DUNGEONCRAWLER_REPO%"
set "GODOT_VERSION=4.7.2"
set "GODOT_BUILDS=https://github.com/godotengine/godot-builds/releases/download/%GODOT_VERSION%-stable"
set "TOOLS=%LOCALAPPDATA%\DungeonCrawlerTools"
set "GODOT_DIR=%TOOLS%\Godot_v%GODOT_VERSION%-stable_mono_win64"
set "GODOT=%GODOT_DIR%\Godot_v%GODOT_VERSION%-stable_mono_win64_console.exe"
set "TEMPLATES=%APPDATA%\Godot\export_templates\%GODOT_VERSION%.stable.mono"
set "TMP_DIR=%TOOLS%\tmp"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_NOLOGO=1"

where curl >nul 2>&1 || (echo This needs the curl command, which Windows 10 version 1803 and newer carry. & goto fail)
where tar >nul 2>&1 || (echo This needs the tar command, which Windows 10 version 1803 and newer carry. & goto fail)
if not exist "%TOOLS%" mkdir "%TOOLS%"
if not exist "%TMP_DIR%" mkdir "%TMP_DIR%"

rem ---- the source: this folder if it is the repository, else %USERPROFILE%\DungeonCrawler (cloned or downloaded)
if exist "%~dp0DungeonCrawler.csproj" (
  cd /d "%~dp0"
) else (
  set "SRC=%USERPROFILE%\DungeonCrawler"
  if not "%DUNGEONCRAWLER_SRC%"=="" set "SRC=%DUNGEONCRAWLER_SRC%"
  call :get_source || goto fail
  cd /d "%SRC%"
)
echo.
echo == Building from %CD%

rem ---- Godot (the game engine) and its export templates, in your user folder
if exist "%GODOT%" (
  echo Godot %GODOT_VERSION% .NET: already here
) else (
  echo.
  echo == Downloading Godot %GODOT_VERSION% .NET ^(about 60 MB^)
  curl -fL -# -o "%TMP_DIR%\godot.zip" "%GODOT_BUILDS%/Godot_v%GODOT_VERSION%-stable_mono_win64.zip" || (echo The Godot download failed. & goto fail)
  tar -xf "%TMP_DIR%\godot.zip" -C "%TOOLS%" || (echo Unpacking Godot failed. & goto fail)
  del "%TMP_DIR%\godot.zip"
  if not exist "%GODOT%" (echo Godot did not unpack where expected: %GODOT% & goto fail)
)
if exist "%TEMPLATES%\windows_release_x86_64.exe" (
  echo Export templates: already here
) else (
  echo.
  echo == Downloading the Godot export templates ^(about 1 GB; this is the big one^)
  curl -fL -# -o "%TMP_DIR%\templates.zip" "%GODOT_BUILDS%/Godot_v%GODOT_VERSION%-stable_mono_export_templates.tpz" || (echo The template download failed. & goto fail)
  if exist "%TMP_DIR%\templates" rmdir /s /q "%TMP_DIR%\templates"
  mkdir "%TMP_DIR%\templates"
  tar -xf "%TMP_DIR%\templates.zip" -C "%TMP_DIR%\templates" || (echo Unpacking the templates failed. & goto fail)
  if not exist "%TEMPLATES%" mkdir "%TEMPLATES%"
  robocopy "%TMP_DIR%\templates\templates" "%TEMPLATES%" /E /MOVE /NFL /NDL /NJH /NJS /NP >nul
  rmdir /s /q "%TMP_DIR%\templates" 2>nul
  del "%TMP_DIR%\templates.zip"
  if not exist "%TEMPLATES%\windows_release_x86_64.exe" (echo The export templates did not unpack where expected: %TEMPLATES% & goto fail)
)

rem ---- the .NET 8 SDK (the game is written in C#): the one on this PC if it has version 8, else a private copy
set "HAVE_DOTNET8="
where dotnet >nul 2>&1 && (dotnet --list-sdks 2>nul | findstr /b /c:"8." >nul && set "HAVE_DOTNET8=1")
if defined HAVE_DOTNET8 (
  echo .NET 8 SDK: already here
) else (
  if exist "%TOOLS%\dotnet\dotnet.exe" ("%TOOLS%\dotnet\dotnet.exe" --list-sdks 2>nul | findstr /b /c:"8." >nul && set "HAVE_DOTNET8=1")
  if not defined HAVE_DOTNET8 (
    echo.
    echo == Downloading the .NET 8 SDK ^(about 200 MB^)
    curl -fL -# -o "%TMP_DIR%\dotnet.zip" "https://aka.ms/dotnet/8.0/dotnet-sdk-win-x64.zip" || (echo The .NET download failed. & goto fail)
    if exist "%TOOLS%\dotnet" rmdir /s /q "%TOOLS%\dotnet"
    mkdir "%TOOLS%\dotnet"
    tar -xf "%TMP_DIR%\dotnet.zip" -C "%TOOLS%\dotnet" || (echo Unpacking .NET failed. & goto fail)
    del "%TMP_DIR%\dotnet.zip"
  )
  set "DOTNET_ROOT=%TOOLS%\dotnet"
  set "PATH=%TOOLS%\dotnet;%PATH%"
  echo .NET 8 SDK: %TOOLS%\dotnet
)

rem ---- import the assets, compile the code, export the game for Windows
if exist ".godot\import-ok" (
  echo Assets: already imported
) else (
  echo.
  echo == Importing the game's assets ^(first time only, 5-15 minutes^)
  if exist ".godot\imported" rmdir /s /q ".godot\imported"
  "%GODOT%" --headless --import --path . > "%TMP_DIR%\godot-import.log" 2>&1
  findstr /c:"FATAL" "%TMP_DIR%\godot-import.log" >nul && (echo The asset import failed; the log is %TMP_DIR%\godot-import.log & goto fail)
  if not exist ".godot" mkdir ".godot"
  echo ok> ".godot\import-ok"
)
echo.
echo == Compiling the game
dotnet build DungeonCrawler.csproj -c Release -v q --nologo -clp:ErrorsOnly || (echo Compiling failed. & goto fail)
echo.
echo == Exporting the game for Windows
if not exist "build\windows" mkdir "build\windows"
"%GODOT%" --headless --path . --export-release "Windows" build\windows\DungeonCrawler.exe > "%TMP_DIR%\godot-export.log" 2>&1
if not exist "build\windows\DungeonCrawler.exe" (type "%TMP_DIR%\godot-export.log" & echo The export failed; the log is %TMP_DIR%\godot-export.log & goto fail)
copy /y installer\windows\Setup.bat build\windows\ >nul
copy /y installer\windows\Host.bat build\windows\ >nul
echo.
echo == Built. Starting the setup program ^(%CD%\build\windows\Setup.bat^)
echo.
call build\windows\Setup.bat %*
goto end

:get_source
where git >nul 2>&1
if not errorlevel 1 (
  if exist "%SRC%\.git" (
    echo.
    echo == Updating the source in %SRC%
    git -C "%SRC%" pull --ff-only || exit /b 1
  ) else (
    echo.
    echo == Downloading the source into %SRC% ^(about 700 MB^)
    git clone --depth 1 "https://github.com/%REPO%.git" "%SRC%" || exit /b 1
  )
  exit /b 0
)
echo.
echo == Downloading the source into %SRC% ^(about 700 MB; git is not installed, so this is a plain copy^)
curl -fL -# -o "%TMP_DIR%\source.zip" "https://codeload.github.com/%REPO%/zip/refs/heads/main" || exit /b 1
if exist "%TMP_DIR%\source" rmdir /s /q "%TMP_DIR%\source"
mkdir "%TMP_DIR%\source"
tar -xf "%TMP_DIR%\source.zip" -C "%TMP_DIR%\source" || exit /b 1
del "%TMP_DIR%\source.zip"
if not exist "%SRC%" mkdir "%SRC%"
for /d %%d in ("%TMP_DIR%\source\*") do robocopy "%%d" "%SRC%" /E /NFL /NDL /NJH /NJS /NP >nul
rmdir /s /q "%TMP_DIR%\source"
if not exist "%SRC%\DungeonCrawler.csproj" (echo The source did not unpack where expected: %SRC% & exit /b 1)
exit /b 0

:fail
echo.
echo The build did not finish. Nothing was installed.
if "%~1"=="" pause
exit /b 1

:end
echo.
if "%~1"=="" pause
exit /b 0
