using CalcAutomation.Core.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CalcAutomation.Core;

/// <summary>
/// AI-driven automation agent backed by a local Ollama instance.
/// Uses the same snapshot-ref interaction model as <see cref="CalculatorAgent"/>
/// but communicates with Ollama's OpenAI-compatible /v1/chat/completions endpoint.
/// </summary>
public sealed class OllamaCalculatorAgent : IDisposable
{
    private readonly OllamaClient _ai;
    private readonly FlaUiEngine _fla;
    private readonly ILogger<OllamaCalculatorAgent> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private const string SystemPrompt = """
        You are a Windows UI automation agent. Control applications using these tools:
        - windows_launch: start an application
        - windows_snapshot: read the accessibility tree (always call this before interacting)
        - windows_click: click a UI element by its ref from the snapshot
        - windows_get_text: read text/value from a UI element
        - windows_close: close an application window

        Rules:
        1. Always call windows_snapshot before clicking anything.
        2. Use exact ref IDs from the snapshot (e.g. w1e47). Never guess refs.
        3. Refs become invalid after a new windows_snapshot call.
        4. For calc.exe use the app path "calc.exe".
        5. When the task is done, state the result clearly (e.g. "Result: 12").

        Calculator workflow (STRICTLY follow this order):
        Step 1. windows_launch "calc.exe"
        Step 2. windows_snapshot — get the element tree
        Step 3. Find and click the "Clear" button (name="Clear" or name="CE") to reset any previous state
        Step 4. windows_snapshot again (refs changed after clicking)
        Step 5. Click each digit button and operator button ONE AT A TIME in the correct order.
                Example for "5 + 3 = ": click Five, click Plus, click Three, click Equals.
                Do NOT click multiple digits to form a multi-digit number unless the number itself is multi-digit.
        Step 6. windows_snapshot again, then use windows_get_text on the display element (name contains "Display" or AutomationId contains "CalculatorResults") to read the result.
        Step 7. Report: "Result: <number>"

        IMPORTANT:
        - Never skip the Clear step — the display may show a leftover value.
        - Never combine steps. Do one click per tool call.
        - The equals button has name="Equals" or label "=". Always click it before reading the display.
        - Read the display AFTER pressing equals, not before.
        """;

    public OllamaCalculatorAgent(
        string model = "gemma4:e4b",
        string baseUrl = "http://localhost:11434",
        ILogger<OllamaCalculatorAgent>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<OllamaCalculatorAgent>.Instance;
        _ai = new OllamaClient(model, baseUrl);
        _fla = new FlaUiEngine();
    }

    /// <summary>
    /// Runs a natural-language automation task to completion and returns the agent's final answer.
    /// </summary>
    public async Task<string> RunAsync(string instruction, CancellationToken ct = default)
    {
        _logger.LogInformation("Task: {Instruction}", instruction);

        var messages = new List<JsonNode>
        {
            JsonSerializer.SerializeToNode(new { role = "user", content = instruction }, JsonOpts)!
        };
        var tools = BuildTools();

        for (var turn = 0; turn < 30; turn++)
        {
            var response = await _ai.SendAsync(messages, tools, SystemPrompt, ct);
            var choice = response.Choices.FirstOrDefault()
                ?? throw new InvalidOperationException("Empty choices in Ollama response");

            var msg = choice.Message;

            // Append assistant turn as a raw JSON node
            messages.Add(JsonSerializer.SerializeToNode(new
            {
                role = "assistant",
                content = msg.Content,
                tool_calls = msg.ToolCalls
            }, JsonOpts)!);

            // Prioritise the presence of tool_calls over finish_reason.
            // Gemma4 (and some other local models) emit finish_reason="stop" even when
            // returning tool calls, so checking finish_reason alone is unreliable.
            var hasToolCalls = msg.ToolCalls is { Count: > 0 };

            if (!hasToolCalls)
            {
                var text = msg.Content ?? "";
                _logger.LogInformation("Agent finished (reason={Reason}): {Result}", choice.FinishReason, text);
                return text;
            }

            // Execute each tool call and collect tool-role messages
            foreach (var call in msg.ToolCalls!)
            {
                _logger.LogInformation("Tool: {Name}({Args})", call.Function.Name, call.Function.Arguments);
                var output = ExecuteTool(call);
                _logger.LogInformation("Result: {Output}", Truncate(output, 300));

                messages.Add(JsonSerializer.SerializeToNode(new
                {
                    role = "tool",
                    tool_call_id = call.Id,
                    content = output
                }, JsonOpts)!);
            }
        }

        throw new InvalidOperationException("Agent did not finish within 30 turns.");
    }

