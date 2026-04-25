using CalcAutomation.Core.Models;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CalcAutomation.Core;

/// <summary>
/// Client for Ollama's OpenAI-compatible chat endpoint (/v1/chat/completions).
/// Handles the tool-use loop message format (tool_calls / tool role).
/// </summary>
public sealed class OllamaClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly ILogger<OllamaClient> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OllamaClient(
        string model = "gemma4:e4b",
        string baseUrl = "http://localhost:11434",
        ILogger<OllamaClient>? logger = null)
    {
        _model = model;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<OllamaClient>.Instance;
        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
    }

    /// <summary>Sends a chat/completions request and returns the parsed response.</summary>
    public async Task<OllamaChatResponse> SendAsync(
        List<JsonNode> messages,
        List<OllamaTool> tools,
        string? system = null,
        CancellationToken ct = default)
    {
        var msgs = new List<JsonNode>(messages);
        if (system != null)
            msgs.Insert(0, JsonNode.Parse(JsonSerializer.Serialize(
                new { role = "system", content = system }, JsonOpts))!);

        var body = new JsonObject
        {
            ["model"] = _model,
            ["messages"] = JsonSerializer.SerializeToNode(msgs, JsonOpts),
            ["tools"] = JsonSerializer.SerializeToNode(tools, JsonOpts),
            ["stream"] = false
        };

        _logger.LogDebug("POST /v1/chat/completions model={Model} msgs={Count}", _model, msgs.Count);

        var resp = await _http.PostAsJsonAsync("/v1/chat/completions", body, JsonOpts, ct);

        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Ollama {(int)resp.StatusCode}: {err}");
        }

        return await resp.Content.ReadFromJsonAsync<OllamaChatResponse>(JsonOpts, ct)
            ?? throw new InvalidOperationException("Null response from Ollama");
    }

    public void Dispose() => _http.Dispose();
}
