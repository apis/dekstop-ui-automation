using CalcAutomation.Core.Models;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace CalcAutomation.Core;

/// <summary>
/// AI-driven automation agent that uses Claude + FlaUI to operate Windows applications.
/// Implements the snapshot-ref loop: Claude reads the accessibility tree, then issues
/// tool calls (click, get_text, etc.) to interact with UI elements by their ref IDs.
/// </summary>
public sealed class CalculatorAgent : IDisposable
{
    private readonly AnthropicClient _ai;
    private readonly FlaUiEngine _fla;
    private readonly ILogger<CalculatorAgent> _logger;

    private const string SystemPrompt = """
        You are a Windows UI automation agent. You control applications using these tools:
        - windows_launch: start an application
        - windows_snapshot: read the accessibility tree (always call this before interacting)
        - windows_click: click a UI element by its ref from the snapshot
        - windows_get_text: read text/value from a UI element
        - windows_close: close an application window

        Workflow:
        1. Launch the app if not already running.
        2. Call windows_snapshot to read the UI tree with element refs.
        3. Identify relevant elements from the snapshot output.
        4. Issue windows_click / windows_get_text using the exact ref IDs from the snapshot.
        5. Re-snapshot after interactions that change the UI if verification is needed.
        6. When the task is complete, return a concise result string (e.g. "Result: 12").

        Rules:
        - Never guess coordinates. Always use refs from a snapshot.
        - Refs are invalidated after each new snapshot call — use fresh refs only.
        - For calc.exe use the path "calc.exe".
        """;

    public CalculatorAgent(
        string apiKey,
        string model = "claude-sonnet-4-6",
        ILogger<CalculatorAgent>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<CalculatorAgent>.Instance;
        _ai = new AnthropicClient(apiKey, model);
        _fla = new FlaUiEngine();
    }

