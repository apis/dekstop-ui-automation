namespace BddScenarioAgent.Config;

public sealed class AgentOptions
{
    public string OllamaUrl     { get; init; } = "http://localhost:11434";
    public string Model         { get; init; } = "gemma4:e4b";
    public string FeatureFile   { get; init; } = string.Empty;
    public string StepsDir      { get; init; } = string.Empty;
    public string StepsFile     { get; init; } = string.Empty;  // CalculatorSteps.cs path
    public string BddProject    { get; init; } = string.Empty;  // .csproj path for build validation
    public string ProposalsFile { get; init; } = "tools/BddScenarioAgent/proposals/new_steps.md";
    public int    MaxTurns      { get; init; } = 20;
    public int    MaxRetries    { get; init; } = 3;
    public int    Iterations    { get; init; } = 5;             // max exploration iterations
}
