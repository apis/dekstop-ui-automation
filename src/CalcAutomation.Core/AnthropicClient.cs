using CalcAutomation.Core.Models;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CalcAutomation.Core;

/// <summary>
/// Minimal Anthropic Messages API client with tool-use support.
/// Uses the REST API directly to avoid SDK version coupling.
/// </summary>
public sealed class AnthropicClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly ILogger<AnthropicClient> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public AnthropicClient(
        string apiKey,
        string model = "claude-sonnet-4-6",
        ILogger<AnthropicClient>? logger = null)
    {
        _model = model;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<AnthropicClient>.Instance;
        _http = new HttpClient { BaseAddress = new Uri("https://api.anthropic.com") };
        _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
    }

    /// <summary>
    /// Sends a message request and returns the parsed response.
    /// Throws <see cref="HttpRequestException"/> on non-2xx status.
    /// </summary>
    public async Task<MessageResponse> SendAsync(
        List<ChatMessage> messages,
        List<ToolDefinition> tools,
        string? system = null,
        CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["model"] = _model,
            ["max_tokens"] = 8192,
            ["messages"] = JsonSerializer.SerializeToNode(messages, JsonOpts),
            ["tools"] = JsonSerializer.SerializeToNode(tools, JsonOpts)
        };
        if (system != null) body["system"] = system;

        _logger.LogDebug("POST /v1/messages model={Model} msgs={Count}", _model, messages.Count);

        var resp = await _http.PostAsJsonAsync("/v1/messages", body, JsonOpts, ct);

        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Anthropic API {(int)resp.StatusCode}: {err}");
        }

        var result = await resp.Content.ReadFromJsonAsync<MessageResponse>(JsonOpts, ct)
            ?? throw new InvalidOperationException("Null response from Anthropic API");

        _logger.LogDebug("Response stop_reason={Reason} blocks={Count}",
            result.StopReason, result.Content.Count);

        return result;
    }

    public void Dispose() => _http.Dispose();
}
