using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace LilithTextInjector;

// Independent ASR transports; the capture owner supplies PCM and a fixed language for the recording.
internal sealed class AiAsrClient
{
    private readonly HttpClient _http;
    internal AiAsrClient(HttpClient http) { _http = http; }
    internal async Task<string> TranscribeAsync(AiAsrSnapshot s, byte[] wav, string language, string instruction, CancellationToken cancellation = default)
    {
        var protocol = s.Model.Protocol;
        var suffix = protocol switch
        {
            "gemini-audio" => "/models/" + Uri.EscapeDataString(s.Model.Model) + ":generateContent",
            "qwen-http" => "/chat/completions", "audio-transcriptions" => "/audio/transcriptions",
            _ => throw new AiConfigurationException("Select an HTTP ASR model for file transcription.")
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, s.Model.Endpoint ?? s.BaseUrl.TrimEnd('/') + suffix);
        AiChatClient.Authenticate(request, protocol, s.Key);
        if (protocol == "audio-transcriptions")
        {
            var form = new MultipartFormDataContent();
            form.Add(new StringContent(s.Model.Model), "model");
            if (language.Length > 0) form.Add(new StringContent(language), "language");
            var audio = new ByteArrayContent(wav); audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            form.Add(audio, "file", "recording.wav"); request.Content = form;
        }
        else
        {
            object body;
            if (protocol == "gemini-audio") body = new
            {
                contents = new[] { new { role = "user", parts = new object[]
                {
                    new { inlineData = new { mimeType = "audio/wav", data = Convert.ToBase64String(wav) } },
                    new { text = "Transcribe only the spoken words, without commentary. " + instruction }
                } } }
            };
            else
            {
                var options = new Dictionary<string, object> { ["enable_itn"] = false };
                if (language.Length > 0) options["language"] = language;
                body = new
                {
                    model = s.Model.Model, stream = false, asr_options = options,
                    messages = new[] { new { role = "user", content = new[] { new { type = "input_audio", input_audio = new { data = "data:audio/wav;base64," + Convert.ToBase64String(wav) } } } } }
                };
            }
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }
        using var response = await _http.SendAsync(request, cancellation).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new AiServiceException((int)response.StatusCode);
        try
        {
            var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false));
            string text;
            if (protocol == "gemini-audio")
            {
                var builder = new StringBuilder();
                if (body?["candidates"]?[0]?["content"]?["parts"] is JsonArray parts)
                    foreach (var p in parts) if (p?["text"] != null && p["thought"]?.GetValue<bool>() != true) builder.Append(p["text"]!.GetValue<string>());
                text = builder.ToString();
            }
            else text = (protocol == "qwen-http" ? body?["choices"]?[0]?["message"]?["content"] : body?["text"])?.GetValue<string>() ?? "";
            if (string.IsNullOrWhiteSpace(text)) throw new AiConfigurationException("ASR service returned no transcript.");
            return text.Trim();
        }
        catch (Exception e) when (e is JsonException || e is InvalidOperationException || e is ArgumentOutOfRangeException)
        { throw new AiConfigurationException("ASR service returned an invalid response."); }
    }
    internal async Task<string> CompleteRealtimeAsync(Task<string> realtime, AiRecordingSnapshot s, byte[] wav,
        string language, string instruction, CancellationToken cancellation = default)
    {
        try { return await realtime.ConfigureAwait(false); }
        catch (Exception) when (s.Fallback != null && !cancellation.IsCancellationRequested)
        { return await TranscribeAsync(s.Fallback, wav, language, instruction, cancellation).ConfigureAwait(false); }
    }
    internal async Task<string> RealtimeAsync(AiAsrSnapshot s, string language, Func<byte[]?> dequeue, Func<bool> finished, CancellationToken cancellation)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(120));
        var token = deadline.Token;
        using var socket = new ClientWebSocket();
        Task<string>? received = null;
        if (s.Key != null) socket.Options.SetRequestHeader("Authorization", "Bearer " + s.Key);
        try
        {
            await socket.ConnectAsync(new Uri(s.Model.Endpoint!), token).WaitAsync(TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
            var taskId = Guid.NewGuid().ToString("N");
            var parameters = new Dictionary<string, object>
            {
                ["format"] = "pcm", ["sample_rate"] = 16000, ["disfluency_removal_enabled"] = false,
                ["punctuation_prediction_enabled"] = true, ["inverse_text_normalization_enabled"] = true
            };
            if (language.Length > 0) parameters["language_hints"] = new[] { language };
            await SendText(socket, JsonSerializer.Serialize(new
            {
                header = new { action = "run-task", task_id = taskId, streaming = "duplex" },
                payload = new { task_group = "audio", task = "asr", function = "recognition", model = s.Model.Model, parameters, input = new { } }
            }), token).ConfigureAwait(false);
            while (true)
            {
                var message = await ReceiveText(socket, token).WaitAsync(TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
                var header = JsonNode.Parse(message)?["header"];
                var evt = header?["event"]?.GetValue<string>();
                if (evt == "task-failed") throw new AiConfigurationException("Realtime ASR service rejected the task.");
                if (evt == "task-started") break;
            }
            received = ReceiveTranscript(socket, token);
            while (true)
            {
                if (received.IsCompleted) return await received.ConfigureAwait(false);
                var audio = dequeue();
                if (audio != null)
                    for (var offset = 0; offset < audio.Length; offset += 8192)
                        await socket.SendAsync(new ArraySegment<byte>(audio, offset, Math.Min(8192, audio.Length - offset)), WebSocketMessageType.Binary, true, token).ConfigureAwait(false);
                else if (finished()) break;
                else await Task.Delay(8, token).ConfigureAwait(false);
            }
            await SendText(socket, JsonSerializer.Serialize(new { header = new { action = "finish-task", task_id = taskId, streaming = "duplex" }, payload = new { input = new { } } }), token).ConfigureAwait(false);
            return await received.WaitAsync(TimeSpan.FromSeconds(15), token).ConfigureAwait(false);
        }
        catch (AiConfigurationException) { throw; }
        catch (OperationCanceledException) { throw new AiConfigurationException("Realtime ASR was cancelled or timed out."); }
        catch { throw new AiConfigurationException("Realtime ASR connection or response failed."); }
        finally
        {
            deadline.Cancel();
            if (received != null)
            {
                socket.Abort();
                try { await received.ConfigureAwait(false); } catch { /* The operation error above is authoritative. */ }
            }
        }
    }
    private static Task SendText(ClientWebSocket socket, string text, CancellationToken token)
        => socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(text)), WebSocketMessageType.Text, true, token);
    private static async Task<string> ReceiveText(ClientWebSocket socket, CancellationToken token)
    {
        var buffer = new byte[8192]; using var stream = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close) throw new AiConfigurationException("Realtime ASR connection closed.");
            if (result.MessageType != WebSocketMessageType.Text) continue;
            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage) return Encoding.UTF8.GetString(stream.ToArray());
        }
    }
    private static async Task<string> ReceiveTranscript(ClientWebSocket socket, CancellationToken token)
    {
        var sentences = new StringBuilder(); var interim = "";
        while (true)
        {
            var message = JsonNode.Parse(await ReceiveText(socket, token).ConfigureAwait(false));
            var evt = message?["header"]?["event"]?.GetValue<string>();
            if (evt == "task-failed") throw new AiConfigurationException("Realtime ASR task failed.");
            if (evt == "task-finished")
            {
                var transcript = sentences.Length > 0 ? sentences.ToString() : interim;
                if (string.IsNullOrWhiteSpace(transcript)) throw new AiConfigurationException("Realtime ASR returned no transcript.");
                return transcript.Trim();
            }
            var sentence = message?["payload"]?["output"]?["sentence"];
            if (evt != "result-generated" || sentence == null) continue;
            var text = sentence["text"]?.GetValue<string>() ?? "";
            if (sentence["sentence_end"]?.GetValue<bool>() == true) { sentences.Append(text); interim = ""; }
            else interim = text;
        }
    }
}