    /// <summary>
    /// Runs a natural-language automation task to completion and returns the agent's final answer.
    /// </summary>
    public async Task<string> RunAsync(string instruction, CancellationToken ct = default)
    {
        _logger.LogInformation("Task: {Instruction}", instruction);

        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = instruction }
        };
        var tools = BuildToolDefinitions();

        for (var turn = 0; turn < 30; turn++) // safety cap
        {
            var response = await _ai.SendAsync(messages, tools, SystemPrompt, ct);

            // Append assistant turn
            messages.Add(new ChatMessage
            {
                Role = "assistant",
                Content = response.Content
            });

            if (response.StopReason == "end_turn")
            {
                var text = response.Content.OfType<TextBlock>().LastOrDefault()?.Text ?? "";
                _logger.LogInformation("Agent finished: {Result}", text);
                return text;
            }

            if (response.StopReason != "tool_use")
                throw new InvalidOperationException($"Unexpected stop_reason: {response.StopReason}");

            // Execute each tool call and collect results
            var results = new List<ToolResultBlock>();
            foreach (var block in response.Content.OfType<ToolUseBlock>())
            {
                _logger.LogInformation("Tool call: {Name}({Input})", block.Name,
                    JsonSerializer.Serialize(block.Input));
                var output = ExecuteTool(block);
                _logger.LogInformation("Tool result: {Output}", Truncate(output, 300));
                results.Add(new ToolResultBlock { ToolUseId = block.Id, Content = output });
            }

            messages.Add(new ChatMessage { Role = "user", Content = results });
        }

        throw new InvalidOperationException("Agent did not finish within 30 turns.");
    }

    private string ExecuteTool(ToolUseBlock call)
    {
        try
        {
            return call.Name switch
            {
                "windows_launch" => _fla.Launch(
                    GetString(call.Input, "app"),
                    GetStringArray(call.Input, "args")),

                "windows_snapshot" => _fla.Snapshot(
                    GetStringOrNull(call.Input, "handle")),

                "windows_click" => Execute(() => _fla.Click(GetString(call.Input, "ref"))),

                "windows_get_text" => _fla.GetText(GetString(call.Input, "ref")),

                "windows_type" => Execute(() => _fla.Type(
                    GetString(call.Input, "text"),
                    GetStringOrNull(call.Input, "ref"))),

                "windows_focus" => Execute(() => _fla.Focus(GetString(call.Input, "ref"))),

                "windows_close" => Execute(() => _fla.Close(GetString(call.Input, "handle"))),

                _ => $"Unknown tool: {call.Name}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Tool {Name} failed: {Msg}", call.Name, ex.Message);
            return $"Error: {ex.Message}";
        }
    }

    // ── Tool definitions (mirror the fla-ui MCP tool schema) ─────────────────

    private static List<ToolDefinition> BuildToolDefinitions() =>
    [
        new ToolDefinition
        {
            Name = "windows_launch",
            Description = "Launch a Windows application. Returns a window handle.",
            InputSchema = new InputSchema
            {
                Properties = new()
                {
                    ["app"] = new() { Type = "string", Description = "Executable path or name, e.g. 'calc.exe'" },
                    ["args"] = new() { Type = "string", Description = "Optional command-line arguments" }
                },
                Required = ["app"]
            }
        },
        new ToolDefinition
        {
            Name = "windows_snapshot",
            Description = "Return the accessibility element tree for a window. " +
                          "Each element gets a unique ref (e.g. w1e5). " +
                          "Always call this before clicking or reading text.",
            InputSchema = new InputSchema
            {
                Properties = new()
                {
                    ["handle"] = new() { Type = "string", Description = "Window handle from windows_launch. Omit to use the most recent window." }
                }
            }
        },
        new ToolDefinition
        {
            Name = "windows_click",
            Description = "Click a UI element by its ref from windows_snapshot.",
            InputSchema = new InputSchema
            {
                Properties = new()
                {
                    ["ref"] = new() { Type = "string", Description = "Element ref, e.g. 'w1e5'" }
                },
                Required = ["ref"]
            }
        },
        new ToolDefinition
        {
            Name = "windows_get_text",
            Description = "Read the text or value of an element by its ref.",
            InputSchema = new InputSchema
            {
                Properties = new()
                {
                    ["ref"] = new() { Type = "string", Description = "Element ref from windows_snapshot" }
                },
                Required = ["ref"]
            }
        },
        new ToolDefinition
        {
            Name = "windows_type",
            Description = "Type text into an element (focuses it first).",
            InputSchema = new InputSchema
            {
                Properties = new()
                {
                    ["text"] = new() { Type = "string", Description = "Text to type" },
                    ["ref"] = new() { Type = "string", Description = "Element ref to focus before typing (optional)" }
                },
                Required = ["text"]
            }
        },
        new ToolDefinition
        {
            Name = "windows_close",
            Description = "Close a window by its handle.",
            InputSchema = new InputSchema
            {
                Properties = new()
                {
                    ["handle"] = new() { Type = "string", Description = "Window handle from windows_launch" }
                },
                Required = ["handle"]
            }
        }
    ];

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string Execute(Action action) { action(); return "ok"; }

    private static string GetString(Dictionary<string, object?> input, string key) =>
        input.TryGetValue(key, out var v) && v is JsonElement je ? je.GetString()!
        : throw new ArgumentException($"Missing required input '{key}'");

    private static string? GetStringOrNull(Dictionary<string, object?> input, string key) =>
        input.TryGetValue(key, out var v) && v is JsonElement je ? je.GetString()
        : null;

    private static string[]? GetStringArray(Dictionary<string, object?> input, string key) =>
        input.TryGetValue(key, out var v) && v is JsonElement je && je.ValueKind == JsonValueKind.Array
            ? je.EnumerateArray().Select(e => e.GetString()!).ToArray()
            : null;

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + $"… (+{s.Length - max} chars)";

    public void Dispose()
    {
        _ai.Dispose();
        _fla.Dispose();
    }
}
