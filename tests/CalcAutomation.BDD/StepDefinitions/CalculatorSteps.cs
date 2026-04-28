using CalcAutomation.BDD.Support;
using NUnit.Framework;
using Reqnroll;

namespace CalcAutomation.BDD.StepDefinitions;

[Binding]
public sealed class CalculatorSteps
{
    private readonly CalculatorContext _ctx;

    public CalculatorSteps(CalculatorContext ctx)
    {
        _ctx = ctx;
    }

    // ── Background ───────────────────────────────────────────────────────────

    [Given("the Calculator application is open")]
    public void GivenTheCalculatorApplicationIsOpen()
    {
        Assert.That(_ctx.Handle, Is.Not.Null, "Calculator window handle should not be null.");
    }

    // ── When steps ───────────────────────────────────────────────────────────

    [When("I add {int} and {int}")]
    public void WhenIAdd(int a, int b) => Compute(a, "+", b);

    [When("I subtract {int} from {int}")]
    public void WhenISubtract(int subtrahend, int minuend) => Compute(minuend, "-", subtrahend);

    [When("I multiply {int} by {int}")]
    public void WhenIMultiply(int a, int b) => Compute(a, "×", b);

    [When("I divide {int} by {int}")]
    public void WhenIDivide(int dividend, int divisor) => Compute(dividend, "÷", divisor);

    [When("I calculate {int} {word} {int}")]
    public void WhenICalculate(int left, string op, int right) =>
        Compute(left, CalculatorContext.MapOperator(op), right);

    [When("the user calculates {int} {word} {int}")]
    public void WhenTheUserCalculates(int left, string op, int right) =>
        WhenICalculate(left, op, right);

    [When("I take the square root of {int}")]
    public void WhenITakeSquareRootOf(int n) => ComputeUnary(n, "√");

    [When("I square {int}")]
    public void WhenISquare(int n) => ComputeUnary(n, "x²");

    [When("I take the reciprocal of {int}")]
    public void WhenITakeReciprocalOf(int n) => ComputeUnary(n, "1/x");

    [When("I negate {int}")]
    public void WhenINegate(int n) => ComputeUnary(n, "+/-");

    [When("I enter {int}")]
    public void WhenIEnter(int number)
    {
        var refs = _ctx.SnapshotAndIndex();
        foreach (var digit in CalculatorContext.ToDigits(number))
        {
            if (!refs.TryGetValue(digit, out var refId))
                throw new KeyNotFoundException(
                    $"No ref found for digit '{digit}'. Available: {string.Join(", ", refs.Keys)}");
            _ctx.Fla.Click(refId);
        }
    }

    [When("I press {string}")]
    public void WhenIPress(string key)
    {
        var refs = _ctx.SnapshotAndIndex();
        if (!refs.TryGetValue(key, out var refId))
            throw new KeyNotFoundException(
                $"Key '{key}' not found in snapshot. Available: {string.Join(", ", refs.Keys)}");
        _ctx.Fla.Click(refId);
    }

    // ── Then steps ───────────────────────────────────────────────────────────

    [Then("the display should show {int}")]
    public void ThenTheDisplayShouldShow(int expected)
    {
        var display = GetDisplayText().Replace(",", "");
        Assert.That(display, Is.EqualTo(expected.ToString()),
            $"Expected display '{expected}' but got '{display}'.");
    }

    [Then("the display should show {string}")]
    public void ThenTheDisplayShouldShowString(string expected)
    {
        var display = GetDisplayText();
        Assert.That(display, Does.Contain(expected),
            $"Expected display to contain '{expected}' but got '{display}'.");
    }

    [Then("the display should contain {string}")]
    public void ThenTheDisplayShouldContain(string expected)
    {
        var display = GetDisplayText();
        Assert.That(display, Does.Contain(expected),
            $"Expected display to contain '{expected}' but got '{display}'.");
    }

    [Then("the display should show an error")]
    public void ThenTheDisplayShouldShowAnError()
    {
        var display = GetDisplayText();
        Assert.That(display,
            Does.Contain("Error").IgnoreCase.Or.Contain("Cannot").IgnoreCase,
            $"Expected display to contain 'Error' or 'Cannot' but got '{display}'.");
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private string GetDisplayText()
    {
        var refs = _ctx.SnapshotAndIndex();
        if (!refs.TryGetValue("display", out var refId))
            throw new InvalidOperationException(
                "Could not find 'display' element in snapshot.");
        var raw = _ctx.Fla.GetText(refId);
        // The display's accessibility Name is "Display is <value>" — strip the prefix.
        const string prefix = "Display is ";
        return raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? raw[prefix.Length..] : raw;
    }

    private void ComputeUnary(int n, string op)
    {
        var refs = _ctx.SnapshotAndIndex();
        var keys = CalculatorContext.ToDigits(n).Append(op).ToArray();
        _ctx.PressSequence(refs, keys);
    }

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
