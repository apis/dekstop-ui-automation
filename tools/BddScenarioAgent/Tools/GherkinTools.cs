using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;

namespace BddScenarioAgent.Tools;

public static class GherkinTools
{
    // Matches [Given("...")] / [When("...")] / [Then("...")] attribute lines
    private static readonly Regex StepAttrRegex =
        new(@"\[(Given|When|Then)\(""([^""]+)""\)\]", RegexOptions.Compiled);

    [Description(
        "Scans a directory for Reqnroll step-definition .cs files and returns " +
        "all available step patterns grouped by Given / When / Then")]
    public static string ReadStepPatterns(
        [Description("Directory path containing .cs step definition files")] string directory)
    {
        if (!Directory.Exists(directory))
            return $"Error: directory not found at '{directory}'";

        var groups = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Given"] = [],
            ["When"]  = [],
            ["Then"]  = [],
        };

        foreach (var file in Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories))
        foreach (var line in File.ReadLines(file))
        {
            var m = StepAttrRegex.Match(line);
            if (m.Success)
                groups[m.Groups[1].Value].Add(m.Groups[2].Value);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Step patterns in '{directory}':");
        foreach (var (kw, patterns) in groups)
        {
            sb.AppendLine($"\n{kw}:");
            foreach (var p in patterns)
                sb.AppendLine($"  [{kw}(\"{p}\")]");
        }
        return sb.ToString();
    }

    private static string StripMarkdownFences(string text)
    {
        var s = text.Trim();
        if (!s.StartsWith("```")) return s;
        // Drop the opening fence line (```gherkin, ```feature, ``` etc.)
        var firstNewline = s.IndexOf('\n');
        if (firstNewline >= 0) s = s[(firstNewline + 1)..];
        // Drop the closing fence
        var lastFence = s.LastIndexOf("```");
        if (lastFence >= 0) s = s[..lastFence];
        return s.Trim();
    }

    [Description(
        "Appends new Gherkin scenarios to the end of a .feature file. " +
        "Call this once with all generated scenarios after confirming they are correct.")]
    public static string AppendScenarios(
        [Description("Path to the .feature file to extend")] string path,
        [Description("Valid Gherkin text to append (scenarios, outlines, and/or a section comment)")] string gherkin)
    {
        if (!File.Exists(path))
            return $"Error: feature file not found at '{path}'";

        var existing = File.ReadAllText(path).TrimEnd();
        // Strip any markdown code fences the LLM may have wrapped the Gherkin in
        var block = StripMarkdownFences(gherkin).Trim();
        File.WriteAllText(path, existing + "\n\n" + block + "\n");

        var lines = block.Split('\n').Length;
        return $"Appended {lines} lines to '{path}'";
    }

    [Description(
        "Records a proposed new step definition in the proposals markdown file. " +
        "Use this when a generated scenario requires a step pattern not yet implemented in C#.")]
    public static string ProposeStep(
        [Description("Path to the proposals markdown file (will be created if it does not exist)")] string path,
        [Description("The Gherkin step pattern, e.g. 'When I press the {word} key'")] string pattern,
        [Description("What this step should do when executed during a test run")] string description,
        [Description("Hint for implementing the step in C# with Reqnroll, referencing FlaUiEngine if applicable")] string implementationHint)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var isNew = !File.Exists(path);
        using var w = new StreamWriter(path, append: true);

        if (isNew)
        {
            w.WriteLine("# Proposed New Step Definitions");
            w.WriteLine();
            w.WriteLine("Add these to `tests/CalcAutomation.BDD/StepDefinitions/CalculatorSteps.cs`:");
            w.WriteLine();
        }

        w.WriteLine($"## `{pattern}`");
        w.WriteLine();
        w.WriteLine($"**Purpose:** {description}");
        w.WriteLine();
        w.WriteLine($"**Implementation hint:**");
        w.WriteLine($"```csharp");
        w.WriteLine(implementationHint);
        w.WriteLine($"```");
        w.WriteLine();

        return $"Proposed step '{pattern}' written to '{path}'";
    }
}
