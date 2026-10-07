using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LilithTextInjector;

internal sealed class AiToolCall
{
    public string Name { get; set; } = "";
    public string Id { get; set; } = "";
    public JsonElement Args { get; set; }
}
internal sealed class AiToolResult
{
    public string Name { get; set; } = "";
    public string Id { get; set; } = "";
    public bool Success { get; set; }
    public string Message { get; set; } = "";
}
internal sealed record AiConversationTurn(string Role, string Text);
internal sealed record AiToolCapability(string State, string Reason);
// Service error bodies stay inside the transport boundary; only a fixed, non-reflective summary escapes.
internal sealed class AiServiceException : Exception
{
    internal int Status { get; }
    internal bool RejectsTools { get; }
    internal AiServiceException(int status, bool rejectsTools = false)
        : base($"AI service HTTP {status}" + (rejectsTools ? ": local tool parameters are unsupported." : "."))
    { Status = status; RejectsTools = rejectsTools; }
}

// Stateless protocol requests plus runtime-only capability evidence. No provider-name or model-name routing.
internal sealed class AiChatClient
{
    private readonly HttpClient _http;
    private readonly ConcurrentDictionary<(string, string), (string Signature, AiToolCapability Capability)> _capabilities = new();
    internal AiChatClient(HttpClient http) { _http = http; }
    internal AiToolCapability Capability(AiChatSnapshot s)
    {
        var key = (s.ProviderId, s.ModelId); var signature = Signature(s);
        if (_capabilities.TryGetValue(key, out var c) && c.Signature == signature) return c.Capability;
        _capabilities[key] = (signature, new("unknown", ""));
        return new("unknown", "");
    }
    private void RecordCapability(AiChatSnapshot s, string state, string reason)
        => _capabilities[(s.ProviderId, s.ModelId)] = (Signature(s), new(state, reason));
    private static string Signature(AiChatSnapshot s) => JsonSerializer.Serialize(new { s.ProviderId, s.BaseUrl, s.Key, s.Model.Model, s.Model.Protocol });
    internal void InvalidateCapabilities() => _capabilities.Clear();
    internal async Task<string> ChatAsync(AiChatSnapshot s, string instruction, IReadOnlyList<AiConversationTurn> history,
        JsonArray declarations, bool permission, bool searchRequested, Func<IReadOnlyList<AiToolCall>, Task<IReadOnlyList<AiToolResult>>> execute,
        CancellationToken cancellation = default)
    {
        var allowTools = permission && s.Model.Tools != "off"
            && (s.Model.Tools == "on" || Capability(s).State != "unsupported");
        var state = InitialMessages(s.Model.Protocol, history);
        var rounds = 0; var executed = false; var retried = false;
        while (true)
        {
            JsonObject response;
            try
            {
                response = await SendAsync(s, BuildBody(s.Model, instruction, state, declarations, allowTools, searchRequested), cancellation).ConfigureAwait(false);
            }
            catch (AiServiceException e) when (e.RejectsTools && allowTools)
            {
                RecordCapability(s, "unsupported", "Service explicitly rejected local tool parameters.");
                if (s.Model.Tools != "auto" || executed || retried) throw;
                retried = true; allowTools = false;
                continue;
            }
            var calls = new List<AiToolCall>(); var text = new StringBuilder();
            ParseResponse(s.Model.Protocol, response, state, calls, text);
            if (calls.Count == 0) return text.ToString();
            if (!allowTools) throw new AiConfigurationException("Service returned local tool calls while local tools were disabled.");
            RecordCapability(s, "supported", "A valid local tool call was returned.");
            if (rounds++ >= 4) throw new AiConfigurationException("The four-round local tool limit was reached.");
            var permitted = new List<AiToolCall>(); var results = new List<AiToolResult>();
            foreach (var call in calls)
            {
                if (permitted.Count < 8 && declarations.Any(d => d?["name"]?.GetValue<string>() == call.Name)) permitted.Add(call);
                else results.Add(new AiToolResult { Name = call.Name, Id = call.Id, Message = "Tool is not allowlisted or this turn exceeds eight actions." });
            }
            if (permitted.Count > 0)
            {
                executed = true;
                results.AddRange(await execute(permitted).ConfigureAwait(false));
            }
            AppendResults(s.Model.Protocol, state, results);
        }
    }
    private static JsonArray InitialMessages(string protocol, IReadOnlyList<AiConversationTurn> history)
    {
        var state = new JsonArray();
        foreach (var t in history)
        {
            var role = t.Role == "model" || t.Role == "assistant" ? "assistant" : "user";
            state.Add(protocol == "gemini"
                ? new JsonObject { ["role"] = role == "assistant" ? "model" : "user", ["parts"] = new JsonArray(new JsonObject { ["text"] = t.Text }) }
                : new JsonObject { ["role"] = role, ["content"] = t.Text });
        }
        return state;
    }
    private static bool SendReasoning(string? effort) => !string.IsNullOrEmpty(effort) && effort != "off";
    private static JsonNode Clone(JsonNode node) => JsonNode.Parse(node.ToJsonString())!;
    private static JsonObject BuildBody(AiChatModel m, string instruction, JsonArray state, JsonArray declarations, bool tools, bool searchRequested)
    {
        var body = new JsonObject();
        var schemas = new JsonArray();
        if (tools)
        {
            foreach (var d in declarations)
            {
                if (m.Protocol == "gemini") schemas.Add(Clone(d!));
                else if (m.Protocol == "anthropic") schemas.Add(new JsonObject { ["name"] = d!["name"]!.GetValue<string>(), ["description"] = d["description"]!.GetValue<string>(), ["input_schema"] = Clone(d["parameters"]!) });
                else
                {
                    var f = new JsonObject { ["name"] = d!["name"]!.GetValue<string>(), ["description"] = d["description"]!.GetValue<string>(), ["parameters"] = Clone(d["parameters"]!) };
                    if (m.Protocol == "responses") { f["type"] = "function"; schemas.Add(f); }
                    else schemas.Add(new JsonObject { ["type"] = "function", ["function"] = f });
                }
            }
        }
        if (m.Protocol == "gemini")
        {
            body["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = instruction }) };
            body["contents"] = Clone(state);
            var generation = new JsonObject();
            if (m.MaxOutputTokens.HasValue) generation["maxOutputTokens"] = m.MaxOutputTokens.Value;
            if (m.Temperature.HasValue) generation["temperature"] = m.Temperature.Value;
            if (m.ThinkingBudget.HasValue || m.ThinkingLevel != null)
            {
                var thinking = new JsonObject();
                if (m.ThinkingBudget.HasValue) thinking["thinkingBudget"] = m.ThinkingBudget.Value;
                if (m.ThinkingLevel != null) thinking["thinkingLevel"] = m.ThinkingLevel;
                generation["thinkingConfig"] = thinking;
            }
            if (generation.Count > 0) body["generationConfig"] = generation;
            var geminiTools = new JsonArray();
            if (tools) geminiTools.Add(new JsonObject { ["functionDeclarations"] = schemas });
            if (searchRequested && m.NativeSearch == "google") geminiTools.Add(new JsonObject { ["googleSearch"] = new JsonObject() });
            if (geminiTools.Count > 0) body["tools"] = geminiTools;
            return body;
        }
        body["model"] = m.Model;
        if (m.Protocol == "responses")
        {
            body["instructions"] = instruction; body["input"] = Clone(state); body["store"] = false;
            // Replay encrypted reasoning items without server-side conversation storage.
            if (m.NativeSearch != "dashscope") body["include"] = new JsonArray("reasoning.encrypted_content");
            if (m.MaxOutputTokens.HasValue) body["max_output_tokens"] = m.MaxOutputTokens.Value;
            if (SendReasoning(m.ReasoningEffort)) body["reasoning"] = new JsonObject { ["effort"] = m.ReasoningEffort };
            if (m.NativeSearch == "dashscope")
            {
                schemas.Add(new JsonObject { ["type"] = "web_search" });
            }
        }
        else if (m.Protocol == "anthropic")
        {
            if (m.MaxOutputTokens == null) throw new AiConfigurationException("Anthropic Messages requires an explicit maxOutputTokens value.");
            body["system"] = instruction; body["messages"] = Clone(state); body["max_tokens"] = m.MaxOutputTokens.Value;
            if (m.ThinkingBudget.HasValue) body["thinking"] = new JsonObject { ["type"] = "enabled", ["budget_tokens"] = m.ThinkingBudget.Value };
        }
        else
        {
            var messages = new JsonArray(new JsonObject { ["role"] = "system", ["content"] = instruction });
            foreach (var item in state) messages.Add(Clone(item!));
            body["messages"] = messages;
            if (m.MaxOutputTokens.HasValue) body[m.MaxTokensField == "max_tokens" ? "max_tokens" : "max_completion_tokens"] = m.MaxOutputTokens.Value;
            if (SendReasoning(m.ReasoningEffort)) body["reasoning_effort"] = m.ReasoningEffort;
        }
        if (m.Temperature.HasValue) body["temperature"] = m.Temperature.Value;
        if (schemas.Count > 0) body["tools"] = schemas;
        return body;
    }
    private async Task<JsonObject> SendAsync(AiChatSnapshot s, JsonObject body, CancellationToken cancellation)
    {
        var path = s.Model.Protocol switch
        {
            "chat-completions" => "/chat/completions", "responses" => "/responses", "anthropic" => "/messages",
            "gemini" => "/models/" + Uri.EscapeDataString(s.Model.Model) + ":generateContent",
            _ => throw new AiConfigurationException("Unknown chat protocol.")
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, s.BaseUrl.TrimEnd('/') + path);
        Authenticate(request, s.Model.Protocol, s.Key);
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, cancellation).ConfigureAwait(false);
        var raw = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new AiServiceException((int)response.StatusCode, ExplicitToolRejection((int)response.StatusCode, raw));
        try { return JsonNode.Parse(raw) as JsonObject ?? throw new JsonException(); }
        catch (JsonException) { throw new AiConfigurationException("AI service returned an invalid JSON response."); }
    }
    internal static void Authenticate(HttpRequestMessage request, string protocol, string? key)
    {
        if (protocol == "anthropic") request.Headers.Add("anthropic-version", "2023-06-01");
        if (key == null) return;
        if (protocol == "gemini" || protocol == "gemini-audio") request.Headers.Add("x-goog-api-key", key);
        else if (protocol == "anthropic") request.Headers.Add("x-api-key", key);
        else request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
    }
    private static bool ExplicitToolRejection(int status, string raw)
    {
        if (status != 400 && status != 422) return false;
        try
        {
            var error = JsonNode.Parse(raw)?["error"];
            var message = error?["message"]?.GetValue<string>() ?? "";
            var param = error?["param"]?.GetValue<string>() ?? "";
            var code = error?["code"]?.GetValue<string>() ?? "";
            if ((param == "tools" || param == "tool_choice" || param.StartsWith("tools.", StringComparison.Ordinal))
                && (code == "unsupported_parameter" || code == "unknown_parameter")) return true;
            return Regex.IsMatch(message, @"\b(tools|tool_choice|function[ _-]?calling|functionDeclarations)\b.{0,100}\b(not supported|unsupported|not available|not allowed|unknown parameter)\b|\b(unsupported|unknown) (parameter|field)[: '""\s]+(tools|tool_choice|functionDeclarations)\b", RegexOptions.IgnoreCase);
        }
        catch { return false; }
    }
    private static void AddCall(List<AiToolCall> calls, string name, string id, JsonNode? args)
    {
        if (string.IsNullOrWhiteSpace(name) || args is not JsonObject) throw new AiConfigurationException("AI service returned malformed tool arguments.");
        calls.Add(new AiToolCall { Name = name, Id = id, Args = JsonSerializer.SerializeToElement(args) });
    }
    private static void ParseResponse(string protocol, JsonObject response, JsonArray state, List<AiToolCall> calls, StringBuilder text)
    {
        if (protocol == "gemini")
        {
            var content = response["candidates"]?[0]?["content"];
            if (content?["parts"] is not JsonArray parts) throw new AiConfigurationException("Gemini returned no candidate content.");
            // Preserve all raw parts, including thoughtSignature, for the next function-response request.
            state.Add(Clone(content));
            foreach (var p in parts)
            {
                if (p?["functionCall"] is JsonObject f) AddCall(calls, f["name"]!.GetValue<string>(), f["id"]?.GetValue<string>() ?? "", f["args"] ?? new JsonObject());
                else if (p?["text"] != null && p["thought"]?.GetValue<bool>() != true) text.Append(p["text"]!.GetValue<string>());
            }
        }
        else if (protocol == "responses")
        {
            if (response["output"] is not JsonArray output) throw new AiConfigurationException("Responses API returned no output.");
            foreach (var item in output)
            {
                if (item == null) continue;
                state.Add(Clone(item));
                if (item["type"]?.GetValue<string>() == "function_call")
                {
                    var id = item["call_id"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(id)) throw new AiConfigurationException("Missing tool call ID.");
                    AddCall(calls, item["name"]!.GetValue<string>(), id, JsonNode.Parse(item["arguments"]!.GetValue<string>()));
                }
                else if (item["content"] is JsonArray parts)
                    foreach (var p in parts) if (p?["type"]?.GetValue<string>() == "output_text") text.Append(p["text"]!.GetValue<string>());
            }
        }
        else if (protocol == "anthropic")
        {
            if (response["content"] is not JsonArray content) throw new AiConfigurationException("Messages API returned no content.");
            state.Add(new JsonObject { ["role"] = "assistant", ["content"] = Clone(content) });
            foreach (var p in content)
            {
                if (p?["type"]?.GetValue<string>() == "text") text.Append(p["text"]!.GetValue<string>());
                else if (p?["type"]?.GetValue<string>() == "tool_use")
                {
                    var id = p["id"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(id)) throw new AiConfigurationException("Missing tool call ID.");
                    AddCall(calls, p["name"]!.GetValue<string>(), id, p["input"]);
                }
            }
        }
        else
        {
            var msg = response["choices"]?[0]?["message"] as JsonObject ?? throw new AiConfigurationException("Chat Completions returned no message.");
            var assistant = new JsonObject { ["role"] = "assistant", ["content"] = msg["content"] == null ? null : Clone(msg["content"]!) };
            if (msg["reasoning_content"] != null) assistant["reasoning_content"] = Clone(msg["reasoning_content"]!);
            if (msg["content"] is JsonValue v) text.Append(v.GetValue<string>());
            if (msg["tool_calls"] is JsonArray toolCalls)
            {
                assistant["tool_calls"] = Clone(toolCalls);
                foreach (var call in toolCalls)
                {
                    if (call?["type"]?.GetValue<string>() != "function") throw new AiConfigurationException("Unknown tool call type.");
                    var id = call["id"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(id)) throw new AiConfigurationException("Missing tool call ID.");
                    AddCall(calls, call["function"]!["name"]!.GetValue<string>(), id, JsonNode.Parse(call["function"]!["arguments"]!.GetValue<string>()));
                }
            }
            state.Add(assistant);
        }
    }
    private static void AppendResults(string protocol, JsonArray state, IReadOnlyList<AiToolResult> results)
    {
        var native = new JsonArray();
        foreach (var r in results)
        {
            var payload = JsonSerializer.Serialize(new { success = r.Success, message = r.Message });
            if (protocol == "gemini")
            {
                var response = new JsonObject { ["name"] = r.Name, ["response"] = JsonNode.Parse(payload) };
                if (r.Id.Length > 0) response["id"] = r.Id;
                native.Add(new JsonObject { ["functionResponse"] = response });
            }
            else if (protocol == "anthropic") native.Add(new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = r.Id, ["content"] = payload, ["is_error"] = !r.Success });
            else if (protocol == "responses") state.Add(new JsonObject { ["type"] = "function_call_output", ["call_id"] = r.Id, ["output"] = payload });
            else state.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = r.Id, ["content"] = payload });
        }
        if (protocol == "gemini") state.Add(new JsonObject { ["role"] = "user", ["parts"] = native });
        else if (protocol == "anthropic") state.Add(new JsonObject { ["role"] = "user", ["content"] = native });
    }
}