    private string ExecuteTool(OllamaToolCall call)
    {
        // Gemma4 (and some models) return arguments as a JSON-encoded string rather than
        // an object node. Normalise to a JsonObject before indexing.
        var args = NormalizeArgs(call.Function.Arguments);
        try
        {
            return call.Function.Name switch
            {
                "windows_launch" => _fla.Launch(GetString(args, "app"), GetStringArray(args, "args")),
                "windows_snapshot" => _fla.Snapshot(GetStringOrNull(args, "handle")),
                "windows_click" => Do(() => _fla.Click(GetString(args, "ref"))),
                "windows_get_text" => _fla.GetText(GetString(args, "ref")),
                "windows_close" => Do(() => _fla.Close(GetString(args, "handle"))),
                _ => $"Unknown tool: {call.Function.Name}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Tool {Name} failed: {Msg}", call.Function.Name, ex.Message);
            return $"Error: {ex.Message}";
        }
    }

    private static List<OllamaTool> BuildTools() =>
    [
        Tool("windows_launch", "Launch a Windows application. Returns a window handle.",
            Prop("app", "Executable name or path, e.g. 'calc.exe'"), required: ["app"]),

        Tool("windows_snapshot",
            "Return the accessibility element tree. Each element gets a unique ref (e.g. w1e5). " +
            "Always call this before clicking or reading text.",
            Prop("handle", "Window handle from windows_launch. Omit for the last opened window.")),

        Tool("windows_click", "Click a UI element by its ref from windows_snapshot.",
            Prop("ref", "Element ref, e.g. 'w1e47'"), required: ["ref"]),

        Tool("windows_get_text", "Read the text or value of an element by its ref.",
            Prop("ref", "Element ref from windows_snapshot"), required: ["ref"]),

        Tool("windows_close", "Close a window by its handle.",
            Prop("handle", "Window handle from windows_launch"), required: ["handle"])
    ];

    // ── Builder helpers ───────────────────────────────────────────────────────

    private static OllamaTool Tool(string name, string desc,
        (string name, string desc) prop, string[]? required = null) =>
        new()
        {
            Function = new OllamaFunction
            {
                Name = name,
                Description = desc,
                Parameters = new InputSchema
                {
                    Properties = new() { [prop.name] = new PropertySchema { Type = "string", Description = prop.desc } },
                    Required = required?.ToList()
                }
            }
        };

    private static (string name, string desc) Prop(string name, string desc) => (name, desc);

    // ── JSON arg helpers ──────────────────────────────────────────────────────

    // Some models serialise arguments as a JSON string instead of an inline object.
    private static JsonNode NormalizeArgs(JsonNode args)
    {
        if (args is JsonValue v && v.TryGetValue<string>(out var s))
        {
            try { return JsonNode.Parse(s) ?? new JsonObject(); }
            catch { return new JsonObject(); }
        }
        return args;
    }

    private static string GetString(JsonNode args, string key)
    {
        var node = args[key] ?? throw new ArgumentException($"Missing required arg '{key}'");
        return node.GetValue<string>();
    }

    private static string? GetStringOrNull(JsonNode args, string key) =>
        args[key]?.GetValue<string>();

    private static string[]? GetStringArray(JsonNode args, string key)
    {
        var node = args[key];
        if (node is JsonArray arr)
            return arr.Select(x => x!.GetValue<string>()).ToArray();
        return null;
    }

    private static string Do(Action a) { a(); return "ok"; }
    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + $"… (+{s.Length - max} chars)";

    public void Dispose()
    {
        _ai.Dispose();
        _fla.Dispose();
    }
}
