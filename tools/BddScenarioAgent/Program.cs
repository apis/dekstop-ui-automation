using BddScenarioAgent.Config;
using BddScenarioAgent.Pipeline;
using Microsoft.Extensions.Logging;

// ── Banner ────────────────────────────────────────────────────────────────────
Console.Error.WriteLine("bddgen 1.0.0 — Agentic BDD scenario generator (Microsoft Agent Framework + Ollama)");

if (args.Contains("--help") || args.Contains("-h"))
{
    PrintHelp();
    return 0;
}

// ── Parse options ─────────────────────────────────────────────────────────────
var featureFile   = GetOpt("--feature-file",   "tests/CalcAutomation.BDD/Features/Calculator.feature");
var stepsDir      = GetOpt("--steps-dir",      "tests/CalcAutomation.BDD/StepDefinitions");
var proposalsFile = GetOpt("--proposals-file", "tools/BddScenarioAgent/proposals/new_steps.md");
var model         = GetOpt("--model",          "gemma4:e4b");
var ollamaUrl     = GetOpt("--ollama-url",     "http://localhost:11434");
var logLevelStr   = GetOpt("--log-level",      "Information");
var dryRun        = args.Contains("--dry-run");
var maxTurns      = int.TryParse(GetOpt("--max-turns",    null), out var mt) ? mt : 20;
var maxRetries    = int.TryParse(GetOpt("--max-retries",  null), out var mr) ? mr : 3;

// ── Logging ───────────────────────────────────────────────────────────────────
if (!Enum.TryParse<LogLevel>(logLevelStr, ignoreCase: true, out var logLevel))
    logLevel = LogLevel.Information;

using var loggerFactory = LoggerFactory.Create(b =>
    b.AddConsole().SetMinimumLevel(logLevel));

var log = loggerFactory.CreateLogger("bddgen");

// ── Validate paths ────────────────────────────────────────────────────────────
if (!File.Exists(featureFile))
{
    log.LogError("Feature file not found: {Path}", featureFile);
    log.LogError("Run from the repository root or pass --feature-file <path>");
    return 1;
}

if (!Directory.Exists(stepsDir))
{
    log.LogError("Step definitions directory not found: {Path}", stepsDir);
    return 1;
}

var proposalsDir = Path.GetDirectoryName(Path.GetFullPath(proposalsFile));
if (!string.IsNullOrEmpty(proposalsDir))
    Directory.CreateDirectory(proposalsDir);

// ── Summary ───────────────────────────────────────────────────────────────────
log.LogInformation("Feature file    : {F}", featureFile);
log.LogInformation("Steps directory : {D}", stepsDir);
log.LogInformation("Proposals file  : {P}", proposalsFile);
log.LogInformation("Model           : {M} @ {U}", model, ollamaUrl);
log.LogInformation("Max turns       : {T}   Max retries: {R}", maxTurns, maxRetries);
log.LogInformation("Dry run         : {DR}", dryRun);

// ── Run pipeline ──────────────────────────────────────────────────────────────
var opts = new AgentOptions
{
    OllamaUrl     = ollamaUrl,
    Model         = model,
    FeatureFile   = featureFile,
    StepsDir      = stepsDir,
    ProposalsFile = proposalsFile,
    MaxTurns      = maxTurns,
    MaxRetries    = maxRetries,
};

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var pipeline = new ScenarioPipeline(loggerFactory.CreateLogger<ScenarioPipeline>());

try
{
    var result = await pipeline.RunAsync(opts, dryRun, cts.Token);
    Console.WriteLine();
    Console.WriteLine(result);
    return 0;
}
catch (OperationCanceledException)
{
    log.LogWarning("Cancelled by user");
    return 130;
}
catch (Exception ex)
{
    log.LogError(ex, "Pipeline failed");
    return 1;
}

// ── Helpers ───────────────────────────────────────────────────────────────────
string GetOpt(string flag, string? defaultValue)
{
    var idx = Array.IndexOf(args, flag);
    return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : defaultValue ?? string.Empty;
}

static void PrintHelp() => Console.WriteLine("""
    bddgen — Agentic BDD scenario generator
    Powered by Microsoft Agent Framework 1.0 + Gemma4 via Ollama

    Usage:
      dotnet run --project tools/BddScenarioAgent [-- options]
      bddgen [options]

    Options:
      --feature-file   <path>   .feature file to extend
                                  (default: tests/CalcAutomation.BDD/Features/Calculator.feature)
      --steps-dir      <path>   Directory containing Reqnroll step definition .cs files
                                  (default: tests/CalcAutomation.BDD/StepDefinitions)
      --proposals-file <path>   Markdown file for proposed new step definitions
                                  (default: tools/BddScenarioAgent/proposals/new_steps.md)
      --model          <name>   Ollama model name  (default: gemma4:e4b)
      --ollama-url     <url>    Ollama base URL    (default: http://localhost:11434)
      --max-turns      <n>      Max tool-use turns per agent stage  (default: 20)
      --max-retries    <n>      Max pipeline retry attempts         (default: 3)
      --log-level      <level>  Trace|Debug|Information|Warning|Error  (default: Information)
      --dry-run                 Analyze coverage only — do not write to the feature file
      --help, -h                Show this help

    Requires:
      Ollama running on localhost:11434 with model gemma4:e4b pulled
        ollama pull gemma4:e4b
        ollama serve

    Example:
      # Run from the repo root
      dotnet run --project tools/BddScenarioAgent

      # Dry-run to preview what would be added
      dotnet run --project tools/BddScenarioAgent -- --dry-run

      # Use a different model
      dotnet run --project tools/BddScenarioAgent -- --model qwen2.5:14b
    """);
