using CalcAutomation.BDD.Support;
using Reqnroll;

namespace CalcAutomation.BDD.StepDefinitions;

[Binding]
public sealed class CalculatorSteps
{
    private readonly CalculatorContext _ctx;

    public CalculatorSteps(CalculatorContext ctx) => _ctx = ctx;

    // ── Given ────────────────────────────────────────────────────────────────

    [Given("the Calculator application is open")]
    public void GivenTheCalculatorIsOpen() =>
        Assert.That(_ctx.Handle, Is.Not.Null.And.Not.Empty,
            "Calculator should have been launched in BeforeScenario hook");

    // ── When – named operations ───────────────────────────────────────────────

    [When("I add {int} and {int}")]
    public void WhenIAdd(int a, int b) => Compute(a, "+", b);

    // "subtract 4 from 10" → the result is 10 - 4
    [When("I subtract {int} from {int}")]
    public void WhenISubtract(int subtrahend, int minuend) => Compute(minuend, "-", subtrahend);

    [When("I multiply {int} by {int}")]
    public void WhenIMultiply(int a, int b) => Compute(a, "×", b);

    [When("I divide {int} by {int}")]
    public void WhenIDivide(int dividend, int divisor) => Compute(dividend, "÷", divisor);

    // ── When – generic (used by Scenario Outline) ────────────────────────────

    // Accepts +  -  *  /  (mapped to the actual button symbols by MapOperator)
    [When("I calculate {int} {word} {int}")]
    public void WhenICalculate(int left, string op, int right) =>
        Compute(left, CalculatorContext.MapOperator(op), right);

    // ── Then ─────────────────────────────────────────────────────────────────

    [Then("the display should show {int}")]
    public void ThenTheDisplayShouldShow(int expected)
    {
        var text = GetDisplayText();
        // Windows Calculator formats large numbers with thousands separators; strip them before comparing.
        var normalized = text.Replace(",", "");
        Assert.That(normalized, Does.Contain(expected.ToString()),
            $"Expected display to contain '{expected}', but it shows: '{text}'");
    }

    // Handles quoted string values in Gherkin: Then the display should show "0.25"
    [Then("the display should show {string}")]
    public void ThenTheDisplayShouldShowString(string expected)
    {
        var text = GetDisplayText();
        Assert.That(text, Does.Contain(expected),
            $"Expected display to contain '{expected}', but it shows: '{text}'");
    }

    // Partial-match step: Then the display should contain "Cannot divide by zero"
    [Then("the display should contain {string}")]
    public void ThenTheDisplayShouldContain(string expected)
    {
        var text = GetDisplayText();
        Assert.That(text, Does.Contain(expected),
            $"Expected display to contain '{expected}', but it shows: '{text}'");
    }

    private string GetDisplayText()
    {
        var refs = _ctx.SnapshotAndIndex();
        Assert.That(refs.TryGetValue("display", out var displayRef), Is.True,
            "Could not find the display element in the accessibility tree");
        return _ctx.Fla.GetText(displayRef!);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void Compute(int left, string op, int right)
    {
        var refs = _ctx.SnapshotAndIndex();
        var keys = CalculatorContext.ToDigits(left)
            .Append(op)
            .Concat(CalculatorContext.ToDigits(right))
            .Append("=")
            .ToArray();
        _ctx.PressSequence(refs, keys);
    }
}
