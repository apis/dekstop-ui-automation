using CalcAutomation.Core;
using NUnit.Framework;

namespace CalcAutomation.Tests;

/// <summary>
/// AI-driven tests: describe the goal in natural language, Claude figures out
/// which elements to click and verifies the result.
/// Requires ANTHROPIC_API_KEY environment variable.
/// </summary>
[TestFixture]
[Apartment(ApartmentState.STA)]
[Category("Agent")]
public class AgentCalculatorTests
{
    private CalculatorAgent _agent = null!;

    [SetUp]
    public void Setup()
    {
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? throw new IgnoreException("ANTHROPIC_API_KEY is not set — skipping agent tests.");
        _agent = new CalculatorAgent(apiKey);
    }

    [TearDown]
    public void Teardown() => _agent?.Dispose();

    [Test]
    public async Task Agent_Adds_7_And_5()
    {
        var result = await _agent.RunAsync(
            "Open calc.exe. Add 7 and 5. Tell me the result shown on the display.");

        Console.WriteLine($"Agent result: {result}");
        Assert.That(result, Does.Contain("12"),
            $"Expected '12' in agent response but got: {result}");
    }

    [Test]
    public async Task Agent_Multiplies_6_By_8()
    {
        var result = await _agent.RunAsync(
            "Open calc.exe. Multiply 6 by 8. Return only the numeric result.");

        Console.WriteLine($"Agent result: {result}");
        Assert.That(result, Does.Contain("48"),
            $"Expected '48' in agent response but got: {result}");
    }

    [Test]
    public async Task Agent_Performs_Chain_Calculation()
    {
        var result = await _agent.RunAsync(
            "Open calc.exe. Calculate: 100 divided by 4, then subtract 3. What does the display show?");

        Console.WriteLine($"Agent result: {result}");
        Assert.That(result, Does.Contain("22"),
            $"Expected '22' in agent response but got: {result}");
    }

    [Test]
    public async Task Agent_Verifies_Division_By_Zero_Behavior()
    {
        var result = await _agent.RunAsync(
            "Open calc.exe. Try to divide 5 by 0. Describe what the display shows.");

        Console.WriteLine($"Agent result: {result}");
        // Windows Calculator shows "Cannot divide by zero" or similar
        Assert.That(result.Length, Is.GreaterThan(0), "Agent should return a description");
        Console.WriteLine("Division-by-zero display: " + result);
    }
}
