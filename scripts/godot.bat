@echo off
setlocal
rem Godot 4.7.2 .NET for this repository on Windows: GODOT when that variable names an exe, else godot.exe on PATH, else
rem the console build that build.bat fetched into %LOCALAPPDATA%\DungeonCrawlerTools. .vscode/tasks.json calls this on
rem Windows ("import assets"); .vscode/launch.json names the fetched exe directly, since a debugger must start the real
rem process. None present: run build.bat once.
set "G=%GODOT%"
if defined G goto run
for %%p in (godot.exe) do if not "%%~$PATH:p"=="" set "G=%%~$PATH:p"
if defined G goto run
set "G=%LOCALAPPDATA%\DungeonCrawlerTools\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe"
if not exist "%G%" (echo No Godot 4.7.2 .NET on this PC: set GODOT to its exe, or run build.bat once ^(it fetches one into %LOCALAPPDATA%\DungeonCrawlerTools^). 1>&2 & exit /b 1)
rem Godot .NET finds its runtime by running "dotnet --list-sdks" and crashes at startup when none is on PATH: without a
rem .NET 8 SDK on PATH, put the private copy build.bat fetched in front of it (no copy there: Godot reports it itself).
where dotnet.exe >nul 2>&1 && (dotnet.exe --list-sdks 2>nul | findstr /b /c:"8." >nul) && goto run
if exist "%LOCALAPPDATA%\DungeonCrawlerTools\dotnet\dotnet.exe" set "PATH=%LOCALAPPDATA%\DungeonCrawlerTools\dotnet;%PATH%"
:run
"%G%" %*
exit /b %errorlevel%
