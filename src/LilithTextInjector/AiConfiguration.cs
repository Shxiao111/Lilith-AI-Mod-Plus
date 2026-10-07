using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace LilithTextInjector;

// Shared disk schema for the native editor and protocol clients. IDs are local, model is the upstream name.
internal sealed class AiConfiguration
{
    public int Version { get; set; } = 1;
    public Dictionary<string, AiProvider> Providers { get; set; } = new();
    // Speech connections are independent of chat. The same ID may exist in both dictionaries.
    public Dictionary<string, AiAsrProvider> AsrProviders { get; set; } = new();
    public AiModelReference Chat { get; set; } = new();
    public AiModelReference Asr { get; set; } = new();
    public AiModelReference? AsrFallback { get; set; }
}
internal sealed class AiModelReference
{
    public string ProviderId { get; set; } = "";
    public string ModelId { get; set; } = "";
}
internal sealed class AiProvider
{
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    // One wire protocol for every chat model. Empty on old files until the models agree.
    public string Protocol { get; set; } = "";
    // Chat Completions gateways that reject max_completion_tokens set this to max_tokens.
    public string? MaxTokensField { get; set; }
    public AiCredential Credential { get; set; } = new();
    public Dictionary<string, AiChatModel> ChatModels { get; set; } = new();
}
internal sealed class AiAsrProvider
{
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public AiCredential Credential { get; set; } = new();
    public Dictionary<string, AiAsrModel> Models { get; set; } = new();
}
internal sealed class AiCredential
{
    public string Mode { get; set; } = "none";
    public string? Reference { get; set; }
}
internal sealed class AiChatModel
{
    public string Model { get; set; } = "";
    public string Name { get; set; } = "";
    public string Protocol { get; set; } = "chat-completions";
    public int? ContextWindow { get; set; }
    public int? MaxOutputTokens { get; set; }
    public string? MaxTokensField { get; set; }
    public double? Temperature { get; set; }
    public string? ReasoningEffort { get; set; }
    public int? ThinkingBudget { get; set; }
    public string? ThinkingLevel { get; set; }
    public string Tools { get; set; } = "auto";
    public string NativeSearch { get; set; } = "none";
}
internal sealed class AiAsrModel
{
    public string Model { get; set; } = "";
    public string Name { get; set; } = "";
    public string Protocol { get; set; } = "audio-transcriptions";
    public string? Endpoint { get; set; }
}
// Resolved objects belong to one operation, never to the mutable UI draft or a later disk read.
internal sealed record AiChatSnapshot(string ProviderId, string ModelId, string BaseUrl, string? Key, AiChatModel Model);
internal sealed record AiAsrSnapshot(string ProviderId, string BaseUrl, string? Key, AiAsrModel Model);
internal sealed record AiRecordingSnapshot(AiAsrSnapshot Primary, AiAsrSnapshot? Fallback);
internal sealed class AiConfigurationException : Exception
{
    internal AiConfigurationException(string message) : base(message) { }
}

