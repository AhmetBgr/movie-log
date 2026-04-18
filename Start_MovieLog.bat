@echo off
if not "%minimized%"=="" goto :minimized
set minimized=true
start /min cmd /C "%~dpnx0"
goto :EOF
:minimized
echo Starting Movie Log Server...
:: Launch the browser immediately
start https://localhost:7008
:loop
:: Start the server project (which hosts the WASM client)
dotnet run --project Server/MyPrivateWatchlist.Server.csproj --launch-profile https
echo.
echo Server process ended. 
echo Press any key to RESTART the server, or close this window to quit.
pause
goto loop


