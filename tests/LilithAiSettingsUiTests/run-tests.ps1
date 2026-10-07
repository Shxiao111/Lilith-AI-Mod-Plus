param([string]$DotnetPath = "dotnet")
$ErrorActionPreference = "Stop"
# Run the actual option construction against the official prefab's observed hierarchy.
$source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\..\src\LilithTextInjector\AiSettingsPanel.cs'))
$methods = foreach ($name in @('Options', 'DetachLocalization')) {
    $match = [regex]::Match($source, '(?ms)^    private static void ' + $name + '\(.*?^    }')
    if (!$match.Success) { throw "Production method not found: $name" }
    $match.Value
}
$work = Join-Path ([IO.Path]::GetTempPath()) ('lilith-ai-ui-tests-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($work) | Out-Null
$fixture = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Fixture.cs'))
[IO.File]::WriteAllText((Join-Path $work 'Program.cs'), $fixture.Replace('/* PRODUCTION_METHODS */', ($methods -join "`n`n")))
[IO.File]::WriteAllText((Join-Path $work 'Tests.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>')
& $DotnetPath run --project (Join-Path $work 'Tests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw "AI settings UI tests failed (exit $LASTEXITCODE)." }

