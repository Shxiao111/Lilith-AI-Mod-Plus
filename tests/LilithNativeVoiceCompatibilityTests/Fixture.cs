// Behavioral regression tests for the production native-voice methods, isolated from Unity.
using System.Text;

DialogueManagerUpdatePatch.RunTests();

static class DialogueManagerUpdatePatch
{
    private static readonly Dictionary<int, string> NativeVoiceFilesByLineId = new();
    private static readonly Dictionary<int, float> NativeVoiceDurationByNodeId = new();
    private static string _loadedNativeVoicePackDirectory = string.Empty;
    private static string _lastObservedVoiceLanguage = string.Empty;
    private static int _lastInjectedNativeNodeId = -1;
    private static float _lastInjectedNativeVoiceAt = -10f;
    private static int _assertions;

    private static AudioClip CreateAudioClipFromWav(byte[] bytes) => new() { length = 5f };

    /* PRODUCTION_METHODS */

    internal static void RunTests()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lilith-voice-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var wave = Path.Combine(directory, "line.wav");
        File.WriteAllBytes(wave, new byte[] { 1 });
        File.WriteAllText(Path.Combine(directory, "manifest.tsv"), "42\t100\tx\tx\tx\tx\tx\tline.wav\n");
        Plugin.NativeVoicePackDirectory.Value = directory;
        var node = new DialogueNode { id = 42, lineId = 100, soundId = "official", actionType = LilithActionType.Reaction };

        foreach (var language in new[] { "ja", "ja-JP" })
        {
            LocalizationConfig.Language = language;
            Assert(IsJapaneseVoiceMode(), "AI Japanese speech must follow the official voice language.");
            NativeVoiceFilesByLineId[node.lineId] = wave;
            NativeVoiceDurationByNodeId[node.id] = 9f;
            _lastInjectedNativeNodeId = node.id;
            _lastInjectedNativeVoiceAt = Time.unscaledTime;
            Assert(!TryPlayInjectedNativeVoice(node), "Japanese must bypass even a recent Chinese injection.");
            Assert(ExtendNativeNodeDuration(node, 2f) == 2f, "Japanese duration must ignore cached Chinese audio.");
            _lastInjectedNativeNodeId = -1;
            Assert(!TryPlayInjectedNativeVoice(node), "Japanese must use official dialogue/action audio.");
            node.soundId = null;
            node.actionType = LilithActionType.None;
            NativeVoiceFilesByLineId.Clear();
            Assert(!TryPlayInjectedNativeVoice(node), "An unvoiced Japanese line must still allow native playback.");
            _loadedNativeVoicePackDirectory = string.Empty;
            EnsureNativeVoiceManifestLoaded();
            Assert(_loadedNativeVoicePackDirectory.Length == 0 && NativeVoiceFilesByLineId.Count == 0,
                "Japanese must not load a supplemental manifest.");
            Assert(ResourceManager.Calls == 0 && DialogueLineRepository.Calls == 0 && AudioManager.PlayCalls == 0 && AudioManager.StopCalls == 0,
                "Japanese must not inspect, stop or replace official audio.");
        }

        LocalizationConfig.Language = "zh-CN";
        Assert(!IsJapaneseVoiceMode(), "AI language must change immediately with the official setting.");
        ObserveVoiceLanguageSelection();
        Assert(NativeVoiceDurationByNodeId.Count == 0 && _lastInjectedNativeNodeId == -1,
            "Language changes must clear playback timing and duplicate-suppression state.");
        node.soundId = "official";
        ResourceManager.HasVoice = true;
        Assert(!TryPlayInjectedNativeVoice(node), "Chinese official dialogue voice must take priority.");
        ResourceManager.HasVoice = false;
        node.actionType = LilithActionType.Reaction;
        ResourceManager.HasActionVoice = true;
        Assert(!TryPlayInjectedNativeVoice(node), "Chinese official action voice must take priority.");
        ResourceManager.HasActionVoice = false;
        node.soundId = null;
        node.actionType = LilithActionType.None;
        Assert(TryPlayInjectedNativeVoice(node) && AudioManager.PlayCalls == 1,
            "An unvoiced Chinese line must still play its supplemental WAV.");
        Assert(ExtendNativeNodeDuration(node, 2f) == 5.75f, "Chinese supplemental duration must still be applied.");
        Assert(TryPlayInjectedNativeVoice(node) && AudioManager.PlayCalls == 1,
            "Multiple native hooks must not replay the same Chinese supplement.");
        Time.unscaledTime += 2f;
        node.lineId = 101;
        Assert(!TryPlayInjectedNativeVoice(node), "A missing Chinese supplement must allow native playback.");
        Plugin.NativeVoicePackEnabled.Value = false;
        node.lineId = 100;
        Assert(!TryPlayInjectedNativeVoice(node), "Disabling supplemental voices must still work.");
        Plugin.NativeVoicePackEnabled.Value = true;

        LocalizationConfig.Language = "ja";
        Assert(!TryPlayInjectedNativeVoice(node) && ExtendNativeNodeDuration(node, 2f) == 2f,
            "Switching to Japanese must bypass Chinese caches before the next update.");
        ObserveVoiceLanguageSelection();
        Assert(NativeVoiceFilesByLineId.Count == 0 && NativeVoiceDurationByNodeId.Count == 0,
            "Switching to Japanese must clear supplemental caches.");
        LocalizationConfig.Language = "zh-CN";
        ObserveVoiceLanguageSelection();
        Assert(TryPlayInjectedNativeVoice(node) && AudioManager.PlayCalls == 2,
            "Switching back to Chinese must reload and play supplemental audio.");
        Assert(AudioManager.StopCalls == 0, "Supplemental injection must not stop official playback.");
        Console.WriteLine($"Native voice compatibility tests passed ({_assertions} assertions); settings override and all three playback hooks checked.");
    }

    private static void Assert(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
}

sealed class Entry<T>(T value) { public T Value = value; }
static class Plugin
{
    internal static readonly Entry<bool> NativeVoicePackEnabled = new(true);
    internal static readonly Entry<string> NativeVoicePackDirectory = new("");
    internal static readonly TestLog PluginLog = new();
}
sealed class TestLog
{
    public void LogInfo(string message) { }
    public void LogWarning(string message) => throw new InvalidOperationException(message);
}
static class LocalizationConfig
{
    internal static string Language = "ja";
    public static string GetCurrentVoiceLanguage() => Language;
}
static class Time { internal static float unscaledTime = 10f; }
enum LilithActionType { None, Reaction }
sealed class DialogueNode
{
    public int id;
    public int lineId;
    public string? soundId;
    public LilithActionType actionType;
}
static class DialogueLineRepository
{
    internal static int Calls;
    public static bool TryGetVoiceSoundId(int lineId, out string soundId) { Calls++; soundId = ""; return false; }
}
static class ResourceManager
{
    internal static int Calls;
    internal static bool HasVoice;
    internal static bool HasActionVoice;
    public static AudioClip? LoadVoiceClip(string soundId) { Calls++; return HasVoice ? new() : null; }
    public static AudioClip? LoadActionVoiceClip(LilithActionType action) { Calls++; return HasActionVoice ? new() : null; }
}
sealed class AudioClip { public float length; }
static class AudioManager
{
    internal static int PlayCalls;
    internal static int StopCalls;
    public static void PlayVoice(AudioClip clip, bool a, bool b) { PlayCalls++; }
    public static void StopVoice() { StopCalls++; }
}
