param([string]$DotnetPath = "dotnet")
$ErrorActionPreference = "Stop"

# Compile the production methods against small game API doubles; Unity is not needed.
$sourcePath = Join-Path $PSScriptRoot '..\..\src\LilithTextInjector\Plugin.cs'
$source = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $sourcePath))
$methods = foreach ($name in @('IsJapaneseVoiceMode', 'ObserveVoiceLanguageSelection', 'TryPlayInjectedNativeVoice', 'ExtendNativeNodeDuration', 'EnsureNativeVoiceManifestLoaded')) {
    # Method closing braces are at four spaces; nested blocks are indented further.
    $pattern = '(?ms)^    (?:private|internal) static [^\r\n]*\b' + $name + '\(.*?^    }'
    $match = [regex]::Match($source, $pattern)
    if (!$match.Success) { throw "Production method not found: $name" }
    $match.Value
}
foreach ($removed in @('RestoreJapaneseGameVoiceOption', 'EnsureJapaneseVoiceOptionVisible', 'PublishJapaneseVoiceSelection', 'VoiceSettingsButtonClickPatch', 'JapaneseVoiceSelected', 'JapaneseNativeVoicePackDirectory', '_japaneseVoiceOverride')) {
    if ($source.Contains($removed)) { throw "Obsolete Japanese voice override remains: $removed" }
}
foreach ($hook in @('DialogueManagerPlayNodeVoicePatch', 'DialogueBubbleUIShowNodeVoicePatch', 'TypewriterPlayNativeVoicePatch')) {
    $match = [regex]::Match($source, '(?ms)^internal static class ' + $hook + '\b.*?^}')
    if (!$match.Success -or !$match.Value.Contains('TryPlayInjectedNativeVoice(node)')) { throw "Native playback hook bypasses the shared guard: $hook" }
}

$work = Join-Path ([IO.Path]::GetTempPath()) ('lilith-native-voice-tests-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($work) | Out-Null
$fixture = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Fixture.cs'))
[IO.File]::WriteAllText((Join-Path $work 'Program.cs'), $fixture.Replace('/* PRODUCTION_METHODS */', ($methods -join "`n`n")))
[IO.File]::WriteAllText((Join-Path $work 'Tests.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>')
& $DotnetPath run --project (Join-Path $work 'Tests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw "Native voice compatibility tests failed (exit $LASTEXITCODE)." }
