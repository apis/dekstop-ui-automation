using System.Text.Json;
using BddScenarioAgent.Config;
using BddScenarioAgent.Prompts;
using BddScenarioAgent.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace BddScenarioAgent.Pipeline;

public sealed class ScenarioPipeline(ILogger<ScenarioPipeline> logger)
{
    private sealed record ReviewResult(
        bool     Approved,
        string[] Issues,
        string[] MissingSteps,
        string   Feedback);

    public async Task<string> RunAsync(AgentOptions opts, bool dryRun = false,
        CancellationToken ct = default)
    {
        var chatClient = new OllamaChatClient(new Uri(opts.OllamaUrl), opts.Model);

        // ── Stage 0: Explorer (once) ──────────────────────────────────────────
        logger.LogInformation("Stage 0 – Exploring Calculator controls");

        var explorerAgent = chatClient.AsAIAgent(
            instructions: SystemPrompts.Explorer,
            tools:
            [
                AIFunctionFactory.Create(CalcExplorerTools.ExploreCalculatorModes),
                AIFunctionFactory.Create(CalcExplorerTools.SnapshotCalculator),
            ]);

        var controlInventory = GetText(await explorerAgent.RunAsync(
            "Call explore_calculator_modes and return the full control inventory for all Calculator modes.",
            cancellationToken: ct));

        logger.LogInformation("Explorer complete ({Len} chars)", controlInventory.Length);
        logger.LogDebug("Control inventory:\n{Inv}", Truncate(controlInventory, 800));

        if (dryRun)
        {
            logger.LogInformation("Dry-run — stopping after exploration");
            return $"=== Control Inventory ===\n{controlInventory}";
        }

        // ── Iterative Analyze → Generate → Fix → Review loop ─────────────────
        var totalAdded = 0;
        var iterationResults = new List<string>();

        for (var iteration = 1; iteration <= opts.Iterations; iteration++)
        {
            logger.LogInformation("=== Iteration {I}/{Max} ===", iteration, opts.Iterations);

            var scenariosBefore = CountScenarios(opts.FeatureFile);

            // ── Stage 1: Analyze ──────────────────────────────────────────────
            logger.LogInformation("Stage 1 – Analyzing coverage");

            var analyzerAgent = chatClient.AsAIAgent(
                instructions: SystemPrompts.Analyzer,
                tools:
                [
                    AIFunctionFactory.Create(FileSystemTools.ReadFile),
                    AIFunctionFactory.Create(GherkinTools.ReadStepPatterns),
                ]);

            var analyzerTask =
                $"""
                Analyze BDD test coverage using the known control inventory below.

                Control inventory (from live Calculator exploration):
                {Truncate(controlInventory, 3000)}

                Feature file     : {opts.FeatureFile}
                Step definitions : {opts.StepsDir}

                Read both locations using the available tools, then produce your JSON coverage analysis.
                Focus on gaps not yet covered in previous iterations — find NEW scenarios only.
                """;

            var analysis = GetText(await analyzerAgent.RunAsync(analyzerTask, cancellationToken: ct));
            logger.LogDebug("Analysis:\n{A}", Truncate(analysis, 600));

            // ── Stage 2a: Implement missing step definitions ──────────────────
            logger.LogInformation("Stage 2a – Implementing missing step definitions");

            if (!string.IsNullOrWhiteSpace(opts.StepsFile))
            {
                var stepTools = new List<AIFunction>
                {
                    AIFunctionFactory.Create(StepWriterTools.ListDefinedSteps),
                    AIFunctionFactory.Create(StepWriterTools.AddStepDefinition),
                };
                if (!string.IsNullOrWhiteSpace(opts.BddProject))
                    stepTools.Add(AIFunctionFactory.Create(BuildTools.BuildProject));

                var stepAgent = chatClient.AsAIAgent(
                    instructions: SystemPrompts.StepImplementer,
                    tools: [.. stepTools]);

                var stepTask =
                    $"""
                    Implement any missing Reqnroll step definitions identified in this coverage analysis.

                    Coverage analysis:
                    {Truncate(analysis, 3000)}

                    Steps file  : {opts.StepsFile}
                    BDD project : {opts.BddProject}
                    """;

                var stepResult = GetText(await stepAgent.RunAsync(stepTask, cancellationToken: ct));
                logger.LogInformation("Step implementer: {R}", Truncate(stepResult, 200));
            }

            // ── Stage 2b: Write new Gherkin scenarios ─────────────────────────
            logger.LogInformation("Stage 2b – Writing new Gherkin scenarios");

            var writerTools = new List<AIFunction>
            {
                AIFunctionFactory.Create(FileSystemTools.ReadFile),
                AIFunctionFactory.Create(GherkinTools.AppendScenarios),
                AIFunctionFactory.Create(GherkinTools.ProposeStep),
            };
            if (!string.IsNullOrWhiteSpace(opts.StepsFile))
                writerTools.Add(AIFunctionFactory.Create(StepWriterTools.ListDefinedSteps));
            else
                writerTools.Add(AIFunctionFactory.Create(GherkinTools.ReadStepPatterns));

            var writerAgent = chatClient.AsAIAgent(
                instructions: SystemPrompts.ScenarioWriter,
                tools: [.. writerTools]);

            var writerTask =
                $"""
                Write new Gherkin scenarios to the feature file based on this coverage analysis.

                Coverage analysis:
                {Truncate(analysis, 3000)}

                Feature file : {opts.FeatureFile}
                Steps file   : {(string.IsNullOrWhiteSpace(opts.StepsFile) ? opts.StepsDir : opts.StepsFile)}
                """;

            var genResult = GetText(await writerAgent.RunAsync(writerTask, cancellationToken: ct));
            logger.LogInformation("Scenario writer: {S}", Truncate(genResult, 200));

            // ── Stage 2b: Fix (if build broken after generation) ──────────────
            if (!string.IsNullOrWhiteSpace(opts.BddProject))
            {
                var buildOutput = BuildTools.BuildProject(opts.BddProject);
                if (buildOutput.Contains("FAILURE"))
                {
                    logger.LogWarning("Build failed after generation — invoking Fixer agent");

                    var fixerAgent = chatClient.AsAIAgent(
                        instructions: SystemPrompts.Fixer,
                        tools:
                        [
                            AIFunctionFactory.Create(FileSystemTools.ReadFile),
                            AIFunctionFactory.Create(FileSystemTools.WriteFile),
                            AIFunctionFactory.Create(BuildTools.BuildProject),
                        ]);

                    var fixerResult = GetText(await fixerAgent.RunAsync(
                        $"""
                        The BDD project failed to build. Fix all compilation errors.

                        Build output:
                        {Truncate(buildOutput, 3000)}

                        BDD project path: {opts.BddProject}
                        """,
                        cancellationToken: ct));

                    logger.LogInformation("Fixer: {R}", Truncate(fixerResult, 200));
                }
                else
                {
                    logger.LogInformation("Build OK after generation");
                }
            }

            // ── Stage 3: Review ───────────────────────────────────────────────
            logger.LogInformation("Stage 3 – Reviewing changes");

            var reviewerTools = new List<AIFunction>
            {
                AIFunctionFactory.Create(FileSystemTools.ReadFile),
                AIFunctionFactory.Create(GherkinTools.ReadStepPatterns),
                AIFunctionFactory.Create(StepWriterTools.ListDefinedSteps),
            };

            if (!string.IsNullOrWhiteSpace(opts.BddProject))
                reviewerTools.Add(AIFunctionFactory.Create(BuildTools.BuildProject));

            var reviewerAgent = chatClient.AsAIAgent(
                instructions: SystemPrompts.Reviewer,
                tools: [.. reviewerTools]);

            var reviewerTask =
                $"""
                Review the scenarios recently added to the feature file.

                Feature file     : {opts.FeatureFile}
                Step definitions : {opts.StepsDir}
                Steps file       : {opts.StepsFile}
                BDD project      : {opts.BddProject}

                Read the current state and output your JSON review.
                """;

            var reviewJson = GetText(await reviewerAgent.RunAsync(reviewerTask, cancellationToken: ct));
            var review = ParseReview(reviewJson);

            var scenariosAfter = CountScenarios(opts.FeatureFile);
            var addedThisIter  = scenariosAfter - scenariosBefore;
            totalAdded        += Math.Max(0, addedThisIter);

            logger.LogInformation(
                "Iteration {I} complete: {Before}→{After} scenarios (+{Added}). Review: {Status}",
                iteration, scenariosBefore, scenariosAfter, addedThisIter,
                review?.Approved == true ? "approved" : review is null ? "unparseable" : "rejected");

            if (review?.Approved == false)
                logger.LogWarning("Review issues: {Issues}", string.Join("; ", review.Issues));

            iterationResults.Add(
                $"Iteration {iteration}: +{addedThisIter} scenarios. " +
                (review?.Feedback ?? "(no review feedback)"));

            // Stop early if no new scenarios were added — coverage saturated
            if (addedThisIter <= 0 && iteration > 1)
            {
                logger.LogInformation("No new scenarios added — coverage appears saturated. Stopping.");
                break;
            }
        }

        return BuildSummary(opts.FeatureFile, totalAdded, iterationResults);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string GetText(AgentResponse response)
    {
        if (response.Messages is not null)
        {
            // Prefer the last assistant message that contains plain text (not a tool call blob)
            foreach (var msg in response.Messages.Reverse())
            {
                if (msg.Role != ChatRole.Assistant) continue;
                // Skip messages whose text looks like a raw tool-call JSON object
                var t = msg.Text;
                if (string.IsNullOrWhiteSpace(t)) continue;
                var trimmed = t.TrimStart();
                if (trimmed.StartsWith("{\"call_id") || trimmed.StartsWith("{\"name")) continue;
                return t;
            }
            // Fallback: any non-empty text from any role
            foreach (var msg in response.Messages.Reverse())
            {
                var t = msg.Text;
                if (!string.IsNullOrWhiteSpace(t)) return t;
            }
        }
        return response.ToString() ?? string.Empty;
    }

    private static int CountScenarios(string featureFile)
    {
        if (!File.Exists(featureFile)) return 0;
        return File.ReadAllLines(featureFile)
            .Count(l => l.TrimStart().StartsWith("Scenario", StringComparison.OrdinalIgnoreCase));
    }

    private static ReviewResult? ParseReview(string json)
    {
        try
        {
            var start = json.IndexOf('{');
            if (start < 0) return null;

            using var doc = JsonDocument.Parse(json[start..]);
            var root = doc.RootElement;

            return new ReviewResult(
                Approved:     root.TryGetProperty("approved",      out var a)  && a.GetBoolean(),
                Issues:       root.TryGetProperty("issues",        out var i)
                                  ? [.. i.EnumerateArray().Select(x => x.GetString() ?? "")]
                                  : [],
                MissingSteps: root.TryGetProperty("missing_steps", out var ms)
                                  ? [.. ms.EnumerateArray().Select(x => x.GetString() ?? "")]
                                  : [],
                Feedback:     root.TryGetProperty("feedback",      out var f)
                                  ? f.GetString() ?? ""
                                  : "");
        }
        catch { return null; }
    }

    private static string BuildSummary(string featureFile, int totalAdded, List<string> iterations)
    {
        var finalCount = CountScenarios(featureFile);
        return $"""
            === BDD Scenario Generation Complete ===
            Total scenarios added : {totalAdded}
            Final scenario count  : {finalCount}

            Iterations:
            {string.Join("\n", iterations.Select(r => "  " + r))}
            """;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
