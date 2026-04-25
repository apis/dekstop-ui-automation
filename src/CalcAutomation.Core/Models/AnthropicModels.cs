using System.Text.Json.Serialization;

namespace CalcAutomation.Core.Models;

// ── Outbound (request) ────────────────────────────────────────────────────────

public record ChatMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    /// <summary>Either a plain string or a list of content blocks.</summary>
    [JsonPropertyName("content")]
    public required object Content { get; init; }
}

public record ToolDefinition
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("input_schema")]
    public required InputSchema InputSchema { get; init; }
}

public record InputSchema
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "object";

    [JsonPropertyName("properties")]
    public Dictionary<string, PropertySchema> Properties { get; init; } = new();

    [JsonPropertyName("required")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Required { get; init; }
}

public record PropertySchema
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("enum")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Enum { get; init; }
}

// ── Inbound (response) ────────────────────────────────────────────────────────

public record MessageResponse
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("stop_reason")]
    public string? StopReason { get; init; }

    [JsonPropertyName("content")]
    public List<ContentBlock> Content { get; init; } = new();
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextBlock), "text")]
[JsonDerivedType(typeof(ToolUseBlock), "tool_use")]
public abstract record ContentBlock
{
    [JsonPropertyName("type")]
    public abstract string Type { get; }
}

public record TextBlock : ContentBlock
{
    [JsonPropertyName("type")]
    public override string Type => "text";

    [JsonPropertyName("text")]
    public string Text { get; init; } = "";
}

public record ToolUseBlock : ContentBlock
{
    [JsonPropertyName("type")]
    public override string Type => "tool_use";

    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("input")]
    public Dictionary<string, object?> Input { get; init; } = new();
}

// ── Tool result (goes in a user message) ─────────────────────────────────────

public record ToolResultBlock
{
    [JsonPropertyName("type")]
    public string Type => "tool_result";

    [JsonPropertyName("tool_use_id")]
    public required string ToolUseId { get; init; }

    [JsonPropertyName("content")]
    public required string Content { get; init; }
}
