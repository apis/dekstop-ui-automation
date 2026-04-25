using CalcAutomation.Core;
using NUnit.Framework;

namespace CalcAutomation.Tests;

/// <summary>
/// AI-driven tests using a local Ollama instance (model: gemma4:e4b).
/// Requires Ollama running at http://localhost:11434 with the model pulled:
///   ollama pull gemma4:e4b
/// </summary>
[TestFixture]
[Apartment(ApartmentState.STA)]
[Category("Ollama")]
public class OllamaAgentCalculatorTests
{
    private OllamaCalculatorAgent _agent = null!;

    private const string Model = "gemma4:e4b";
    private const string BaseUrl = "http://localhost:11434";

    [SetUp]
    public async Task Setup()
    {
        await EnsureOllamaReachable();
        _agent = new OllamaCalculatorAgent(Model, BaseUrl);
    }

    [TearDown]
    public void Teardown() => _agent?.Dispose();

    [Test]
    public async Task Ollama_Adds_5_And_3()
    {
        var result = await _agent.RunAsync(
            "Use calc.exe to compute 5 + 3. " +
            "Press Clear, then press Five, then Plus, then Three, then Equals. " +
            "Read the display and report the result.");

        Console.WriteLine($"Agent result: {result}");
        Assert.That(result, Does.Contain("8"),
            $"Expected '8' in agent response but got: {result}");
    }

    [Test]
    public async Task Ollama_Multiplies_4_By_7()
    {
        var result = await _agent.RunAsync(
            "Use calc.exe to compute 4 × 7. " +
            "Press Clear, then press Four, then Multiply, then Seven, then Equals. " +
            "Read the display and report the result.");

        Console.WriteLine($"Agent result: {result}");
        Assert.That(result, Does.Contain("28"),
            $"Expected '28' in agent response but got: {result}");
    }

    [Test]
    public async Task Ollama_Subtracts_3_From_10()
    {
        var result = await _agent.RunAsync(
            "Use calc.exe to compute 10 - 3. " +
            "Press Clear, then press One, then Zero, then Minus, then Three, then Equals. " +
            "Read the display and report the result.");

        Console.WriteLine($"Agent result: {result}");
        Assert.That(result, Does.Contain("7"),
            $"Expected '7' in agent response but got: {result}");
    }

    [Test]
    public async Task Ollama_Divides_20_By_4()
    {
        var result = await _agent.RunAsync(
            "Use calc.exe to compute 20 ÷ 4. " +
            "Press Clear, then press Two, then Zero, then Divide, then Four, then Equals. " +
            "Read the display and report the result.");

        Console.WriteLine($"Agent result: {result}");
        Assert.That(result, Does.Contain("5"),
            $"Expected '5' in agent response but got: {result}");
    }

    // ── Setup helper ──────────────────────────────────────────────────────────

    private static async Task EnsureOllamaReachable()
    {
        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            var resp = await http.GetAsync($"{BaseUrl}/api/tags");
            if (!resp.IsSuccessStatusCode)
                Assert.Ignore($"Ollama not reachable at {BaseUrl} (status {(int)resp.StatusCode}) — skipping Ollama tests.");
        }
        catch (Exception ex)
        {
            Assert.Ignore($"Ollama not reachable at {BaseUrl}: {ex.Message} — skipping Ollama tests.");
        }
    }
}
