@echo off
rem Card V0: build the Development Mono player -> Builds\DevMono\VeyraCity.exe (also writes Logs\build_<timestamp>.log).
rem Batch mode cannot open a project that is already open in an Editor: close the Unity Editor first,
rem or use the menu Veyra > Build > Dev Mono inside the Editor.
setlocal
set "UNITY=E:\Unity\Editors\6000.6.8f1\Editor\Unity.exe"
set "PROJECT=E:\VeyraCity"
if not exist "%UNITY%" (echo Unity 6000.6.8f1 not found at %UNITY% & exit /b 2)
set TEMP=E:\Temp
set TMP=E:\Temp
if not exist "%PROJECT%\Logs" mkdir "%PROJECT%\Logs"
echo Building Dev Mono player (log: %PROJECT%\Logs\unity_build.log) ...
start "" /wait "%UNITY%" -batchmode -quit -projectPath "%PROJECT%" -executeMethod VeyraBuild.BuildDevMono -logFile "%PROJECT%\Logs\unity_build.log"
set RC=%ERRORLEVEL%
if %RC%==0 (echo BUILD OK: %PROJECT%\Builds\DevMono\VeyraCity.exe) else (echo BUILD FAILED with exit code %RC%: see %PROJECT%\Logs\unity_build.log)
exit /b %RC%
