param([switch]$Test)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.dotnet-home'
$sdk = Join-Path $PSScriptRoot '.tools/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $sdk)) { $sdk = 'dotnet' }
if ($Test) {
    & $sdk run --project (Join-Path $PSScriptRoot 'tests/ReleaseRadar.Tests') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
& $sdk publish (Join-Path $PSScriptRoot 'src/ReleaseRadar') -c Release --self-contained false -o (Join-Path $PSScriptRoot 'dist/ReleaseRadar')
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $PSScriptRoot 'dist/ReleaseRadar/README.md')
Compress-Archive -Path (Join-Path $PSScriptRoot 'dist/ReleaseRadar/*') -DestinationPath (Join-Path $PSScriptRoot 'dist/ReleaseRadar-Windows.zip') -Force
Write-Host 'Ready: dist/ReleaseRadar/ReleaseRadar.exe'
