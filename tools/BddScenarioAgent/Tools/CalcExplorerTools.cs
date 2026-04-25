using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using CalcAutomation.Core;

namespace BddScenarioAgent.Tools;

/// <summary>
/// FlaUI-based tools for exploring the Windows Calculator's live accessibility tree.
/// Mirrors the fla-ui MCP server operations so the agent can discover real controls
/// the same way Claude Code does via MCP — but from within the .NET pipeline.
/// All methods run FlaUI on an STA thread as required by UIA3.
/// </summary>
public static class CalcExplorerTools
{
    private static readonly Regex RefRegex = new(@"\[ref=(\w+)", RegexOptions.Compiled);

    [Description(
        "Launches Windows Calculator, snapshots its accessibility tree in Standard mode, then " +
        "navigates to Scientific, Programmer, and Date Calculation modes and snapshots those too. " +
        "Returns the complete control inventory for all modes. Use this once at the start to " +
        "discover every button, input, and display element that can be covered by BDD scenarios.")]
    public static string ExploreCalculatorModes()
    {
        var output = new StringBuilder();
        Exception? threadEx = null;

        var thread = new Thread(() =>
        {
            try
            {
                using var fla = new FlaUiEngine();
                var handle = fla.Launch("calc.exe");

                // Standard mode — always available
                var snapshot = fla.Snapshot(handle);
                output.AppendLine("=== STANDARD MODE ===");
                output.AppendLine(snapshot);
                output.AppendLine();

                // Navigate to each additional mode via the hamburger navigation button
                foreach (var mode in new[] { "Scientific", "Programmer", "Date Calculation", "Graphing" })
                {
                    var navRef = FindRefByName(fla.Snapshot(handle), "Open Navigation");
                    if (navRef is null) break;  // navigation not available

                    fla.Click(navRef);
                    Thread.Sleep(500);

                    var menuRef = FindRefByName(fla.Snapshot(handle), mode);
                    if (menuRef is null) continue;  // mode not present in this version

                    fla.Click(menuRef);
                    Thread.Sleep(600);

                    output.AppendLine($"=== {mode.ToUpper()} MODE ===");
                    output.AppendLine(fla.Snapshot(handle));
                    output.AppendLine();
                }

                fla.Close(handle);
            }
            catch (Exception ex) { threadEx = ex; }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(60));

        if (threadEx is not null)
            return $"Calculator exploration failed: {threadEx.Message}\n\n" +
                   "Fallback: infer available controls from the existing step definitions and feature file.";

        return output.Length > 0
            ? output.ToString()
            : "No output — Calculator may not be accessible. Check that Ollama and Windows Calculator are available.";
    }

    [Description(
        "Takes a single accessibility-tree snapshot of the currently open Calculator window " +
        "without navigating or modifying state. Useful for checking current display value or " +
        "confirming which mode is active during iterative exploration.")]
    public static string SnapshotCalculator()
    {
        var result = string.Empty;
        Exception? threadEx = null;

        var thread = new Thread(() =>
        {
            try
            {
                using var fla = new FlaUiEngine();
                var handle = fla.Launch("calc.exe");
                result = fla.Snapshot(handle);
                fla.Close(handle);
            }
            catch (Exception ex) { threadEx = ex; }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(30));

        return threadEx is not null
            ? $"Snapshot failed: {threadEx.Message}"
            : result;
    }

    private static string? FindRefByName(string snapshot, string name)
    {
        foreach (var line in snapshot.Split('\n'))
        {
            if (!line.Contains(name, StringComparison.OrdinalIgnoreCase)) continue;
            var m = RefRegex.Match(line);
            if (m.Success) return m.Groups[1].Value;
        }
        return null;
    }
}
