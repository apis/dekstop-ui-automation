using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CalcAutomation.Core.Models;

// ── Outbound ─────────────────────────────────────────────────────────────────

public record OllamaTool
{
    [JsonPropertyName("type")]
    public string Type => "function";

    [JsonPropertyName("function")]
    public required OllamaFunction Function { get; init; }
}

public record OllamaFunction
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("parameters")]
    public required InputSchema Parameters { get; init; }
}

// ── Inbound ───────────────────────────────────────────────────────────────────

public record OllamaChatResponse
{
    [JsonPropertyName("choices")]
    public List<OllamaChoice> Choices { get; init; } = [];
}

public record OllamaChoice
{
    [JsonPropertyName("message")]
    public required OllamaMessage Message { get; init; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; init; }
}

public record OllamaMessage
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = "assistant";

    [JsonPropertyName("content")]
    public string? Content { get; init; }

    [JsonPropertyName("tool_calls")]
    public List<OllamaToolCall>? ToolCalls { get; init; }
}

public record OllamaToolCall
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];

    [JsonPropertyName("function")]
    public required OllamaFunctionCall Function { get; init; }
}

public record OllamaFunctionCall
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>JSON string of the arguments object.</summary>
    [JsonPropertyName("arguments")]
    public required JsonNode Arguments { get; init; }
}
