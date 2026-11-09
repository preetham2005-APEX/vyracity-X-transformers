@echo off
rem Card V0: build the Release IL2CPP player -> Builds\Release\VeyraCity.exe (also writes Logs\build_<timestamp>.log).
rem Close the Unity Editor first (batch mode cannot open a project that is already open), or use Veyra > Build > Release IL2CPP.
setlocal
set "UNITY=E:\Unity\Editors\6000.6.8f1\Editor\Unity.exe"
set "PROJECT=E:\VeyraCity"
if not exist "%UNITY%" (echo Unity 6000.6.8f1 not found at %UNITY% & exit /b 2)
set TEMP=E:\Temp
set TMP=E:\Temp
if not exist "%PROJECT%\Logs" mkdir "%PROJECT%\Logs"
echo Building Release IL2CPP player (log: %PROJECT%\Logs\unity_build_release.log) ...
start "" /wait "%UNITY%" -batchmode -quit -projectPath "%PROJECT%" -executeMethod VeyraBuild.BuildReleaseIL2CPP -logFile "%PROJECT%\Logs\unity_build_release.log"
set RC=%ERRORLEVEL%
if %RC%==0 (echo BUILD OK: %PROJECT%\Builds\Release\VeyraCity.exe) else (echo BUILD FAILED with exit code %RC%: see %PROJECT%\Logs\unity_build_release.log)
exit /b %RC%
