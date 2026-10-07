param([string]$DotnetPath = "dotnet")
$ErrorActionPreference = "Stop"
# This suite owns the disk/editor-draft and transport contracts; actual native Unity layout is checked in-game.
& $DotnetPath run --project (Join-Path $PSScriptRoot 'LilithAiSettingsTests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw "AI settings tests failed (exit $LASTEXITCODE)." }