// Owns validation, one-time migration and a recoverable two-file commit. The journal restores the old pair after a crash.
internal sealed class AiConfigurationStore
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    internal static readonly string[] ChatProtocols = { "chat-completions", "responses", "anthropic", "gemini" };
    internal static string CanonicalChatProtocol(string protocol) => protocol switch
    {
        "openai-completions" => "chat-completions", "openai-responses" => "responses", "anthropic-messages" => "anthropic", _ => protocol
    };
    internal static bool IsProviderId(string id, bool lowercaseOnly)
    {
        if (id.Length == 0 || id.Length > 64) return false;
        if (!IsIdLetter(id[0], lowercaseOnly)) return false;
        for (var i = 1; i < id.Length; i++)
        {
            var c = id[i];
            if (!IsIdLetter(c, lowercaseOnly) && c is not (>= '0' and <= '9') && c != '-') return false;
        }
        return true;
    }
    private static bool IsIdLetter(char c, bool lowercaseOnly) => lowercaseOnly ? c is >= 'a' and <= 'z' : c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');
    internal static bool AddChatModel(AiProvider provider, string upstreamId)
    {
        var id = upstreamId.Trim();
        if (id.Length == 0 || provider.ChatModels.Values.Any(m => m != null && m.Model == id)) return false;
        var key = id; var n = 2;
        while (provider.ChatModels.ContainsKey(key)) key = id + "-" + n++;
        var protocol = string.IsNullOrWhiteSpace(provider.Protocol) ? "chat-completions" : CanonicalChatProtocol(provider.Protocol);
        provider.Protocol = protocol;
        provider.ChatModels[key] = new AiChatModel
        {
            Model = id, Name = id, Protocol = protocol,
            MaxTokensField = protocol == "chat-completions" ? provider.MaxTokensField : null
        };
        return true;
    }
    internal static readonly string[] AsrProtocols = { "gemini-audio", "qwen-http", "dashscope-realtime", "audio-transcriptions" };
    // Stored speech keys use their own credential-file entry so a later edit does not change the chat key.
    internal static string AsrCredentialReference(string providerId) => "asr:" + providerId;
    internal readonly string ConfigPath;
    internal readonly string CredentialsPath;
    private readonly string _journal;
    private readonly object _gate = new();
    // Test-only injection at the file boundary also exercises rollback of an interrupted second replacement.
    private readonly Action<string, string> _replace;
    internal AiConfigurationStore(string directory, Action<string, string>? replace = null)
    {
        ConfigPath = Path.Combine(directory, "lilith-ai.json");
        CredentialsPath = Path.Combine(directory, "lilith-ai.credentials.json");
        _journal = Path.Combine(directory, "lilith-ai.transaction.json");
        _replace = replace ?? Replace;
    }
    internal bool Exists => File.Exists(ConfigPath);
    internal (AiConfiguration Config, Dictionary<string, string> Credentials) Read()
    {
        lock (_gate)
        {
            Recover();
            try
            {
                var text = File.ReadAllText(ConfigPath);
                var root = JsonNode.Parse(text) as JsonObject
                    ?? throw new AiConfigurationException("AI configuration is empty.");
                if (root["version"] == null || root["providers"] == null || root["chat"] == null || root["asr"] == null)
                    throw new AiConfigurationException("AI configuration requires version, providers, chat and asr fields.");
                var credentials = File.Exists(CredentialsPath)
                    ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(CredentialsPath), Json)
                        ?? throw new AiConfigurationException("Credential storage is empty.")
                    : new Dictionary<string, string>();
                // Old files keep speech models on the chat provider. Split in memory; the next save writes asrProviders.
                SeparateAsrProviders(root, credentials);
                var config = JsonSerializer.Deserialize<AiConfiguration>(root, Json)
                    ?? throw new AiConfigurationException("AI configuration is empty.");
                Validate(config);
                return (config, credentials);
            }
            catch (AiConfigurationException) { throw; }
            catch (Exception e) when (e is IOException || e is JsonException || e is UnauthorizedAccessException || e is NullReferenceException || e is ArgumentException)
            { throw new AiConfigurationException("Cannot read AI configuration or credential storage; check file format and permissions."); }
        }
    }
    internal AiChatSnapshot ChatSnapshot()
    {
        var (c, keys) = Read();
        var p = c.Providers[c.Chat.ProviderId];
        var model = p.ChatModels[c.Chat.ModelId];
        if (ChatProtocols.Contains(p.Protocol)) model.Protocol = p.Protocol;
        return new(c.Chat.ProviderId, c.Chat.ModelId, p.BaseUrl, ResolveKey(p.Credential, keys), model);
    }
    internal AiRecordingSnapshot RecordingSnapshot()
    {
        var (c, keys) = Read();
        AiAsrSnapshot Resolve(AiModelReference r)
        {
            var p = c.AsrProviders[r.ProviderId];
            return new(r.ProviderId, p.BaseUrl, ResolveKey(p.Credential, keys), p.Models[r.ModelId]);
        }
        return new(Resolve(c.Asr), c.AsrFallback == null ? null : Resolve(c.AsrFallback));
    }
    internal static string? ResolveKey(AiCredential c, Dictionary<string, string> keys)
    {
        if (c.Mode == "none") return null;
        string? value = c.Mode == "env" ? Environment.GetEnvironmentVariable(c.Reference!)
            : keys.TryGetValue(c.Reference!, out var saved) ? saved : null;
        if (string.IsNullOrWhiteSpace(value))
            throw new AiConfigurationException(c.Mode == "env" ? "The referenced credential environment variable is missing." : "The referenced local credential is not configured.");
        return value;
    }
    internal static void Validate(AiConfiguration c)
    {
        void Require(bool condition, string message) { if (!condition) throw new AiConfigurationException(message); }
        bool Token(string? value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 32) return false;
            foreach (var ch in value)
                if (!(ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '-')) return false;
            return true;
        }
        void Url(string? url, bool ws = false) => Require(Uri.TryCreate(url, UriKind.Absolute, out var u)
            && (ws ? u.Scheme == "ws" || u.Scheme == "wss" : u.Scheme == "http" || u.Scheme == "https")
            && string.IsNullOrEmpty(u.UserInfo) && string.IsNullOrEmpty(u.Query) && string.IsNullOrEmpty(u.Fragment), "Use an absolute HTTP/WS URL without credentials, query or fragment.");
        Require(c.Version == 1, "Unsupported AI configuration version.");
        Require(c.Providers != null && c.Providers.Count > 0, "At least one provider is required.");
        Require(c.AsrProviders != null, "Speech providers are required.");
        foreach (var (id, p) in c.Providers!)
        {
            Require(IsProviderId(id, false) && p != null, "Provider ID must start with a letter and use only letters, digits, and hyphens.");
            Require(!string.IsNullOrWhiteSpace(p!.Name), "Provider display name is required.");
            Url(p.BaseUrl);
            Require(p.MaxTokensField == null || p.MaxTokensField == "max_tokens" || p.MaxTokensField == "max_completion_tokens", "Chat Completions output field must be max_tokens or max_completion_tokens.");
            ApplyProviderProtocol(p);
            Require(p.ChatModels == null || p.ChatModels.Count == 0 || ChatProtocols.Contains(p.Protocol), "A provider uses one chat protocol. Split mixed protocols into two providers.");
            Require(p.MaxTokensField == null || p.Protocol == "chat-completions", "maxTokensField requires OpenAI Chat Completions.");
            Require(p.Credential != null && new[] { "stored", "env", "none" }.Contains(p.Credential.Mode), "Credential mode must be stored, env or none.");
            Require(p.Credential!.Mode == "none" || !string.IsNullOrWhiteSpace(p.Credential.Reference), "A credential reference is required.");
            Require(p.ChatModels != null, "Provider model catalogs are required.");
            foreach (var (modelId, m) in p.ChatModels!)
            {
                Require(!string.IsNullOrWhiteSpace(modelId) && m != null && !string.IsNullOrWhiteSpace(m.Model), "Chat model ID is required.");
                Require(ChatProtocols.Contains(m!.Protocol) && m.Protocol == p.Protocol, "Chat model protocol must match its provider.");
                Require(m.ContextWindow == null || m.ContextWindow > 0, "Context window must be positive.");
                Require(new[] { "auto", "on", "off" }.Contains(m.Tools), "Tool policy must be auto, on or off.");
                Require(m.MaxOutputTokens == null || m.MaxOutputTokens > 0, "Output token limit must be positive.");
                Require(m.Temperature == null || double.IsFinite(m.Temperature.Value) && m.Temperature >= 0, "Temperature must be nonnegative.");
                Require(m.ReasoningEffort == null || (m.Protocol == "responses" || m.Protocol == "chat-completions") && Token(m.ReasoningEffort), "Reasoning effort requires an OpenAI protocol and a short token.");
                Require(m.ThinkingBudget == null || (m.Protocol == "anthropic" || m.Protocol == "gemini") && m.ThinkingBudget >= -1, "Thinking budget requires Anthropic or Gemini.");
                Require(m.ThinkingLevel == null || m.Protocol == "gemini", "Thinking level requires Gemini.");
                Require(m.ThinkingBudget == null || m.ThinkingLevel == null, "Choose thinking budget or thinking level, not both.");
                Require(m.NativeSearch == "none" || m.NativeSearch == "google" && m.Protocol == "gemini"
                    || m.NativeSearch == "dashscope" && m.Protocol == "responses", "Native search does not match the protocol.");
                Require(m.Protocol != "anthropic" || m.MaxOutputTokens != null, "Anthropic Messages requires an explicit maxOutputTokens value.");
                Require(m.Protocol != "anthropic" || m.ThinkingBudget == null || m.ThinkingBudget >= 1024 && m.MaxOutputTokens > m.ThinkingBudget, "Anthropic thinking needs a budget >=1024 and a larger output limit.");
            }
        }
        foreach (var (id, p) in c.AsrProviders!)
        {
            Require(IsProviderId(id, false) && p != null, "Provider ID must start with a letter and use only letters, digits, and hyphens.");
            Require(!string.IsNullOrWhiteSpace(p!.Name), "Provider display name is required.");
            Url(p.BaseUrl);
            Require(p.Credential != null && new[] { "stored", "env", "none" }.Contains(p.Credential.Mode), "Credential mode must be stored, env or none.");
            Require(p.Credential!.Mode == "none" || !string.IsNullOrWhiteSpace(p.Credential.Reference), "A credential reference is required.");
            Require(p.Models != null, "Provider model catalogs are required.");
            foreach (var (modelId, m) in p.Models!)
            {
                Require(!string.IsNullOrWhiteSpace(modelId) && m != null && !string.IsNullOrWhiteSpace(m.Model), "ASR model ID is required.");
                Require(AsrProtocols.Contains(m!.Protocol), "Unknown ASR protocol.");
                if (m.Protocol == "dashscope-realtime") Url(m.Endpoint, true);
                else if (m.Endpoint != null) Url(m.Endpoint);
            }
        }
        void Reference(AiModelReference? r, bool chat)
        {
            var message = "Selected model reference is invalid. Reassign active models before deleting a provider or model.";
            if (chat)
                Require(r != null && !string.IsNullOrWhiteSpace(r.ProviderId) && !string.IsNullOrWhiteSpace(r.ModelId) && c.Providers.TryGetValue(r.ProviderId, out var chatProvider)
                    && chatProvider.ChatModels.ContainsKey(r.ModelId), message);
            else
                Require(r != null && !string.IsNullOrWhiteSpace(r.ProviderId) && !string.IsNullOrWhiteSpace(r.ModelId) && c.AsrProviders.TryGetValue(r.ProviderId, out var speechProvider)
                    && speechProvider.Models.ContainsKey(r.ModelId), message);
        }
        Reference(c.Chat, true); Reference(c.Asr, false);
        if (c.AsrFallback != null)
        {
            Reference(c.AsrFallback, false);
            Require(c.AsrProviders[c.AsrFallback.ProviderId].Models[c.AsrFallback.ModelId].Protocol != "dashscope-realtime", "ASR fallback must be an HTTP model.");
        }
    }
    // A file that already has asrProviders is left alone, even when that object is empty.
    private static void SeparateAsrProviders(JsonObject root, Dictionary<string, string> credentials)
    {
        if (root.ContainsKey("asrProviders")) return;
        if (root["providers"] is not JsonObject providers) return;
        string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
        var chatId = root["chat"] is JsonObject chat ? Text(chat["providerId"]) : null;
        var speech = new JsonObject();
        var ids = new List<string>();
        foreach (var pair in providers) ids.Add(pair.Key);
        foreach (var id in ids)
        {
            if (providers[id] is not JsonObject provider || provider["asrModels"] is not JsonObject models) continue;
            if (models.Count == 0)
            {
                provider.Remove("asrModels");
                continue;
            }
            var credential = provider["credential"] is JsonObject existing
                ? JsonNode.Parse(existing.ToJsonString())!.AsObject()!
                : new JsonObject { ["mode"] = "none" };
            if (Text(credential["mode"]) == "stored")
            {
                var reference = Text(credential["reference"]);
                if (!string.IsNullOrWhiteSpace(reference))
                {
                    if (credentials.TryGetValue(reference, out var secret)) credentials[AsrCredentialReference(id)] = secret;
                    credential["reference"] = AsrCredentialReference(id);
                }
            }
            var entry = new JsonObject
            {
                ["credential"] = credential,
                ["models"] = JsonNode.Parse(models.ToJsonString())
            };
            var name = Text(provider["name"]);
            var baseUrl = Text(provider["baseUrl"]);
            if (name != null) entry["name"] = name;
            if (baseUrl != null) entry["baseUrl"] = baseUrl;
            speech[id] = entry;
            var chatModels = provider["chatModels"] as JsonObject;
            var speechOnly = (chatModels == null || chatModels.Count == 0) && !string.Equals(id, chatId, StringComparison.Ordinal);
            if (speechOnly) providers.Remove(id);
            else provider.Remove("asrModels");
        }
        root["asrProviders"] = speech;
    }
    internal void Save(AiConfiguration c, Dictionary<string, string> credentials)
    {
        lock (_gate)
        {
            Validate(c);
            // Missing credentials may be intentionally cleared, but never silently select another connection.
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            Recover();
            var old = new Dictionary<string, string?>
            {
                [ConfigPath] = File.Exists(ConfigPath) ? File.ReadAllText(ConfigPath) : null,
                [CredentialsPath] = File.Exists(CredentialsPath) ? File.ReadAllText(CredentialsPath) : null
            };
            try
            {
                File.WriteAllText(ConfigPath + ".tmp", JsonSerializer.Serialize(c, Json));
                File.WriteAllText(CredentialsPath + ".tmp", JsonSerializer.Serialize(credentials, Json));
                File.WriteAllText(_journal + ".tmp", JsonSerializer.Serialize(old, Json));
                Replace(_journal + ".tmp", _journal);
                _replace(CredentialsPath + ".tmp", CredentialsPath);
                _replace(ConfigPath + ".tmp", ConfigPath);
                File.Delete(_journal);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Recover();
                throw new AiConfigurationException("Could not save AI configuration; the previous files were retained. Check permissions and disk space.");
            }
            finally
            {
                foreach (var path in new[] { ConfigPath, CredentialsPath, _journal })
                    try { if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp"); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
            }
        }
    }
    private static void Replace(string temp, string destination)
    {
        if (File.Exists(destination)) File.Replace(temp, destination, null);
        else File.Move(temp, destination);
    }
    private void Recover()
    {
        if (!File.Exists(_journal)) return;
        try
        {
            var old = JsonSerializer.Deserialize<Dictionary<string, string?>>(File.ReadAllText(_journal), Json)!;
            foreach (var path in new[] { ConfigPath, CredentialsPath })
            {
                if (old[path] == null) File.Delete(path);
                else { File.WriteAllText(path + ".recovery", old[path]); Replace(path + ".recovery", path); }
            }
            File.Delete(_journal);
        }
        catch { throw new AiConfigurationException("AI save recovery failed. Restore the configuration backup before continuing."); }
    }
    internal void Migrate(string legacyPath)
    {
        if (Exists) return;
        var entries = new Dictionary<(string, string), string>();
        var section = "";
        if (File.Exists(legacyPath)) foreach (var line in File.ReadAllLines(legacyPath))
        {
            var value = line.Trim();
            if (value.StartsWith("#") || value.Length == 0) continue;
            if (value.StartsWith("[") && value.EndsWith("]")) { section = value[1..^1]; continue; }
            var equals = value.IndexOf('=');
            if (equals > 0) entries[(section, value[..equals].Trim())] = value[(equals + 1)..].Trim();
        }
        Migrate((s, key, fallback) => entries.TryGetValue((s, key), out var value) ? value : fallback, legacyPath);
    }
    internal void Migrate(Func<string, string, string, string> readLegacy, string legacyPath)
    {
        lock (_gate)
        {
            Recover();
            if (Exists) return;
            if (File.Exists(legacyPath)) File.Copy(legacyPath, legacyPath + ".ai-migration-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak", false);
            var c = new AiConfiguration(); var keys = new Dictionary<string, string>();
            void Provider(string id, string url, string model, string protocol, string search = "none")
            {
                var key = readLegacy(id, "ApiKey", "").Trim();
                c.Providers[id] = new AiProvider
                {
                    Name = id, BaseUrl = StripEndpoint(StripEndpoint(url, "chat-completions"), protocol), Protocol = protocol,
                    Credential = new AiCredential { Mode = id == "Custom" && key.Length == 0 ? "none" : "stored", Reference = id },
                    ChatModels = new() { ["chat"] = new AiChatModel { Model = model, Name = model, Protocol = protocol, NativeSearch = search } }
                };
                if (key.Length > 0) keys[id] = key;
            }
            Provider("Gemini", "https://generativelanguage.googleapis.com/v1beta", readLegacy("Gemini", "Model", "gemini-3.5-flash"), "gemini", "google");
            Provider("Qwen", readLegacy("Qwen", "BaseUrl", "https://dashscope.aliyuncs.com/compatible-mode/v1"), readLegacy("Qwen", "Model", "qwen3.7-plus"), "responses", "dashscope");
            Provider("OpenAI", "https://api.openai.com/v1", readLegacy("OpenAI", "Model", "gpt-4.1-mini"), "chat-completions");
            Provider("DeepSeek", "https://api.deepseek.com", readLegacy("DeepSeek", "Model", "deepseek-chat"), "chat-completions");
            var customUrl = readLegacy("Custom", "BaseUrl", ""); var customModel = readLegacy("Custom", "Model", "");
            if (!string.IsNullOrWhiteSpace(customUrl) && !string.IsNullOrWhiteSpace(customModel)) Provider("Custom", customUrl, customModel, "chat-completions");
            AiAsrProvider Speech(string id)
            {
                var chatProvider = c.Providers[id];
                var stored = chatProvider.Credential.Mode == "stored";
                var reference = stored ? AsrCredentialReference(id) : chatProvider.Credential.Reference;
                var speechProvider = new AiAsrProvider
                {
                    Name = chatProvider.Name, BaseUrl = chatProvider.BaseUrl,
                    Credential = new AiCredential { Mode = chatProvider.Credential.Mode, Reference = reference }
                };
                c.AsrProviders[id] = speechProvider;
                if (stored && chatProvider.Credential.Reference != null && keys.TryGetValue(chatProvider.Credential.Reference, out var secret))
                    keys[reference!] = secret;
                return speechProvider;
            }
            var geminiSpeech = Speech("Gemini");
            geminiSpeech.Models["audio"] = new AiAsrModel { Model = c.Providers["Gemini"].ChatModels["chat"].Model, Name = "Gemini ASR", Protocol = "gemini-audio" };
            var q = c.Providers["Qwen"];
            var qwenSpeech = Speech("Qwen");
            qwenSpeech.Models["http"] = new AiAsrModel { Model = readLegacy("Qwen", "AsrModel", "qwen3-asr-flash"), Name = "HTTP ASR", Protocol = "qwen-http" };
            var realtime = readLegacy("Qwen", "RealtimeAsrModel", "paraformer-realtime-v2");
            qwenSpeech.Models["realtime"] = new AiAsrModel { Model = realtime, Name = "Realtime ASR", Protocol = "dashscope-realtime",
                Endpoint = q.BaseUrl.Contains("dashscope-intl.", StringComparison.OrdinalIgnoreCase) ? "wss://dashscope-intl.aliyuncs.com/api-ws/v1/inference" : "wss://dashscope.aliyuncs.com/api-ws/v1/inference" };
            var active = readLegacy("AI", "Provider", "Gemini");
            if (active.Equals("Tongyi", StringComparison.OrdinalIgnoreCase) || active.Equals("DashScope", StringComparison.OrdinalIgnoreCase)) active = "Qwen";
            var selected = c.Providers.Keys.FirstOrDefault(id => id.Equals(active, StringComparison.OrdinalIgnoreCase))
                ?? throw new AiConfigurationException("Legacy active provider is invalid or incomplete. Configure it before migration.");
            c.Chat = new() { ProviderId = selected, ModelId = "chat" };
            c.Asr = new() { ProviderId = selected == "Qwen" ? "Qwen" : "Gemini", ModelId = selected == "Qwen" ? "realtime" : "audio" };
            if (selected == "Qwen") c.AsrFallback = new() { ProviderId = "Qwen", ModelId = "http" };
            Save(c, keys);
        }
    }
    internal static void ApplyProviderProtocol(AiProvider p)
    {
        if (p.ChatModels == null) return;
        foreach (var m in p.ChatModels.Values)
            if (m != null && !string.IsNullOrWhiteSpace(m.Protocol)) m.Protocol = CanonicalChatProtocol(m.Protocol);
        if (!string.IsNullOrWhiteSpace(p.Protocol)) p.Protocol = CanonicalChatProtocol(p.Protocol);
        if (p.ChatModels.Count == 0) return;
        if (string.IsNullOrWhiteSpace(p.Protocol))
        {
            var distinct = p.ChatModels.Values.Where(m => m != null).Select(m => m!.Protocol).Distinct(StringComparer.Ordinal).ToArray();
            if (distinct.Length == 1) p.Protocol = distinct[0];
            return;
        }
        if (!ChatProtocols.Contains(p.Protocol)) return;
        foreach (var m in p.ChatModels.Values)
        {
            if (m == null) continue;
            m.Protocol = p.Protocol;
            m.MaxTokensField = p.Protocol == "chat-completions" ? p.MaxTokensField : null;
        }
    }
    internal static string StripEndpoint(string url, string protocol)
    {
        url = url.Trim().TrimEnd('/');
        var suffix = protocol switch { "responses" => "/responses", "chat-completions" => "/chat/completions", "anthropic" => "/messages", _ => "" };
        return suffix.Length > 0 && url.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? url[..^suffix.Length] : url;
    }
}

// A settings session owns one draft. Native tabs and cloned rows never bind to the game's archive.
internal sealed class AiSettingsDraft
{
    internal AiConfiguration Config { get; }
    internal Dictionary<string, string> Credentials { get; }
    internal AiSettingsDraft(AiConfigurationStore store) { (Config, Credentials) = store.Read(); }
    internal void Save(AiConfigurationStore store) => store.Save(Config, Credentials);
}

