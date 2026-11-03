param([ValidateSet('Debug','Release')][string]$Configuration = 'Release', [switch]$Test)
$ErrorActionPreference = 'Stop'
$studioMsBuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
if (!(Test-Path -LiteralPath $studioMsBuild)) { throw 'Visual Studio 2022 MSBuild was not found at the configured path.' }
& $studioMsBuild (Join-Path $PSScriptRoot 'Source\DVMS.AIDialogueStudio.csproj') /restore "/p:Configuration=$Configuration" /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw 'Studio build failed.' }
if ($Test) {
    foreach ($studioTestArgument in @('--self-test', '--live-self-test')) {
    $studioRun = Start-Process -FilePath (Join-Path $PSScriptRoot "Build\$Configuration\DVMS.AIDialogueStudio.exe") -ArgumentList $studioTestArgument -WindowStyle Hidden -Wait -PassThru
    Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot "Build\$Configuration\TestEvidence") -Filter results.txt -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | Get-Content
    if ($studioRun.ExitCode -ne 0) { throw 'Studio verification failed. See TestEvidence.' }
    }
}
