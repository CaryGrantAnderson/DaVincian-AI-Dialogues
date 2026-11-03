@echo off
cd /d "%~dp0"
set "DOTNET_CLI_HOME=%~dp0.runtime"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
echo Nex Voice: http://localhost:5188
echo Keep this window open while using the app. Press Ctrl+C to stop it.
dotnet run --project "%~dp0NexVoice.csproj" --no-launch-profile
pause
