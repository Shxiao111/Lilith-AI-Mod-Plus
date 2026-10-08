using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LilithTextInjector;

// Fake loopback services exercise production modules without Unity or real credentials.
internal static class Tests
{
    private static int passed;
    private static void Check(bool yes, string name) { if (!yes) throw new Exception(name); passed++; }
    private static async Task<T> Fails<T>(Func<Task> action) where T : Exception
    {
        try { await action(); } catch (T e) { passed++; return e; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static T Fails<T>(Action action) where T : Exception => Fails<T>(() => { action(); return Task.CompletedTask; }).GetAwaiter().GetResult();
    private static AiConfiguration Config(string url)
    {
        var c = new AiConfiguration { Chat = new() { ProviderId = "local", ModelId = "chat" }, Asr = new() { ProviderId = "local", ModelId = "audio" } };
        c.Providers["local"] = new AiProvider { Name = "Any provider", BaseUrl = url,
            ChatModels = new() { ["chat"] = new() { Model = "arbitrary-upstream", Name = "My model" }, ["another"] = new() { Model = "second" } } };
        c.AsrProviders["local"] = new AiAsrProvider { Name = "Any speech", BaseUrl = url,
            Models = new() { ["audio"] = new() { Model = "my-transcriber" } } };
        return c;
    }
    private static void StoreTests()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lilith-config-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        var store = new AiConfigurationStore(dir); var c = Config("http://localhost:12345/provider-version");
        c.AsrProviders["other"] = new AiAsrProvider { Name = "Independent", BaseUrl = "http://localhost:22222/api", Models = new() { ["a"] = new() { Model = "asr" } } };
        c.Asr = new() { ProviderId = "other", ModelId = "a" };
        var keys = new Dictionary<string, string> { ["my-key"] = "fake-secret" }; c.Providers["local"].Credential = new() { Mode = "stored", Reference = "my-key" };
        store.Save(c, keys);
        Check(!File.ReadAllText(store.ConfigPath).Contains("fake-secret"), "Main file contains no secret");
        var first = store.ChatSnapshot(); Check(first.Key == "fake-secret" && first.Model.Model == "arbitrary-upstream", "Stored credential/model resolved");
        var recording = store.RecordingSnapshot();
        Check(recording.Primary.ProviderId == "other" && recording.Primary.BaseUrl == "http://localhost:22222/api" && first.BaseUrl.Contains("12345"), "ASR independent of chat");
        var cancelled = new AiSettingsDraft(store); cancelled.Config.Providers["local"].ChatModels["chat"].Model = "discarded";
        Check(store.ChatSnapshot().Model.Model == "arbitrary-upstream", "Draft cancellation does not change disk");
        var draft = new AiSettingsDraft(store); draft.Config.Chat.ModelId = "another"; draft.Credentials["my-key"] = "new-fake-secret"; draft.Save(store);
        Check(store.ChatSnapshot().Model.Model == "second" && store.ChatSnapshot().Key == "new-fake-secret", "Single draft saves config and credentials");
        Check(first.Key == "fake-secret" && first.Model.Model == "arbitrary-upstream", "Operation snapshot unchanged after save");
        var saved = File.ReadAllText(store.ConfigPath); var savedKeys = File.ReadAllText(store.CredentialsPath);
        var attempts = 0; var broken = new AiConfigurationStore(dir, (temp, target) => { if (++attempts == 2) throw new IOException("simulated"); File.Replace(temp, target, null); });
        var failedDraft = new AiSettingsDraft(broken); failedDraft.Config.Providers["local"].Name = "Uncommitted"; failedDraft.Credentials["my-key"] = "failed-key";
        Fails<AiConfigurationException>(() => failedDraft.Save(broken));
        Check(File.ReadAllText(store.ConfigPath) == saved && File.ReadAllText(store.CredentialsPath) == savedKeys, "Second-file failure restores valid pair");
        Check(failedDraft.Config.Providers["local"].Name == "Uncommitted", "Failed save retains draft");
        failedDraft.Save(store); Check(store.Read().Config.Providers["local"].Name == "Uncommitted", "Retry draft works");
        // Simulate a process interruption after replacing credentials but before replacing the main config.
        var originals = new Dictionary<string,string?> { [store.ConfigPath] = saved, [store.CredentialsPath] = savedKeys };
        File.WriteAllText(Path.Combine(dir,"lilith-ai.transaction.json"), JsonSerializer.Serialize(originals)); File.WriteAllText(store.CredentialsPath,"{}");
        store.Read(); Check(File.ReadAllText(store.CredentialsPath) == savedKeys, "Interrupted commit recovered");
        var missing = new AiSettingsDraft(store); missing.Config.Providers.Remove("local"); Fails<AiConfigurationException>(() => missing.Save(store));
        Check(File.ReadAllText(store.ConfigPath) == saved, "Deleting active referenced provider cannot save");
        var env = store.Read(); env.Config.Providers["local"].Credential = new() { Mode = "env", Reference = "LILITH_TEST_FAKE_KEY" };
        Environment.SetEnvironmentVariable("LILITH_TEST_FAKE_KEY", "env-fake"); store.Save(env.Config, env.Credentials);
        Check(store.ChatSnapshot().Key == "env-fake", "Environment reference resolved"); Environment.SetEnvironmentVariable("LILITH_TEST_FAKE_KEY",null);
        Fails<AiConfigurationException>(() => store.ChatSnapshot());
        env.Config.Providers["local"].Credential.Mode = "none"; store.Save(env.Config, env.Credentials); Check(store.ChatSnapshot().Key == null, "No-auth explicit");
        env.Config.Providers["local"].Credential.Mode = "stored"; env.Config.Providers["local"].Credential.Reference = "missing"; store.Save(env.Config, env.Credentials);
        Fails<AiConfigurationException>(() => store.ChatSnapshot());
        File.WriteAllText(store.ConfigPath,"{broken"); var migrated = false;
        store.Migrate((s,k,d) => { migrated = true; return d; }, Path.Combine(dir,"old.cfg"));
        Check(!migrated, "Corrupt existing config must not remigrate"); Fails<AiConfigurationException>(() => store.Read());
        File.WriteAllText(store.ConfigPath,"{}"); Fails<AiConfigurationException>(() => store.Read());
        File.WriteAllText(store.ConfigPath,saved.Replace("\"modelId\": \"another\"", "\"modelId\": null")); Fails<AiConfigurationException>(() => store.Read());
        Console.WriteLine("PASS configuration, credentials, immutable reads, draft and transactional recovery");
    }
    private static void MigrationTests()
    {
        foreach (var active in new[]{"Qwen","OpenAI","Custom","Gemini"})
        {
            var dir = Path.Combine(Path.GetTempPath(),"lilith-migration-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
            var cfg = Path.Combine(dir,"legacy.cfg"); var legacy = $"[AI]\nProvider = {active}\n[Qwen]\nApiKey = fake-q\nModel = old-selected-q\nBaseUrl = https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions\nRealtimeAsrModel = old-realtime\nAsrModel = old-http\n[Custom]\nBaseUrl = http://localhost:1234/v1/chat/completions\nModel = local-chat\n[Gemini]\nModel = old-gemini\nApiKey = fake-g\n";
            File.WriteAllText(cfg,legacy); var store = new AiConfigurationStore(dir); store.Migrate(cfg); var (c, keys) = store.Read();
            Check(c.Chat.ProviderId == active, "Migration keeps selection");
            Check(File.ReadAllText(cfg) == legacy && Directory.GetFiles(dir,"*.bak").Length == 1, "Backup and legacy untouched");
            Check(c.Providers["Qwen"].BaseUrl.EndsWith("/v1") && c.Providers["Qwen"].ChatModels["chat"].NativeSearch == "dashscope", "Known endpoint stripped/search preserved");
            Check(c.Providers["Gemini"].Protocol == "gemini" && c.Providers["Qwen"].Protocol == "responses" && c.Providers["OpenAI"].Protocol == "chat-completions" && c.Providers["DeepSeek"].Protocol == "chat-completions" && c.Providers["Custom"].Protocol == "chat-completions", "Migrated providers keep one protocol");
            Check(c.Asr.ProviderId == (active == "Qwen" ? "Qwen" : "Gemini") && (c.AsrFallback != null) == (active == "Qwen"), "Legacy ASR selection and explicit fallback");
            Check(c.Providers["Gemini"].Credential.Reference == "Gemini" && c.AsrProviders["Gemini"].Credential.Reference == "asr:Gemini" && c.AsrProviders["Qwen"].BaseUrl == c.Providers["Qwen"].BaseUrl, "Speech connections copy address and use their own credential reference");
            Check(!c.AsrProviders.ContainsKey("OpenAI") && !c.AsrProviders.ContainsKey("DeepSeek") && !c.AsrProviders.ContainsKey("Custom"), "Only Gemini and Qwen speech connections are migrated");
            if (active == "Qwen") Check(store.RecordingSnapshot().Fallback!.Model.Model == "old-http" && store.RecordingSnapshot().Primary.Key == "fake-q" && keys["asr:Qwen"] == "fake-q", "Explicit old HTTP fallback model and copied Qwen speech key");
            if (active == "Custom") Check(store.ChatSnapshot().Key == null, "Legacy custom unauthenticated local connection");
            if (active != "Qwen") Check(store.RecordingSnapshot().Primary.Key == "fake-g", "Gemini speech uses the copied key");
            Check(keys["Gemini"] == "fake-g" && keys["asr:Gemini"] == "fake-g", "Migrated key stored separately");
            store.Migrate(cfg); Check(Directory.GetFiles(dir,"*.bak").Length == 1, "One-time migration only");
        }
        Check(AiConfigurationStore.StripEndpoint("https://p.example/api/messages", "anthropic") == "https://p.example/api", "Only known suffix stripped");
        Check(AiConfigurationStore.StripEndpoint("https://p.example/api/custom", "responses") == "https://p.example/api/custom", "No version path guessing");
        Console.WriteLine("PASS one-time legacy migration and selection preservation");
    }
    private static void ProviderProtocolTests()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lilith-provider-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        var store = new AiConfigurationStore(dir); var c = Config("http://localhost:12345/v1");
        Check(AiConfigurationStore.IsProviderId("my-gateway", true) && !AiConfigurationStore.IsProviderId("Gemini", true) && AiConfigurationStore.IsProviderId("Gemini", false), "New IDs are lowercase; migrated IDs still load");
        store.Save(c, new()); var saved = store.Read().Config;
        var savedFile = JsonNode.Parse(File.ReadAllText(store.ConfigPath))!;
        var savedModel = savedFile["providers"]!["local"]!["chatModels"]!["another"]!;
        Check(saved.Version == 2 && saved.Providers["local"].Protocol == "chat-completions" && savedModel["protocol"] == null && savedModel["maxTokensField"] == null, "Agreed model protocol is promoted onto the provider and not stored again");
        c.Providers["local"].Protocol = "openai-responses"; store.Save(c, new());
        Check(store.ChatSnapshot().Model.Protocol == "responses" && store.Read().Config.Providers["local"].Protocol == "responses", "Provider protocol canonicalizes and is copied onto the call snapshot");
        var mixed = store.Read().Config; mixed.Providers["local"].Protocol = ""; mixed.Providers["local"].ChatModels["chat"].Protocol = "gemini"; mixed.Providers["local"].ChatModels["another"].Protocol = "responses";
        Fails<AiConfigurationException>(() => store.Save(mixed, new()));
        var gated = store.Read().Config; gated.Providers["local"].Protocol = "chat-completions"; gated.Providers["local"].MaxTokensField = "max_tokens"; gated.Providers["local"].ChatModels["chat"].ContextWindow = 8192; store.Save(gated, new());
        var round = store.Read().Config.Providers["local"];
        var call = store.ChatSnapshot();
        Check(round.MaxTokensField == "max_tokens" && round.ChatModels["chat"].ContextWindow == 8192 && call.Model.MaxTokensField == "max_tokens" && call.Model.Protocol == "chat-completions", "Output field stays on the provider and is copied to the call");
        var generated = AiConfigurationStore.AllocateProviderId(new[] { "p-taken" });
        Check(AiConfigurationStore.AddChatModel(round, "added-model") && !AiConfigurationStore.AddChatModel(round, "added-model") && round.Protocol == "chat-completions" && AiConfigurationStore.IsProviderId(generated, true) && generated != "p-taken", "Catalog selection adds one draft model and new ids are generated");
        Check(AiModelCatalog.ListUri("chat-completions", "https://gateway.example/v1").AbsoluteUri == "https://gateway.example/v1/models", "Chat list keeps the version path");
        Check(AiModelCatalog.ListUri("anthropic", "https://gateway.example").AbsoluteUri == "https://gateway.example/v1/models?limit=1000", "Anthropic list adds v1");
        Check(AiModelCatalog.ListUri("anthropic-messages", "https://gateway.example/v1").AbsoluteUri == "https://gateway.example/v1/models?limit=1000", "Anthropic list keeps an existing v1");
        Check(AiModelCatalog.Parse("gemini", "{\"models\":[{\"name\":\"models/gemini-3.5-flash\"},{\"name\":\"models/gemini-3.5-flash\"}]}").SequenceEqual(new[] { "gemini-3.5-flash" }), "Gemini catalog ids are unprefixed and unique");
        Console.WriteLine("PASS provider protocol, permanent ID rule and model catalog parsing");
    }
    private static async Task CatalogTests()
    {
        using var http = new HttpClient();
        await using var server = new FakeServer();
        server.Replies.Enqueue((200, "{\"data\":[{\"id\":\"alpha\"},{\"id\":\"beta\"},{\"id\":\"alpha\"}]}"));
        var ids = await AiModelCatalog.FetchAsync(http, "openai-completions", server.Url + "v1", "fake-key", default);
        Check(ids.SequenceEqual(new[] { "alpha", "beta" }) && server.Requests[0].Path == "/v1/models" && server.Requests[0].Headers["Authorization"] == "Bearer fake-key", "OpenAI catalog auth and ids");
        server.Replies.Enqueue((200, "{\"data\":[{\"id\":\"claude-sonnet\"}]}"));
        ids = await AiModelCatalog.FetchAsync(http, "anthropic", server.Url.TrimEnd('/'), "fake-anthropic", default);
        Check(ids.SequenceEqual(new[] { "claude-sonnet" }) && server.Requests[1].Path == "/v1/models" && server.Requests[1].Headers["x-api-key"] == "fake-anthropic" && server.Requests[1].Headers["anthropic-version"] == "2023-06-01", "Anthropic catalog root and headers");
        server.Replies.Enqueue((200, "{\"data\":[]}"));
        await Fails<AiConfigurationException>(() => AiModelCatalog.FetchAsync(http, "chat-completions", server.Url + "v1", null, default));
        Console.WriteLine("PASS model catalog fetch");
    }
    private static JsonArray Tools() => new(new JsonObject { ["name"] = "allowed", ["description"] = "Only safe test tool", ["parameters"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["value"] = new JsonObject { ["type"] = "string" } } } });
    private static string Text(string protocol) => protocol switch
    {
        "gemini" => "{\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[{\"text\":\"hidden\",\"thought\":true},{\"text\":\"ok\"}]}}]}",
        "responses" => "{\"output\":[{\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"output_text\",\"text\":\"ok\"}]}]}",
        "anthropic" => "{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]}",
        _ => "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"ok\"}}]}"
    };
    private static string Tool(string protocol, string name = "allowed") => protocol switch
    {
        "gemini" => "{\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[{\"thoughtSignature\":\"preserve-signature\",\"functionCall\":{\"id\":\"call1\",\"name\":\""+name+"\",\"args\":{\"value\":\"safe\"}}}]}}]}",
        "responses" => "{\"output\":[{\"type\":\"reasoning\",\"id\":\"r1\",\"encrypted_content\":\"preserve-encrypted\",\"summary\":[]},{\"type\":\"function_call\",\"call_id\":\"call1\",\"name\":\""+name+"\",\"arguments\":\"{\\\"value\\\":\\\"safe\\\"}\"}]}",
        "anthropic" => "{\"content\":[{\"type\":\"thinking\",\"thinking\":\"hidden\",\"signature\":\"preserve-thinking\"},{\"type\":\"tool_use\",\"id\":\"call1\",\"name\":\""+name+"\",\"input\":{\"value\":\"safe\"}}]}",
        _ => "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":null,\"reasoning_content\":\"preserve-reasoning\",\"tool_calls\":[{\"type\":\"function\",\"id\":\"call1\",\"function\":{\"name\":\""+name+"\",\"arguments\":\"{\\\"value\\\":\\\"safe\\\"}\"}}]}}]}"
    };
    private static AiChatSnapshot Chat(FakeServer server, string protocol = "chat-completions", string tools = "auto") => new("arbitrary-brand", "local-id", server.Url + "version", "fake-key", new AiChatModel { Model = "any-model", Protocol = protocol, Tools = tools, MaxOutputTokens = protocol == "anthropic" ? 1024 : null });
    private static Task<IReadOnlyList<AiToolResult>> Execute(IReadOnlyList<AiToolCall> calls) => Task.FromResult<IReadOnlyList<AiToolResult>>(calls.Select(c => new AiToolResult { Name = c.Name, Id = c.Id, Success = true, Message = "safe-result" }).ToArray());
    private static Task<string> Request(AiChatClient client, AiChatSnapshot s, bool permission = true, Func<IReadOnlyList<AiToolCall>,Task<IReadOnlyList<AiToolResult>>>? execute = null, bool search = false)
        => client.ChatAsync(s,"persona-instruction", new[]{ new AiConversationTurn("user","hello") },Tools(), permission,search, execute ?? Execute);
    private static async Task ChatProtocolTests()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        foreach (var protocol in AiConfigurationStore.ChatProtocols)
        {
            await using var server = new FakeServer(); server.Replies.Enqueue((200, Tool(protocol))); server.Replies.Enqueue((200,Text(protocol)));
            var client = new AiChatClient(http); var s = Chat(server, protocol); var ran = 0;
            var result = await Request(client,s,execute: calls => { ran++; Check(calls[0].Args.GetProperty("value").GetString()=="safe", "Tool args parsed"); return Execute(calls); });
            Check(result == "ok" && ran == 1 && server.Requests.Count == 2,"Tool continuation " + protocol);
            Check(client.Capability(s).State == "supported", "Valid tool evidence");
            var req = server.Requests[0]; var body = JsonNode.Parse(req.Body)!;
            var expectedPath = protocol switch { "gemini" => "/version/models/any-model:generateContent", "responses" => "/version/responses", "anthropic" => "/version/messages", _ => "/version/chat/completions" };
            Check(req.Path == expectedPath, "Exact suffix/no v1 inference " + protocol);
            Check(protocol == "gemini" ? req.Headers["x-goog-api-key"] == "fake-key" : protocol == "anthropic" ? req.Headers["x-api-key"] == "fake-key" && req.Headers["anthropic-version"] == "2023-06-01" : req.Headers["Authorization"] == "Bearer fake-key", "Protocol authentication " + protocol);
            Check(body["tools"] != null && req.Body.Contains("persona-instruction"), "Persona and tools " + protocol);
            Check(body["temperature"] == null && body["generationConfig"] == null && body["max_completion_tokens"] == null && body["max_output_tokens"] == null, "Unspecified generation fields omitted");
            var continuation = server.Requests[1].Body;
            Check(continuation.Contains("call1") && continuation.Contains("safe-result"), "Tool ID/result replay " + protocol);
            Check(continuation.Contains(protocol switch { "gemini" => "preserve-signature", "responses" => "preserve-encrypted", "anthropic" => "preserve-thinking", _ => "preserve-reasoning" }), "Native reasoning replay " + protocol);
            var changed = s with { BaseUrl = s.BaseUrl + "/new" }; Check(client.Capability(changed).State == "unknown" && client.Capability(s).State == "unknown", "Address change invalidates rather than resurrects evidence");
            client.InvalidateCapabilities();
            server.Replies.Enqueue((200,Text(protocol))); await Request(client,s); Check(client.Capability(s).State == "unknown", "Plain text is not unsupported");
        }
        foreach (var protocol in AiConfigurationStore.ChatProtocols)
        {
            await using var server = new FakeServer(); var s = Chat(server,protocol); s.Model.Temperature = .25; s.Model.MaxOutputTokens = 2048;
            if (protocol == "anthropic" || protocol == "gemini") s.Model.ThinkingBudget = 1024; else s.Model.ReasoningEffort = "medium";
            server.Replies.Enqueue((200,Text(protocol))); await Request(new(http),s); var body = server.Requests[0].Body;
            Check(body.Contains("2048") && body.Contains("0.25") && body.Contains(protocol == "gemini" ? "thinkingBudget" : protocol == "anthropic" ? "budget_tokens" : "effort"), "Optional protocol parameters " + protocol);
        }
        await using (var server = new FakeServer())
        {
            var s = Chat(server); s.Model.MaxOutputTokens = 2048; s.Model.MaxTokensField = "max_tokens"; s.Model.ReasoningEffort = "off";
            server.Replies.Enqueue((200, Text("chat-completions"))); await Request(new(http), s); var body = server.Requests[0].Body;
            Check(body.Contains("\"max_tokens\":2048") && !body.Contains("max_completion_tokens") && !body.Contains("reasoning_effort"), "Gateway output field and omitted off effort");
        }
        await using (var server = new FakeServer())
        {
            var s = Chat(server,"gemini","off"); s.Model.NativeSearch = "google"; server.Replies.Enqueue((200,Text("gemini"))); await Request(new(http),s,permission:false,search:true);
            Check(server.Requests[0].Body.Contains("googleSearch") && !server.Requests[0].Body.Contains("functionDeclarations"), "Native web search independent of local tools");
        }
        Console.WriteLine("PASS four chat protocols, auth, parameters and native tool-result replay");
    }
    private const string Reject = "{\"error\":{\"param\":\"tools\",\"code\":\"unsupported_parameter\",\"message\":\"fake-secret echoed by service\"}}";
    private static async Task CapabilityTests()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        await using (var server = new FakeServer())
        {
            server.Replies.Enqueue((400,Reject)); server.Replies.Enqueue((200,Text("chat-completions"))); var client = new AiChatClient(http); var s = Chat(server);
            Check(await Request(client,s) == "ok" && server.Requests.Count == 2, "Auto retries exactly once");
            Check(JsonNode.Parse(server.Requests[0].Body)!["tools"] != null && JsonNode.Parse(server.Requests[1].Body)!["tools"] == null, "Retry removes local tools");
            Check(client.Capability(s).State == "unsupported", "Explicit rejection cached");
            server.Replies.Enqueue((200,Text("chat-completions"))); await Request(client,s); Check(JsonNode.Parse(server.Requests[2].Body)!["tools"] == null, "Cached unsupported is pure chat");
            Check(client.Capability(s with { Key = "different-fake" }).State == "unknown", "Credential change invalidates");
        }
        foreach (var code in new[]{400,401,403,429,500})
        {
            await using var server = new FakeServer(); server.Replies.Enqueue((code, code == 400 ? "{\"error\":{\"param\":\"temperature\",\"code\":\"unsupported_parameter\",\"message\":\"unsupported temperature fake-secret\"}}" : Reject));
            var client = new AiChatClient(http); var s = Chat(server); var e = await Fails<AiServiceException>(() => Request(client,s));
            Check(server.Requests.Count == 1 && client.Capability(s).State == "unknown", "No downgrade for auth/rate/general parameters");
            Check(!e.Message.Contains("fake-secret"), "Safe non-reflective error");
        }
        await using (var server = new FakeServer())
        {
            server.Replies.Enqueue((400,Reject)); var client = new AiChatClient(http); var s = Chat(server,tools:"on"); await Fails<AiServiceException>(() => Request(client,s)); Check(server.Requests.Count == 1,"Tools on rejection errors without downgrade");
        }
        foreach (var permission in new[]{true,false})
        {
            await using var server = new FakeServer(); server.Replies.Enqueue((200,Text("chat-completions"))); await Request(new(http),Chat(server,tools: permission ? "off" : "on"),permission);
            Check(JsonNode.Parse(server.Requests[0].Body)!["tools"] == null,"Off or missing permission never sends tools");
        }
        await using (var server = new FakeServer())
        {
            server.Replies.Enqueue((200,Tool("chat-completions"))); server.Replies.Enqueue((400,Reject)); var ran = 0;
            await Fails<AiServiceException>(() => Request(new(http),Chat(server),execute: c => { ran++; return Execute(c); })); Check(ran == 1 && server.Requests.Count == 2,"No retry after executing a tool");
        }
        await using (var server = new FakeServer())
        {
            for (var i=0;i<5;i++) server.Replies.Enqueue((200,Tool("responses"))); var ran = 0;
            await Fails<AiConfigurationException>(() => Request(new(http),Chat(server,"responses"),execute: c => { ran++; return Execute(c); })); Check(ran == 4,"Four-round limit");
        }
        await using (var server = new FakeServer())
        {
            server.Replies.Enqueue((200,Tool("anthropic","unlisted"))); server.Replies.Enqueue((200,Text("anthropic"))); var ran = 0;
            await Request(new(http),Chat(server,"anthropic"),execute: c => { ran++; return Execute(c); }); Check(ran == 0 && server.Requests[1].Body.Contains("not allowlisted"),"Whitelist does not execute unknown tool");
        }
        await using (var server = new FakeServer())
        {
            server.Delay = TimeSpan.FromSeconds(3); server.Replies.Enqueue((200,Text("gemini"))); var client = new AiChatClient(http); var s = Chat(server,"gemini"); await Fails<TaskCanceledException>(() => Request(client,s)); Check(server.Requests.Count == 1 && client.Capability(s).State == "unknown", "Timeout is not capability rejection");
        }
        Console.WriteLine("PASS permission gating, safe auto downgrade, capability invalidation and round limit");
    }
    private static async Task AsrTests()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) }; var client = new AiAsrClient(http); var wav = Encoding.ASCII.GetBytes("RIFF-fake-wave-audio");
        foreach (var protocol in new[]{"gemini-audio","qwen-http","audio-transcriptions"})
        {
            await using var server = new FakeServer(); server.Replies.Enqueue((200,protocol == "gemini-audio" ? "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"recognized\"}]}}]}" : protocol == "qwen-http" ? "{\"choices\":[{\"message\":{\"content\":\"recognized\"}}]}" : "{\"text\":\"recognized\"}"));
            var s = new AiAsrSnapshot("independent-asr",server.Url + "v-custom","fake-asr",new() { Model = "selected-asr", Protocol = protocol });
            Check(await client.TranscribeAsync(s,wav,"ja","Japanese") == "recognized","ASR parse " + protocol); var req = server.Requests[0];
            Check(req.Path == "/v-custom" + (protocol == "gemini-audio" ? "/models/selected-asr:generateContent" : protocol == "qwen-http" ? "/chat/completions" : "/audio/transcriptions"),"ASR exact endpoint");
            if (protocol == "audio-transcriptions") Check(req.Body.Contains("selected-asr") && req.Body.Contains("ja") && req.Body.Contains("recording.wav") && req.Body.Contains("RIFF-fake-wave-audio"),"Multipart model/language/audio file");
            else Check(req.Body.Contains(Convert.ToBase64String(wav)) && req.Body.Contains(protocol == "gemini-audio" ? "Japanese" : "ja"),"ASR audio and captured language");
        }
        await using (var server = new FakeServer())
        {
            server.Replies.Enqueue((200,"{\"text\":\"override\"}")); var s = new AiAsrSnapshot("asr", "http://localhost:1/unreachable", null, new() { Model = "asr", Endpoint = server.Url + "custom" });
            Check(await client.TranscribeAsync(s,wav,"","") == "override" && server.Requests[0].Path == "/custom", "Explicit endpoint override");
        }
        await using (var server = new FakeServer())
        {
            var s = new AiAsrSnapshot("rt",server.Url,"fake-ws",new() { Model = "chosen-rt", Protocol = "dashscope-realtime", Endpoint = server.Url.Replace("http:","ws:") + "realtime" });
            var queue = new Queue<byte[]>(); queue.Enqueue(new byte[]{1,2,3,4});
            Check(await client.RealtimeAsync(s,"ja", () => queue.TryDequeue(out var a) ? a : null, () => queue.Count == 0, default) == "recognized realtime", "WebSocket ASR transcript");
            Check(server.WsAudioBytes == 4 && server.WsStart!.Contains("chosen-rt") && server.WsStart.Contains("ja") && server.WsAuth == "Bearer fake-ws", "Realtime model/language/auth/PCM");
        }
        await using (var server = new FakeServer())
        {
            server.WsFail = true; server.Replies.Enqueue((200,"{\"text\":\"explicit fallback\"}")); var primary = new AiAsrSnapshot("rt",server.Url,null,new() { Model = "chosen-rt", Protocol = "dashscope-realtime", Endpoint = server.Url.Replace("http:","ws:") + "rt" });
            var fallback = new AiAsrSnapshot("other",server.Url + "fallback",null,new() { Model = "explicit-fallback" }); var snapshot = new AiRecordingSnapshot(primary,fallback);
            var realtime = client.RealtimeAsync(primary,"en",()=>null,()=>true,default);
            Check(await client.CompleteRealtimeAsync(realtime,snapshot,wav,"en","") == "explicit fallback", "Real websocket failure uses explicit HTTP fallback");
            Check(server.Requests[0].Path == "/fallback/audio/transcriptions" && server.Requests[0].Body.Contains("explicit-fallback"), "No hardcoded fallback endpoint/model");
            await Fails<AiConfigurationException>(() => client.CompleteRealtimeAsync(Task.FromException<string>(new AiConfigurationException("failed")),new(primary,null),wav,"en",""));
            Check(server.Requests.Count == 1, "No fallback configured means no HTTP request");
        }
        Console.WriteLine("PASS four ASR protocols, websocket lifecycle and explicit fallback");
    }
    private static void SplitTests()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lilith-split-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        var store = new AiConfigurationStore(dir);
        File.WriteAllText(store.ConfigPath, @"{
  ""version"": 1,
  ""providers"": {
    ""local"": {
      ""name"": ""Local"",
      ""baseUrl"": ""http://localhost:1234/v1"",
      ""protocol"": ""chat-completions"",
      ""credential"": { ""mode"": ""stored"", ""reference"": ""my-key"" },
      ""chatModels"": { ""chat"": { ""model"": ""chat-model"", ""name"": ""Chat"", ""protocol"": ""chat-completions"", ""tools"": ""auto"" } },
      ""asrModels"": { ""audio"": { ""model"": ""asr-model"", ""name"": ""Speech"", ""protocol"": ""audio-transcriptions"" } }
    },
    ""chatonly"": {
      ""name"": ""Chat only"",
      ""baseUrl"": ""http://localhost:9/v1"",
      ""protocol"": ""chat-completions"",
      ""credential"": { ""mode"": ""none"" },
      ""chatModels"": { ""chat"": { ""model"": ""m"", ""name"": ""M"", ""protocol"": ""chat-completions"", ""tools"": ""auto"" } },
      ""asrModels"": {}
    },
    ""speechbox"": {
      ""name"": ""Speech only"",
      ""baseUrl"": ""http://localhost:8/v1"",
      ""credential"": { ""mode"": ""stored"", ""reference"": ""speech-key"" },
      ""chatModels"": {},
      ""asrModels"": { ""a"": { ""model"": ""box"", ""name"": ""Box"", ""protocol"": ""qwen-http"" } }
    }
  },
  ""chat"": { ""providerId"": ""local"", ""modelId"": ""chat"" },
  ""asr"": { ""providerId"": ""local"", ""modelId"": ""audio"" }
}");
        File.WriteAllText(store.CredentialsPath, "{\"my-key\":\"fake-secret\",\"speech-key\":\"fake-speech\"}");
        var disk = File.ReadAllText(store.ConfigPath); var diskKeys = File.ReadAllText(store.CredentialsPath);
        var (split, splitKeys) = store.Read();
        Check(split.Providers.ContainsKey("local") && split.Providers.ContainsKey("chatonly") && !split.Providers.ContainsKey("speechbox"), "Speech-only connections leave the chat list");
        Check(split.AsrProviders.ContainsKey("local") && split.AsrProviders.ContainsKey("speechbox") && !split.AsrProviders.ContainsKey("chatonly"), "Speech models move to asrProviders");
        Check(split.AsrProviders["local"].Models["audio"].Model == "asr-model" && split.AsrProviders["local"].BaseUrl == "http://localhost:1234/v1", "Speech address and model are copied");
        Check(split.Providers["local"].Credential.Reference == "my-key" && split.AsrProviders["local"].Credential.Reference == "asr:local", "Chat credential reference stays");
        Check(split.AsrProviders["speechbox"].Credential.Reference == "asr:speechbox", "Speech-only credential reference is namespaced");
        Check(splitKeys["my-key"] == "fake-secret" && splitKeys["asr:local"] == "fake-secret" && splitKeys["speech-key"] == "fake-speech" && splitKeys["asr:speechbox"] == "fake-speech", "Stored speech keys are copied");
        Check(File.ReadAllText(store.ConfigPath) == disk && File.ReadAllText(store.CredentialsPath) == diskKeys, "Split stays in memory until save");
        Check(store.ChatSnapshot().Key == "fake-secret" && store.RecordingSnapshot().Primary.Key == "fake-secret" && store.RecordingSnapshot().Primary.Model.Model == "asr-model", "Unsaved split resolves both keys");
        store.Save(split, splitKeys);
        var draft = new AiSettingsDraft(store); draft.Credentials["asr:local"] = "speech-only-secret"; draft.Save(store);
        var saved = store.Read();
        Check(saved.Credentials["my-key"] == "fake-secret" && saved.Credentials["asr:local"] == "speech-only-secret", "Speech key edits leave the chat key");
        Check(saved.Config.Providers.ContainsKey("local") && !saved.Config.Providers.ContainsKey("speechbox") && saved.Config.AsrProviders["speechbox"].Models["a"].Model == "box", "Saved split is not imported again");
        var keptDir = Path.Combine(Path.GetTempPath(), "lilith-split-kept-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(keptDir);
        var kept = new AiConfigurationStore(keptDir);
        File.WriteAllText(kept.ConfigPath, @"{
  ""version"": 1,
  ""providers"": {
    ""local"": {
      ""name"": ""Local"",
      ""baseUrl"": ""http://localhost:1234/v1"",
      ""protocol"": ""chat-completions"",
      ""credential"": { ""mode"": ""none"" },
      ""chatModels"": { ""chat"": { ""model"": ""chat-model"", ""protocol"": ""chat-completions"", ""tools"": ""auto"" } },
      ""asrModels"": { ""audio"": { ""model"": ""should-not-return"", ""protocol"": ""audio-transcriptions"" } }
    }
  },
  ""asrProviders"": {
    ""local"": {
      ""name"": ""Speech"",
      ""baseUrl"": ""http://localhost:9/v1"",
      ""credential"": { ""mode"": ""none"" },
      ""models"": { ""audio"": { ""model"": ""kept"", ""protocol"": ""audio-transcriptions"" } }
    }
  },
  ""chat"": { ""providerId"": ""local"", ""modelId"": ""chat"" },
  ""asr"": { ""providerId"": ""local"", ""modelId"": ""audio"" }
}");
        var existing = kept.Read().Config;
        Check(existing.AsrProviders["local"].Models["audio"].Model == "kept" && existing.AsrProviders["local"].BaseUrl == "http://localhost:9/v1", "Existing asrProviders are not rebuilt from leftover asrModels");
        var frozenDir = Path.Combine(Path.GetTempPath(), "lilith-split-v2-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(frozenDir);
        var frozen = new AiConfigurationStore(frozenDir);
        File.WriteAllText(frozen.ConfigPath, @"{
  ""version"": 2,
  ""providers"": {
    ""local"": {
      ""name"": ""Local"",
      ""baseUrl"": ""http://localhost:1234/v1"",
      ""protocol"": ""chat-completions"",
      ""credential"": { ""mode"": ""none"" },
      ""chatModels"": { ""chat"": { ""model"": ""chat-model"", ""tools"": ""auto"" } },
      ""asrModels"": { ""audio"": { ""model"": ""should-not-return"", ""protocol"": ""audio-transcriptions"" } }
    }
  },
  ""chat"": { ""providerId"": ""local"", ""modelId"": ""chat"" },
  ""asr"": { ""providerId"": ""local"", ""modelId"": ""audio"" }
}");
        Fails<AiConfigurationException>(() => frozen.Read());
        Console.WriteLine("PASS independent speech providers and old-file split");
    }
    public static async Task Main()
    {
        StoreTests(); MigrationTests(); ProviderProtocolTests(); SplitTests(); await ChatProtocolTests(); await CapabilityTests(); await AsrTests(); await CatalogTests(); Console.WriteLine($"PASS {passed} assertions (loopback services; fake keys only)");
    }
}
internal sealed record ObservedRequest(string Path, string Body, System.Collections.Specialized.NameValueCollection Headers);
internal sealed class FakeServer : IAsyncDisposable
{
    private readonly HttpListener listener = new(); private readonly CancellationTokenSource stop = new(); private readonly Task loop;
    internal readonly Queue<(int Status,string Body)> Replies = new(); internal readonly List<ObservedRequest> Requests = new();
    internal string Url { get; } internal TimeSpan Delay; internal bool WsFail; internal string? WsStart; internal string? WsAuth; internal int WsAudioBytes;
    internal FakeServer()
    {
        var probe = new TcpListener(IPAddress.Loopback,0); probe.Start(); var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        Url = $"http://127.0.0.1:{port}/"; listener.Prefixes.Add(Url); listener.Start(); loop = Loop();
    }
    private async Task Loop()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var context = await listener.GetContextAsync().WaitAsync(stop.Token);
                if (context.Request.IsWebSocketRequest) { await Ws(context); continue; }
                using var reader = new StreamReader(context.Request.InputStream); var body = await reader.ReadToEndAsync();
                Requests.Add(new(context.Request.Url!.AbsolutePath,body,context.Request.Headers));
                var reply = Replies.Count > 0 ? Replies.Dequeue() : (500,"{\"error\":{\"message\":\"missing fake response\"}}");
                if (Delay > TimeSpan.Zero) await Task.Delay(Delay, stop.Token);
                context.Response.StatusCode = reply.Item1; context.Response.ContentType = "application/json";
                var bytes = Encoding.UTF8.GetBytes(reply.Item2); context.Response.ContentLength64 = bytes.Length; await context.Response.OutputStream.WriteAsync(bytes); context.Response.Close();
            }
        }
        catch (Exception) when (stop.IsCancellationRequested) { }
    }
    private async Task Ws(HttpListenerContext context)
    {
        WsAuth = context.Request.Headers["Authorization"]; var accepted = await context.AcceptWebSocketAsync(null); using var socket = accepted.WebSocket;
        var buffer = new byte[32768]; var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer),stop.Token); WsStart = Encoding.UTF8.GetString(buffer,0,result.Count);
        async Task Send(string json) => await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)),WebSocketMessageType.Text,true,stop.Token);
        async Task Finish()
        {
            // Do not dispose immediately after sending: an abrupt close can discard the last
            // event on HttpListener's websocket. Keep the connection until the client finishes.
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", stop.Token);
            try { await socket.ReceiveAsync(new ArraySegment<byte>(buffer), stop.Token); }
            catch (WebSocketException) { /* The production client aborts after its final event. */ }
        }
        if (WsFail) { await Send("{\"header\":{\"event\":\"task-failed\"},\"payload\":{\"message\":\"fake-secret server echo\"}}"); await Finish(); return; }
        await Send("{\"header\":{\"event\":\"task-started\"}}");
        while (true)
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer),stop.Token);
            if (result.MessageType == WebSocketMessageType.Binary) { WsAudioBytes += result.Count; continue; }
            if (Encoding.UTF8.GetString(buffer,0,result.Count).Contains("finish-task")) break;
        }
        await Send("{\"header\":{\"event\":\"result-generated\"},\"payload\":{\"output\":{\"sentence\":{\"text\":\"recognized realtime\",\"sentence_end\":true}}}}");
        await Send("{\"header\":{\"event\":\"task-finished\"}}");
        await Finish();
    }
    public async ValueTask DisposeAsync() { stop.Cancel(); listener.Close(); try { await loop; } catch (IOException) { } catch (WebSocketException) { } stop.Dispose(); }
}
