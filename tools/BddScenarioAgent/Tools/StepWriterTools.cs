using System.ComponentModel;

namespace BddScenarioAgent.Tools;

/// <summary>
/// Tools for writing new Reqnroll step definitions directly into the C# step definition file.
/// The agent uses these to implement steps required by new scenarios, rather than just
/// proposing them — closing the loop from Gherkin to runnable test code.
/// </summary>
public static class StepWriterTools
{
    [Description(
        "Adds a new step-definition method to the Reqnroll CalculatorSteps.cs file. " +
        "The [Given/When/Then] attribute is added automatically — provide only the C# method code. " +
        "Step methods have access to: " +
        "_ctx.Fla (FlaUiEngine), " +
        "_ctx.Handle (string window handle), " +
        "_ctx.SnapshotAndIndex() (returns Dictionary<string,string> of button-label -> ref), " +
        "_ctx.PressSequence(refs, keys) (clears then clicks each key ref in order). " +
        "FlaUiEngine methods: Launch(app), Snapshot(handle), Click(ref), GetText(ref), Type(text,ref?), Close(handle). " +
        "Use GetDisplayText() (private helper in CalculatorSteps) to read the display value.")]
    public static string AddStepDefinition(
        [Description("Path to the CalculatorSteps.cs file")] string path,
        [Description("Step keyword: Given, When, or Then")] string keyword,
        [Description("Gherkin step pattern, e.g. 'I press the {word} key' or 'I switch to {word} mode'")] string pattern,
        [Description(
            "Full C# method declaration including access modifier, name, parameters, and body. " +
            "Example: 'public void WhenISwitchMode(string mode) { /* body */ }'")]
        string methodCode)
    {
        if (!File.Exists(path))
            return $"Error: step definitions file not found at '{path}'";

        // Reject patterns that contain raw double quotes — they would break the [When("...")] attribute syntax.
        // Use Reqnroll type parameters like {string} for values that appear quoted in Gherkin.
        if (pattern.Contains('"'))
            return $"Error: step pattern must not contain double-quote characters. " +
                   $"Use {{string}} or {{int}} type parameters instead. Rejected pattern: {pattern}";

        var content = File.ReadAllText(path);

        if (content.Contains($"\"{pattern}\""))
            return $"Step pattern '{pattern}' already exists in '{path}' — skipping";

        // Format: attribute on its own line, then the method indented 4 spaces
        var attribute   = $"    [{keyword}(\"{pattern}\")]";
        var methodLines = methodCode.Trim().Replace("\r\n", "\n").Split('\n');
        var indented    = string.Join("\n", methodLines.Select(l => "    " + l));
        var block       = $"\n{attribute}\n{indented}\n";

        // Insert before the private helper section to keep public steps grouped together
        const string helperMarker = "    // ── Helpers";
        var insertAt = content.LastIndexOf(helperMarker, StringComparison.Ordinal);
        if (insertAt < 0)
        {
            // Fallback: before the last closing brace of the class
            insertAt = content.LastIndexOf('}');
            if (insertAt < 0)
                return "Error: could not find insertion point — is this a valid CalculatorSteps.cs file?";
        }

        File.WriteAllText(path, content[..insertAt] + block + "\n" + content[insertAt..]);
        return $"Added [{keyword}(\"{pattern}\")] to '{path}'";
    }

    [Description(
        "Reads the current list of all step patterns already defined in a step definitions file, " +
        "including method signatures. Use this before adding a step to avoid duplicates.")]
    public static string ListDefinedSteps(
        [Description("Path to the step definitions .cs file")] string path)
    {
        if (!File.Exists(path))
            return $"Error: file not found at '{path}'";

        var lines  = File.ReadAllLines(path);
        var result = new System.Text.StringBuilder();
        result.AppendLine($"Defined steps in '{path}':");

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("[Given(") || line.StartsWith("[When(") || line.StartsWith("[Then("))
            {
                result.AppendLine($"  {line}");
                // Also show the method signature on the next non-empty line
                for (var j = i + 1; j < Math.Min(i + 3, lines.Length); j++)
                {
                    var next = lines[j].Trim();
                    if (next.Length > 0 && !next.StartsWith("["))
                    {
                        result.AppendLine($"    → {next}");
                        break;
                    }
                }
            }
        }

        return result.ToString();
    }
}
