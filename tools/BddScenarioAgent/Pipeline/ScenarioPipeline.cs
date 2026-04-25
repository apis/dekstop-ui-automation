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

        for (var attempt = 1; attempt <= opts.MaxRetries; attempt++)
        {
            logger.LogInformation("Pipeline attempt {Attempt}/{Max}", attempt, opts.MaxRetries);

            // ── Stage 1: Analyze ──────────────────────────────────────────────────
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
                Analyze BDD test coverage.

                Feature file     : {opts.FeatureFile}
                Step definitions : {opts.StepsDir}

                Read both locations using the available tools, then produce your JSON coverage analysis.
                """;

            var analysis = GetText(await analyzerAgent.RunAsync(analyzerTask, cancellationToken: ct));
            logger.LogDebug("Analysis result: {Summary}", Truncate(analysis, 400));

            if (dryRun)
            {
                logger.LogInformation("Dry-run mode — skipping generation and review");
                return $"=== Coverage Analysis ===\n{analysis}";
            }

            // ── Stage 2: Generate ─────────────────────────────────────────────────
            logger.LogInformation("Stage 2 – Generating new scenarios");

            var generatorAgent = chatClient.AsAIAgent(
                instructions: SystemPrompts.Generator,
                tools:
                [
                    AIFunctionFactory.Create(FileSystemTools.ReadFile),
                    AIFunctionFactory.Create(GherkinTools.ReadStepPatterns),
                    AIFunctionFactory.Create(GherkinTools.AppendScenarios),
                    AIFunctionFactory.Create(GherkinTools.ProposeStep),
                ]);

            var generatorTask =
                $"""
                Add new Gherkin scenarios to the Calculator feature file.

                Coverage analysis (Stage 1 output):
                {analysis}

                Feature file     : {opts.FeatureFile}
                Step definitions : {opts.StepsDir}
                Proposals file   : {opts.ProposalsFile}

                Steps:
                1. Call read_file to read the current feature file
                2. Call read_step_patterns to confirm available step vocabulary
                3. Generate 6–12 new, distinct scenarios targeting the identified gaps
                4. Call append_scenarios once with all new scenarios
                5. For any step pattern that does not exist, call propose_step
                """;

            var genResult = GetText(await generatorAgent.RunAsync(generatorTask, cancellationToken: ct));
            logger.LogInformation("Generator summary: {S}", Truncate(genResult, 200));

            // ── Stage 3: Review ───────────────────────────────────────────────────
            logger.LogInformation("Stage 3 – Reviewing changes");

            var reviewerAgent = chatClient.AsAIAgent(
                instructions: SystemPrompts.Reviewer,
                tools:
                [
                    AIFunctionFactory.Create(FileSystemTools.ReadFile),
                    AIFunctionFactory.Create(GherkinTools.ReadStepPatterns),
                ]);

            var reviewerTask =
                $"""
                Review the scenarios recently added to the feature file.

                Feature file     : {opts.FeatureFile}
                Step definitions : {opts.StepsDir}

                Read the current state of both and output your JSON review.
                """;

            var reviewJson = GetText(await reviewerAgent.RunAsync(reviewerTask, cancellationToken: ct));
            logger.LogDebug("Review JSON: {R}", Truncate(reviewJson, 400));

            var review = ParseReview(reviewJson);

            if (review?.Approved != false)
            {
                if (review is null)
                    logger.LogWarning("Could not parse review JSON — accepting result");
                else
                    logger.LogInformation("Review approved: {Feedback}", review.Feedback);

                return BuildSummary(analysis, genResult, reviewJson);
            }

            logger.LogWarning(
                "Review rejected on attempt {A}: {Issues}",
                attempt, string.Join("; ", review.Issues));

            if (attempt < opts.MaxRetries)
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
        }

        return $"Pipeline finished after {opts.MaxRetries} attempts " +
               $"(check '{opts.FeatureFile}' and '{opts.ProposalsFile}' for output).";
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the text content from an AgentResponse.
    /// Tries Messages first (Microsoft.Extensions.AI ChatMessage.Text), then ToString() as fallback.
    /// </summary>
    private static string GetText(AgentResponse response)
    {
        if (response.Messages is not null)
        {
            foreach (var msg in response.Messages.Reverse())
            {
                var t = msg.Text;
                if (!string.IsNullOrWhiteSpace(t)) return t;
            }
        }
        return response.ToString() ?? string.Empty;
    }

    private static ReviewResult? ParseReview(string json)
    {
        try
        {
            // LLMs sometimes prefix JSON with explanatory text — skip to the first '{'
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

    private static string BuildSummary(string analysis, string genResult, string review) =>
        $"""
        === Coverage Analysis ===
        {Truncate(analysis, 1000)}

        === Generation Result ===
        {genResult}

        === Review ===
        {Truncate(review, 500)}
        """;

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
