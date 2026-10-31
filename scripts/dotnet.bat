@echo off
setlocal
rem dotnet for this repository on Windows, the rule build.bat uses: the dotnet on PATH when it is a .NET 8 SDK, else the
rem private copy build.bat fetched into %LOCALAPPDATA%\DungeonCrawlerTools. .vscode/tasks.json calls this on Windows, so
rem F5 builds without a system-wide .NET install. Neither present: run build.bat once.
set "PRIVATE=%LOCALAPPDATA%\DungeonCrawlerTools\dotnet"
where dotnet.exe >nul 2>&1 || goto private
dotnet.exe --list-sdks 2>nul | findstr /b /c:"8." >nul || goto private
dotnet.exe %*
exit /b %errorlevel%

:private
if not exist "%PRIVATE%\dotnet.exe" (echo No .NET 8 SDK on this PC. Run build.bat once; it fetches one into %PRIVATE%. 1>&2 & exit /b 1)
set "DOTNET_ROOT=%PRIVATE%"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_NOLOGO=1"
"%PRIVATE%\dotnet.exe" %*
exit /b %errorlevel%
