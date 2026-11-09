@echo off
rem Card V0: run the automated route test in a player build and write Logs\stats.json + Logs\player.log.
rem Usage: Tools\run_autotest.cmd [route=R_SMOKE] [seconds=60, 0 = until the route is complete] [build=DevMono] [extra player args, e.g. -timeOfDay 23]
rem Then: powershell -ExecutionPolicy Bypass -File Tools\verify_run.ps1
setlocal
set "ROUTE=%~1"
if "%ROUTE%"=="" set "ROUTE=R_SMOKE"
set "SECS=%~2"
if "%SECS%"=="" set "SECS=60"
set "BUILD=%~3"
if "%BUILD%"=="" set "BUILD=DevMono"
set "PROJECT=E:\VeyraCity"
set "EXE=%PROJECT%\Builds\%BUILD%\VeyraCity.exe"
if not exist "%EXE%" (echo %EXE% not found: build it first with Tools\build_dev.cmd & exit /b 2)
if not exist "%PROJECT%\Logs" mkdir "%PROJECT%\Logs"
if exist "%PROJECT%\Logs\stats.json" del /q "%PROJECT%\Logs\stats.json"
echo Running %BUILD% route %ROUTE% for %SECS% s ...
start "" /wait "%EXE%" -screen-width 2560 -screen-height 1440 -screen-fullscreen 1 -autotest %SECS% -route %ROUTE% -statsPath "%PROJECT%\Logs\stats.json" -logFile "%PROJECT%\Logs\player.log" -veyraSaveDir "%PROJECT%\Logs\autotest_save" %4 %5 %6 %7 %8 %9
set RC=%ERRORLEVEL%
echo Player exit code %RC%. Stats: %PROJECT%\Logs\stats.json  Log: %PROJECT%\Logs\player.log
exit /b %RC%
