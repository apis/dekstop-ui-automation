using System.ComponentModel;
using System.Diagnostics;

namespace BddScenarioAgent.Tools;

/// <summary>
/// Tools for building and running the BDD test project.
/// The agent uses these to close the loop: write step → build → confirm it compiles → run tests.
/// </summary>
public static class BuildTools
{
    [Description(
        "Builds a .NET project using 'dotnet build' and returns the full build output. " +
        "Call this after adding or modifying step definitions to verify they compile correctly. " +
        "Returns exit code, warnings, and any compilation errors.")]
    public static string BuildProject(
        [Description("Path to the .csproj file, .sln file, or a directory containing one")] string path)
        => RunDotnet($"build \"{path}\" --no-incremental", timeoutSeconds: 90);

    [Description(
        "Runs the BDD tests using 'dotnet test' and returns the results summary. " +
        "Use this to verify that newly added Gherkin scenarios execute correctly against the Calculator. " +
        "Requires the Windows Calculator to be accessible and Ollama to NOT be needed (tests use FlaUI directly).")]
    public static string RunTests(
        [Description("Path to the BDD test project or .sln")] string path,
        [Description("Optional NUnit filter expression, e.g. 'Category!=Agent&Category!=Ollama' to skip AI tests — leave empty to run all")] string filter = "")
    {
        var args = $"test \"{path}\"";
        if (!string.IsNullOrWhiteSpace(filter))
            args += $" --filter \"{filter}\"";
        return RunDotnet(args, timeoutSeconds: 300);
    }

    private static string RunDotnet(string arguments, int timeoutSeconds)
    {
        try
        {
            using var proc = new Process();
            proc.StartInfo = new ProcessStartInfo("dotnet", arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                WorkingDirectory       = Environment.CurrentDirectory,
            };
            proc.Start();

            // Read stdout and stderr concurrently to avoid deadlocking on full buffers
            var outTask = Task.Run(() => proc.StandardOutput.ReadToEnd());
            var errTask = Task.Run(() => proc.StandardError.ReadToEnd());

            var exited = proc.WaitForExit(timeoutSeconds * 1_000);
            if (!exited)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return $"dotnet {arguments}: timed out after {timeoutSeconds}s";
            }

            var output   = outTask.Result;
            var errors   = errTask.Result;
            var combined = (output + errors).Trim();
            var label    = proc.ExitCode == 0 ? "SUCCESS" : "FAILURE";

            return $"Exit {proc.ExitCode} ({label}):\n{Truncate(combined, 4000)}";
        }
        catch (Exception ex)
        {
            return $"Failed to run 'dotnet {arguments}': {ex.Message}";
        }
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "\n[... output truncated]";
}
