using CalcAutomation.Core;
using System.Text.RegularExpressions;

namespace CalcAutomation.BDD.Support;

/// <summary>
/// Shared per-scenario state and FlaUI interaction helpers.
/// Reqnroll injects one instance of this class per scenario,
/// shared between hooks and step definitions.
/// </summary>
public class CalculatorContext
{
    public FlaUiEngine Fla { get; set; } = null!;
    public string Handle { get; set; } = null!;

    private static readonly Dictionary<string, string> ButtonAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Zero"] = "0",  ["One"] = "1",  ["Two"] = "2",   ["Three"] = "3",
            ["Four"] = "4",  ["Five"] = "5", ["Six"] = "6",   ["Seven"] = "7",
            ["Eight"] = "8", ["Nine"] = "9",
            ["Plus"] = "+",  ["Minus"] = "-",
            ["Multiply by"] = "×", ["Divide by"] = "÷",
            ["Equals"] = "=", ["Clear entry"] = "CE", ["Clear"] = "C"
        };

    /// <summary>
    /// Takes an accessibility-tree snapshot and returns a map of canonical key → ref ID
    /// (e.g. "7" → "w1e12", "+" → "w1e20", "display" → "w1e5").
    /// </summary>
    public Dictionary<string, string> SnapshotAndIndex()
    {
        var tree = Fla.Snapshot(Handle);
        var refs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in tree.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var refMatch = Regex.Match(line, @"\[ref=(\w+)");
            if (!refMatch.Success) continue;
            var refId = refMatch.Groups[1].Value;

            var nameMatch = Regex.Match(line, "\"([^\"]+)\"");
            if (!nameMatch.Success) continue;
            var name = nameMatch.Groups[1].Value;

            if (name.StartsWith("Display is", StringComparison.OrdinalIgnoreCase))
                refs["display"] = refId;

            foreach (var (alias, key) in ButtonAliases)
                if (name.StartsWith(alias, StringComparison.OrdinalIgnoreCase))
                    refs[key] = refId;
        }

        return refs;
    }

    /// <summary>
    /// Clears the calculator then clicks each key in sequence.
    /// </summary>
    public void PressSequence(Dictionary<string, string> refs, params string[] keys)
    {
        if (refs.TryGetValue("CE", out var ce)) Fla.Click(ce);
        else if (refs.TryGetValue("C", out var c)) Fla.Click(c);

        foreach (var key in keys)
        {
            if (!refs.TryGetValue(key, out var refId))
                throw new KeyNotFoundException(
                    $"No ref found for key '{key}'. Available: {string.Join(", ", refs.Keys)}");
            Fla.Click(refId);
        }
    }

    /// <summary>
    /// Maps common operator symbols (from Gherkin Examples) to the keys used in <see cref="PressSequence"/>.
    /// </summary>
    public static string MapOperator(string op) => op switch
    {
        "+" => "+",
        "-" => "-",
        "*" or "×" => "×",
        "/" or "÷" => "÷",
        _ => throw new ArgumentException($"Unknown operator '{op}'. Use +, -, *, /")
    };

    /// <summary>Splits a non-negative integer into its constituent digit strings.</summary>
    public static IEnumerable<string> ToDigits(int n) =>
        Math.Abs(n).ToString().Select(c => c.ToString());
}
