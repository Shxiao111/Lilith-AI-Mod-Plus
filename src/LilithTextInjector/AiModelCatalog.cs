using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace LilithTextInjector;

// Model discovery may append /v1 for Anthropic's list endpoint. Chat requests keep the configured base URL.
internal static class AiModelCatalog
{
    internal static Uri ListUri(string protocol, string baseUrl)
    {
        var canonical = AiConfigurationStore.CanonicalChatProtocol(protocol);
        if (!Uri.TryCreate(baseUrl.Trim().TrimEnd('/'), UriKind.Absolute, out var root)
            || root.Scheme != "http" && root.Scheme != "https"
            || !string.IsNullOrEmpty(root.UserInfo) || !string.IsNullOrEmpty(root.Query) || !string.IsNullOrEmpty(root.Fragment))
            throw new AiConfigurationException("Use an absolute HTTP URL without credentials, query or fragment.");
        var text = root.AbsoluteUri.TrimEnd('/');
        if (canonical == "anthropic")
            text = text.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? text + "/models?limit=1000" : text + "/v1/models?limit=1000";
        else text += "/models";
        return new Uri(text);
    }
    internal static async Task<IReadOnlyList<string>> FetchAsync(HttpClient http, string protocol, string baseUrl, string? key, CancellationToken cancellation)
    {
        var canonical = AiConfigurationStore.CanonicalChatProtocol(protocol);
        if (!AiConfigurationStore.ChatProtocols.Contains(canonical)) throw new AiConfigurationException("Unknown chat protocol.");
        using var request = new HttpRequestMessage(HttpMethod.Get, ListUri(canonical, baseUrl));
        AiChatClient.Authenticate(request, canonical, key);
        using var response = await http.SendAsync(request, cancellation).ConfigureAwait(false);
        var raw = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new AiServiceException((int)response.StatusCode);
        return Parse(canonical, raw);
    }
    internal static IReadOnlyList<string> Parse(string protocol, string json)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (JsonException) { throw new AiConfigurationException("AI service returned an invalid JSON response."); }
        var canonical = AiConfigurationStore.CanonicalChatProtocol(protocol);
        var array = canonical == "gemini" ? root?["models"] as JsonArray ?? root?["data"] as JsonArray : root?["data"] as JsonArray;
        var ids = new List<string>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        if (array != null) foreach (var item in array)
        {
            var name = Text(item?["name"]); var id = Text(item?["id"]);
            var chosen = canonical == "gemini" && !string.IsNullOrWhiteSpace(name) ? name! : id;
            if (string.IsNullOrWhiteSpace(chosen)) continue;
            chosen = chosen.Trim();
            if (chosen.StartsWith("models/", StringComparison.Ordinal)) chosen = chosen["models/".Length..];
            if (chosen.Length > 0 && seen.Add(chosen)) ids.Add(chosen);
        }
        if (ids.Count == 0) throw new AiConfigurationException("The endpoint returned no model ids.");
        return ids;
    }
    private static string? Text(JsonNode? node)
    {
        try { return node?.GetValue<string>(); }
        catch (InvalidOperationException) { return null; }
    }
}
