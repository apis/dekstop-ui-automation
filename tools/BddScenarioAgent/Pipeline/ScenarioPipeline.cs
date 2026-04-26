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

            // ── Sanitize feature file (remove stray backticks from prior runs) ─
            SanitizeFeatureFile(opts.FeatureFile);

            // ── Pre-check: fix any lingering build failures before analyzing ──
            if (!string.IsNullOrWhiteSpace(opts.BddProject))
            {
                var preBuild = BuildTools.BuildProject(opts.BddProject);
                if (preBuild.Contains("FAILURE"))
                {
                    logger.LogWarning("Pre-iteration build check failed — running Fixer before analyzing");
                    var preErrors = string.Join("\n", preBuild.Split('\n')
                        .Where(l => l.Contains(": error CS") || l.Contains("Build FAILED"))
                        .Take(10));
                    logger.LogWarning("Pre-build errors:\n{E}", preErrors);

                    var preFixerAgent = chatClient.AsAIAgent(
                        instructions: SystemPrompts.Fixer,
                        tools:
                        [
                            AIFunctionFactory.Create(FileSystemTools.ReadFile),
                            AIFunctionFactory.Create(FileSystemTools.WriteFile),
                            AIFunctionFactory.Create(BuildTools.BuildProject),
                        ]);

                    var preFixResult = GetText(await preFixerAgent.RunAsync(
                        $"""
                        The BDD project has pre-existing build errors. Fix them before any new scenarios are added.

                        Build errors:
                        {Truncate(preBuild, 3000)}

                        BDD project path: {opts.BddProject}
                        Steps file path : {opts.StepsFile}
                        """,
                        cancellationToken: ct));

                    logger.LogInformation("Pre-iteration fixer: {R}", Truncate(preFixResult, 200));
                }
            }

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
            logger.LogInformation("Analysis complete ({Len} chars)", analysis.Length);
            logger.LogDebug("Analysis output:\n{A}", Truncate(analysis, 1200));

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

            // ── Stage 2b: Append scenarios ────────────────────────────────────
            // Try to extract proposed_scenarios from the analysis JSON and append directly —
            // this bypasses Gemma4's tendency to output Gherkin as prose instead of calling a tool.
            logger.LogInformation("Stage 2b – Appending new Gherkin scenarios");

            logger.LogDebug("Raw analysis output: {A}", Truncate(analysis, 1000));

            var genResult = string.Empty;
            var (proposedGherkin, extractReason) = ExtractProposedGherkin(analysis, iteration);

            if (!string.IsNullOrWhiteSpace(proposedGherkin))
            {
                var count = proposedGherkin.Split('\n').Count(l => l.TrimStart().StartsWith("Scenario:", StringComparison.OrdinalIgnoreCase));
                logger.LogInformation("Extracted {N} scenario(s) from analysis JSON — appending directly", count);
                genResult = GherkinTools.AppendScenarios(opts.FeatureFile, proposedGherkin);
                logger.LogInformation("Append result: {R}", genResult);
            }
            else
            {
                logger.LogWarning("Could not extract scenarios from analysis JSON ({Reason}) — falling back to writer agent", extractReason);

                var writerAgent = chatClient.AsAIAgent(
                    instructions: SystemPrompts.ScenarioWriter,
                    tools: [AIFunctionFactory.Create(GherkinTools.AppendScenarios)]);

                var writerTask =
                    $"""
                    Call append_scenarios with feature_file="{opts.FeatureFile}" and the new Gherkin scenarios below.
                    Do not read any files. Do not output text. Just call append_scenarios immediately.

                    {Truncate(analysis, 2000)}
                    """;

                genResult = GetText(await writerAgent.RunAsync(writerTask, cancellationToken: ct));
                logger.LogInformation("Scenario writer fallback: {S}", Truncate(genResult, 200));
            }

            // ── Stage 2c: Fix (if build broken after new steps/scenarios) ─────
            if (!string.IsNullOrWhiteSpace(opts.BddProject))
            {
                var buildOutput = BuildTools.BuildProject(opts.BddProject);
                if (buildOutput.Contains("FAILURE"))
                {
                    // Extract just the error lines for logging clarity
                    var errors = string.Join("\n", buildOutput.Split('\n')
                        .Where(l => l.Contains(": error CS") || l.Contains("Build FAILED"))
                        .Take(10));
                    logger.LogWarning("Build failed after generation:\n{Errors}", errors);

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

                        Build errors (from dotnet build):
                        {Truncate(buildOutput, 3000)}

                        BDD project path: {opts.BddProject}
                        Steps file path : {opts.StepsFile}
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
            logger.LogDebug("Raw reviewer output: {R}", Truncate(reviewJson, 800));

            var review = ParseReview(reviewJson);
            if (review is null)
                logger.LogWarning("Could not parse reviewer JSON. Raw output (500 chars): {R}", Truncate(reviewJson, 500));

            var scenariosAfter = CountScenarios(opts.FeatureFile);
            var addedThisIter  = scenariosAfter - scenariosBefore;
            totalAdded        += Math.Max(0, addedThisIter);

            logger.LogInformation(
                "Iteration {I} complete: {Before}→{After} scenarios (+{Added}). Review: {Status}",
                iteration, scenariosBefore, scenariosAfter, addedThisIter,
                review?.Approved == true ? "approved" : review is null ? "unparseable" : "rejected");

            if (review?.Approved == true)
                logger.LogInformation("Review feedback: {F}", review.Feedback);
            else if (review?.Approved == false)
                logger.LogWarning("Review rejected — issues: {Issues}", string.Join("; ", review.Issues));

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
            var jsonText = ExtractJsonObject(json);
            if (jsonText.Length == 0) return null;

            using var doc = JsonDocument.Parse(jsonText);
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

    private static (string Gherkin, string Reason) ExtractProposedGherkin(string analysis, int iteration)
    {
        var jsonText = ExtractJsonObject(analysis);
        if (jsonText.Length == 0)
            return (string.Empty, "no JSON object found in analysis output");

        JsonDocument doc;
        try { doc = JsonDocument.Parse(jsonText); }
        catch (JsonException ex)
            { return (string.Empty, $"JSON parse error: {ex.Message}"); }

        using (doc)
        {
            var root = doc.RootElement;

            if (!root.TryGetProperty("proposed_scenarios", out var scenariosEl))
                return (string.Empty, "analysis JSON has no 'proposed_scenarios' key");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"  # ── Generated scenarios — iteration {iteration} ──────────────────────────────────");
            sb.AppendLine();

            var added   = 0;
            var skipped = 0;
            foreach (var s in scenariosEl.EnumerateArray())
            {
                var title    = s.TryGetProperty("title",              out var t)  ? t.GetString() ?? "" : "";
                var usesOnly = !s.TryGetProperty("uses_existing_steps", out var u) || u.GetBoolean();
                var newPats  = s.TryGetProperty("new_step_patterns_needed", out var np)
                    ? np.EnumerateArray().Count()
                    : 0;

                if (!usesOnly && newPats > 0) { skipped++; continue; }
                if (string.IsNullOrWhiteSpace(title)) continue;

                sb.AppendLine($"  Scenario: {title}");

                if (s.TryGetProperty("steps_sketch", out var steps))
                {
                    foreach (var step in steps.EnumerateArray())
                    {
                        var line = step.GetString() ?? "";
                        if (line.TrimStart().StartsWith("Given the Calculator", StringComparison.OrdinalIgnoreCase))
                            continue;
                        sb.AppendLine($"    {line.TrimStart()}");
                    }
                }

                sb.AppendLine();
                added++;
            }

            if (added == 0)
                return (string.Empty, $"all {skipped} proposed scenario(s) require new step patterns");

            return (sb.ToString(), $"ok ({added} scenarios, {skipped} skipped — need new steps)");
        }
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

    // Extracts the first complete, balanced JSON object from arbitrary text,
    // then sanitizes invalid escape sequences so JsonDocument.Parse succeeds
    // even when the LLM emits \l, \s, \p etc. inside JSON strings.
    private static string ExtractJsonObject(string text)
    {
        var s = StripMarkdownFences(text);
        var start = s.IndexOf('{');
        if (start < 0) return string.Empty;

        var depth    = 0;
        var inString = false;
        var escape   = false;

        for (var i = start; i < s.Length; i++)
        {
            var c = s[i];
            if (escape)   { escape = false; continue; }
            if (c == '\\' && inString) { escape = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;

            if      (c == '{') depth++;
            else if (c == '}') { depth--; if (depth == 0) return SanitizeJsonEscapes(s[start..(i + 1)]); }
        }
        return string.Empty;
    }

    // Replace \X with X for any X that is not a valid JSON escape character.
    // Valid JSON escapes: \" \\ \/ \b \f \n \r \t \uXXXX
    private static readonly System.Text.RegularExpressions.Regex InvalidEscapeRegex =
        new(@"\\([^""\\\/bfnrtu])", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static string SanitizeJsonEscapes(string json) =>
        InvalidEscapeRegex.Replace(json, m => m.Groups[1].Value);

    // Removes backtick characters and markdown code fences from a feature file in-place.
    // Returns true if the file was modified.
    private bool SanitizeFeatureFile(string featureFile)
    {
        if (!File.Exists(featureFile)) return false;
        var lines   = File.ReadAllLines(featureFile);
        var cleaned = lines
            .Where(l => { var t = l.Trim(); return t != "`" && t != "``" && !t.StartsWith("```"); })
            .Select(l => l.Replace("`", ""))
            .ToArray();
        if (cleaned.Length == lines.Length && !lines.Any(l => l.Contains('`'))) return false;
        File.WriteAllLines(featureFile, cleaned);
        logger.LogInformation(
            "Sanitized feature file: removed {R} line(s) with backtick/fence content",
            lines.Length - cleaned.Length);
        return true;
    }

    private static string StripMarkdownFences(string text)
    {
        var s = text.Trim();
        if (!s.StartsWith("```")) return s;
        var firstNewline = s.IndexOf('\n');
        if (firstNewline >= 0) s = s[(firstNewline + 1)..];
        var lastFence = s.LastIndexOf("```");
        if (lastFence >= 0) s = s[..lastFence];
        return s.Trim();
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
