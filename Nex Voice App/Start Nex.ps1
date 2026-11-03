$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.runtime'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
Write-Host 'Nex Voice opens at http://localhost:5188'
Write-Host 'The API key stays in the server environment. Press Ctrl+C to stop the app.'
dotnet run --project "$PSScriptRoot\NexVoice.csproj" --no-launch-profile
