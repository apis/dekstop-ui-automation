using CalcAutomation.Core;
using NUnit.Framework;

namespace CalcAutomation.Tests;

/// <summary>
/// Direct FlaUI tests (no AI): launch calc.exe, snapshot the tree,
/// click buttons by ref, verify the display — deterministic and fast.
/// </summary>
[TestFixture]
[Apartment(ApartmentState.STA)] // UIA3 requires STA
public class CalculatorTests
{
    private FlaUiEngine _fla = null!;
    private string _handle = null!;

    [OneTimeSetUp]
    public void LaunchCalculator()
    {
        _fla = new FlaUiEngine();
        _handle = _fla.Launch("calc.exe");
        Thread.Sleep(1500); // give UWP time to finish rendering
    }

    [OneTimeTearDown]
    public void CloseCalculator()
    {
        _fla.Close(_handle);
        _fla.Dispose();
    }

    [Test]
    public void Snapshot_ReturnsNonEmptyTree()
    {
        var tree = _fla.Snapshot(_handle);
        System.IO.File.WriteAllText(@"C:\sandbox\test-auto\snapshot-debug.txt", tree);
        Console.WriteLine(tree);
        Assert.That(tree, Is.Not.Empty, "Snapshot should not be empty");
        Assert.That(tree, Does.Contain("[ref="), "Snapshot should contain element refs");
        Assert.That(tree, Does.Contain("Display"), "Snapshot should contain the display element");
    }

    [Test]
    [Order(1)]
    public void Addition_SevenPlusFive_Equals12()
    {
        var (display, seven, plus, five, equals) = FindCalcRefs();

        _fla.Click(seven);
        _fla.Click(plus);
        _fla.Click(five);
        _fla.Click(equals);

        var result = _fla.GetText(display);
        Console.WriteLine($"Display: {result}");
        Assert.That(result, Does.Contain("12"), $"Expected 12 but got: {result}");
    }

    [Test]
    [Order(2)]
    public void Subtraction_TenMinusFour_Equals6()
    {
        var (display, _, _, five, equals) = FindCalcRefs();
        var (_, _, _, _, _) = (display, "", "", "", ""); // re-snapshot for fresh refs
        var refs = SnapshotAndIndex();

        PressSequence(refs, "1", "0", "-", "4", "=");

        var result = _fla.GetText(refs["display"]);
        Console.WriteLine($"Display: {result}");
        Assert.That(result, Does.Contain("6"), $"Expected 6 but got: {result}");
    }

    [Test]
    [Order(3)]
    public void Multiplication_ThreeTimesThree_Equals9()
    {
        var refs = SnapshotAndIndex();
        PressSequence(refs, "3", "×", "3", "=");

        var result = _fla.GetText(refs["display"]);
        Console.WriteLine($"Display: {result}");
        Assert.That(result, Does.Contain("9"), $"Expected 9 but got: {result}");
    }

    [Test]
    [Order(4)]
    public void Division_TwentyDividedByFour_Equals5()
    {
        var refs = SnapshotAndIndex();
        PressSequence(refs, "2", "0", "÷", "4", "=");

        var result = _fla.GetText(refs["display"]);
        Console.WriteLine($"Display: {result}");
        Assert.That(result, Does.Contain("5"), $"Expected 5 but got: {result}");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Snapshots the window and builds a name→ref map for common calc elements.
    /// </summary>
    private Dictionary<string, string> SnapshotAndIndex()
    {
        var tree = _fla.Snapshot(_handle);
        var refs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Button name aliases → canonical key
        var buttonAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Zero"] = "0", ["One"] = "1", ["Two"] = "2", ["Three"] = "3",
            ["Four"] = "4", ["Five"] = "5", ["Six"] = "6", ["Seven"] = "7",
            ["Eight"] = "8", ["Nine"] = "9",
            ["Plus"] = "+", ["Minus"] = "-", ["Multiply by"] = "×",
            ["Divide by"] = "÷", ["Equals"] = "=",
            ["Clear entry"] = "CE", ["Clear"] = "C"
        };

        foreach (var line in tree.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var refMatch = System.Text.RegularExpressions.Regex.Match(line, @"\[ref=(\w+)");
            if (!refMatch.Success) continue;
            var refId = refMatch.Groups[1].Value;

            // Extract quoted element name
            var nameMatch = System.Text.RegularExpressions.Regex.Match(line, "\"([^\"]+)\"");
            if (!nameMatch.Success) continue;
            var name = nameMatch.Groups[1].Value;

            // Map display
            if (name.StartsWith("Display is", StringComparison.OrdinalIgnoreCase))
                refs["display"] = refId;

            // Map buttons by alias
            foreach (var (alias, key) in buttonAliases)
                if (name.StartsWith(alias, StringComparison.OrdinalIgnoreCase))
                    refs[key] = refId;
        }

        return refs;
    }

    private void PressSequence(Dictionary<string, string> refs, params string[] keys)
    {
        // Clear first
        if (refs.TryGetValue("CE", out var ce)) _fla.Click(ce);
        else if (refs.TryGetValue("C", out var c)) _fla.Click(c);

        foreach (var key in keys)
        {
            if (!refs.TryGetValue(key, out var refId))
                throw new KeyNotFoundException($"No ref found for key '{key}'. Available: {string.Join(", ", refs.Keys)}");
            _fla.Click(refId);
        }
    }

    private (string display, string seven, string plus, string five, string equals) FindCalcRefs()
    {
        var refs = SnapshotAndIndex();
        return (
            refs.GetValueOrDefault("display") ?? throw new KeyNotFoundException("display"),
            refs.GetValueOrDefault("7") ?? throw new KeyNotFoundException("7"),
            refs.GetValueOrDefault("+") ?? throw new KeyNotFoundException("+"),
            refs.GetValueOrDefault("5") ?? throw new KeyNotFoundException("5"),
            refs.GetValueOrDefault("=") ?? throw new KeyNotFoundException("=")
        );
    }
}
