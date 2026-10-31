@echo off
setlocal
rem Host a DungeonCrawler game for your friends from this PC. Double-click it and leave the window open; closing the
rem window stops the server.
rem   Host.bat              port 7777, the shipped campaign, up to 4 players
rem   Host.bat 8000 6       another port, up to 6 players
rem Friends start the game, choose Join Server and enter the address this prints. People on your home network use the
rem first address; people elsewhere need the second one AND your router to forward TCP port 7777 to this PC.
rem The game is saved as it goes (slot "server"), so stopping and starting again continues where everyone left off.
set "PORT=%~1"
if "%PORT%"=="" set "PORT=7777"
set "PLAYERS=%~2"
if "%PLAYERS%"=="" set "PLAYERS=4"
set "GAME=%~dp0DungeonCrawler.exe"
if not exist "%GAME%" set "GAME=%LOCALAPPDATA%\Programs\DungeonCrawler\DungeonCrawler.exe"
if not exist "%GAME%" echo DungeonCrawler.exe is not next to this script and the game is not installed.& pause & exit /b 1
set "LAN="
for /f "tokens=2 delims=:" %%a in ('ipconfig ^| findstr /c:"IPv4"') do if not defined LAN set "LAN=%%a"
set "LAN=%LAN: =%"
set "PUBLIC="
for /f %%a in ('curl -s --max-time 3 https://api.ipify.org 2^>nul') do set "PUBLIC=%%a"
echo DungeonCrawler server on port %PORT%, up to %PLAYERS% players.
echo   Same network:  ws://%LAN%:%PORT%
if defined PUBLIC echo   Internet:      ws://%PUBLIC%:%PORT%   (forward TCP %PORT% on your router to this PC first)
echo Windows may ask whether to allow DungeonCrawler through the firewall: click Allow.
echo Leave this window open while playing. Close it to stop the server; the game is saved and continues next time.
echo.
"%GAME%" --headless -- --server --port=%PORT% --bind=0.0.0.0 --campaign=builtin --save=server --max-players=%PLAYERS%
echo Server stopped.
pause
