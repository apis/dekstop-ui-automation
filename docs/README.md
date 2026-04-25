# CalcAutomation — Windows UI Test Automation with FlaUI + Claude AI

A .NET 10 solution that automates `calc.exe` using three complementary approaches:

1. **Direct FlaUI** — deterministic, fast tests that click by accessibility ref IDs
2. **AI-driven agent (Claude)** — natural-language instructions executed by Claude via tool use
3. **AI-driven agent (Ollama)** — same pattern, backed by a local model running in Ollama

A fourth layer, **BDD / Reqnroll**, wraps the direct FlaUI approach in Gherkin feature files so QA engineers can read and extend tests without writing C#.

A fifth layer, **BddScenarioAgent**, is an agentic CLI that autonomously extends the feature file by discovering live Calculator controls, identifying coverage gaps, implementing missing Reqnroll steps in C#, and writing new Gherkin scenarios — all driven by a local Ollama model.

---

## Architecture

```
┌──────────────────────────────────────────────────────────────────────────────┐
│  Test Layer  (NUnit)                                                         │
│                                                                              │
│  ┌─────────────────────┐  ┌───────────────────────┐  ┌──────────────────┐   │
│  │  CalculatorTests    │  │  AgentCalculatorTests  │  │ OllamaAgent      │   │
│  │  (direct FlaUI)     │  │  (Claude tool-use)    │  │ CalculatorTests  │   │
│  └────────┬────────────┘  └──────────┬────────────┘  └───────┬──────────┘   │
│           │                          │                        │              │
│  ┌────────┴──────────────┐           │                        │              │
│  │  CalcAutomation.BDD   │           │                        │              │
│  │  (Reqnroll/Gherkin)   │           │                        │              │
│  └────────┬──────────────┘           │                        │              │
└───────────│──────────────────────────│────────────────────────│──────────────┘
            │                          │                        │
            ▼                          ▼                        ▼
┌───────────────────────┐  ┌──────────────────────┐  ┌──────────────────────┐
│     FlaUiEngine       │◄─│   CalculatorAgent    │  │ OllamaCalculator     │
│  Launch / Snapshot    │  │  (Anthropic tool-use │  │ Agent                │
│  Click / GetText      │  │   loop)              │  │ (Ollama tool-use     │
│  Type / Close         │  └──────────┬───────────┘  │  loop)               │
└───────────────────────┘             │               └──────────┬───────────┘
            ▲                         ▼                          ▼
            │               ┌──────────────────────┐  ┌──────────────────────┐
            │               │   AnthropicClient    │  │    OllamaClient      │
            │               │  POST /v1/messages   │  │  POST /v1/chat/      │
            │               └──────────────────────┘  │  completions         │
            │                          │               └──────────────────────┘
            │               Anthropic API (cloud)      Ollama (localhost:11434)
            │
┌───────────┴──────────────────────────────────────────────────────────────┐
│  tools/BddScenarioAgent  (agentic CLI)                                   │
│                                                                          │
│  Stage 0: Explorer  ──→  FlaUI live snapshot of all Calculator modes    │
│  Stage 1: Analyzer  ──→  coverage gap report (JSON)                     │
│  Stage 2: Generator ──→  new Gherkin scenarios + C# step implementations│
│  Stage 2b: Fixer    ──→  auto-fixes compilation errors from new steps   │
│  Stage 3: Reviewer  ──→  validates Gherkin quality and step coverage    │
│                                                                          │
│  Iterates until no new scenarios are added (coverage saturation)         │
└──────────────────────────────────────────────────────────────────────────┘
```

### The Snapshot-Ref Pattern

Instead of guessing screen coordinates, the engine reads the **accessibility tree** and assigns each element a unique ref ID (e.g. `w1e15`). Interactions target refs, so tests are resilient to window moves and resizes.

**Example snapshot output:**
```
- window "Calculator" [ref=w1e1]
  - text "Display is 0" [ref=w1e15]
  - group "Number pad" [ref=w1e39]
    - button "Seven" [ref=w1e47]
    - button "Five"  [ref=w1e45]
  - group "Standard operators" [ref=w1e33]
    - button "Plus"   [ref=w1e37]
    - button "Equals" [ref=w1e38]
```

Both Claude and Ollama read this text tree, map names to refs, and issue tool calls — no coordinates ever involved.

---

## Project Structure

```
CalcAutomation/
├── CalcAutomation.slnx
├── CLAUDE.md                              ← Claude Code operating rules for this repo
├── docs/
│   └── README.md                          ← this file
├── src/
│   └── CalcAutomation.Core/
│       ├── FlaUiEngine.cs                 ← FlaUI wrapper (launch/snapshot/click/get_text)
│       ├── AnthropicClient.cs             ← HTTP client for Anthropic Messages API
│       ├── OllamaClient.cs                ← HTTP client for Ollama chat endpoint
│       ├── CalculatorAgent.cs             ← Claude AI agent tool-use loop
│       ├── OllamaCalculatorAgent.cs       ← Ollama AI agent tool-use loop
│       └── Models/
│           ├── AnthropicModels.cs         ← Anthropic request/response types
│           └── OllamaModels.cs            ← Ollama (OpenAI-compatible) types
├── tests/
│   ├── CalcAutomation.Tests/
│   │   ├── CalculatorTests.cs             ← Direct FlaUI tests (no AI)
│   │   ├── AgentCalculatorTests.cs        ← Claude AI tests
│   │   └── OllamaAgentCalculatorTests.cs  ← Ollama AI tests
│   └── CalcAutomation.BDD/
│       ├── Features/
│       │   └── Calculator.feature         ← Gherkin scenarios (edit this to add tests)
│       ├── StepDefinitions/
│       │   └── CalculatorSteps.cs         ← Gherkin → FlaUI bindings
│       ├── Hooks/
│       │   └── CalculatorHooks.cs         ← BeforeScenario / AfterScenario lifecycle
│       ├── Support/
│       │   └── CalculatorContext.cs       ← Per-scenario shared state + FlaUI helpers
│       ├── AssemblyAttributes.cs          ← STA + no-parallel constraints
│       └── reqnroll.json                  ← Reqnroll configuration
└── tools/
    └── BddScenarioAgent/
        ├── BddScenarioAgent.csproj        ← net10.0-windows; references CalcAutomation.Core
        ├── Program.cs                     ← CLI entry point and option parsing
        ├── Config/
        │   └── AgentOptions.cs            ← All configurable options
        ├── Pipeline/
        │   └── ScenarioPipeline.cs        ← Multi-stage iterative pipeline orchestration
        ├── Prompts/
        │   └── SystemPrompts.cs           ← System prompts for each agent stage
        └── Tools/
            ├── CalcExplorerTools.cs       ← FlaUI-based Calculator control discovery
            ├── StepWriterTools.cs         ← Reqnroll step definition insertion
            ├── BuildTools.cs              ← dotnet build / dotnet test wrappers
            ├── FileSystemTools.cs         ← read_file / write_file helpers
            └── GherkinTools.cs            ← Feature file and step pattern readers
```

---

## Prerequisites

| Requirement | Version | Required for |
|---|---|---|
| .NET SDK | 10.0+ | everything |
| Windows | 10 / 11 | UIA3 COM interop |
| calc.exe | Ships with Windows | all tests |
| `ANTHROPIC_API_KEY` env var | — | `AgentCalculatorTests` only |
| Ollama running on `localhost:11434` | — | `OllamaAgentCalculatorTests`, `BddScenarioAgent` |
| Ollama model `gemma4:e4b` pulled | — | `OllamaAgentCalculatorTests`, `BddScenarioAgent` |

> **FlaUI.UIA3 restore warning:** FlaUI.UIA3 5.x targets .NET Framework but runs fine on .NET 10 via COM interop. The `NU1701` restore warnings are expected and harmless.

### Installing Ollama

```powershell
# Install Ollama from https://ollama.com then:
ollama pull gemma4:e4b
ollama serve          # starts the server on localhost:11434
```

---

## Running the Tests

### Direct FlaUI tests (fast, no external dependencies)

```bash
dotnet test --filter "Category!=Agent&Category!=Ollama"
```

These tests launch `calc.exe` once per fixture, snapshot the accessibility tree, parse element names into a `name → ref` dictionary, click buttons by ref, and assert the display value.

### BDD / Gherkin tests (same mechanics, human-readable)

```bash
dotnet test tests/CalcAutomation.BDD
```

Or filter by project when running the full solution:

```bash
dotnet test --filter "FullyQualifiedName~CalcAutomation.BDD"
```

These tests run the same FlaUI interactions as the direct tests, but the test cases are defined as Gherkin scenarios in `Features/Calculator.feature`. **QA engineers only need to edit the feature file** — no C# knowledge required to add new arithmetic scenarios.

### Claude AI tests (requires Anthropic API key)

```powershell
$env:ANTHROPIC_API_KEY = "sk-ant-..."
dotnet test --filter "Category=Agent"
```

These tests pass plain-English instructions to `CalculatorAgent`, which runs a Claude tool-use loop until the task is complete.

**Example:**
```
"Open calc.exe. Add 7 and 5. Tell me the result shown on the display."
→ Agent result: "The display shows 12."
```

### Ollama AI tests (requires local Ollama + model)

```bash
dotnet test --filter "Category=Ollama"
```

Same pattern as the Claude tests but uses a locally-running Ollama instance. Tests skip automatically with a clear message if Ollama is unreachable — they never fail the suite just because the service is down.

**Note:** Ollama prompts are more prescriptive than Claude prompts (e.g., "Press Clear, then press Five, then Plus…") to compensate for local models being less capable at UI reasoning. The agent also handles a Gemma4-specific quirk where `finish_reason="stop"` is returned even when tool calls are present — see [Known Issues §8](#8-gemma4-emits-finish_reasonstop-even-with-tool-calls).

---

## BddScenarioAgent — Autonomous Test Generator

`tools/BddScenarioAgent` is a standalone .NET 10 CLI that autonomously extends `Calculator.feature` by running a multi-stage agentic pipeline powered by a local Ollama model (default: `gemma4:e4b`).

### How it works

1. **Stage 0 – Explorer**: Launches the Windows Calculator via FlaUI and snapshots its accessibility tree across all available modes (Standard, Scientific, Programmer, Date Calculation). Builds a complete control inventory.
2. **Stage 1 – Analyzer**: Reads the existing feature file and step definitions, compares them against the control inventory, and produces a JSON coverage-gap report.
3. **Stage 2 – Generator**: For each gap, either writes a new Gherkin scenario directly (if an existing step covers it) or first calls `AddStepDefinition` to implement the missing Reqnroll step in C#, then validates it compiles with `dotnet build`.
4. **Stage 2b – Fixer** *(if needed)*: If the build fails after adding a new step, a dedicated Fixer agent reads the error output, patches the file, and re-builds until it passes.
5. **Stage 3 – Reviewer**: Validates the new scenarios for correctness, step availability, and Gherkin quality. Reports approved/rejected with specific issue details.

The pipeline repeats for up to `--iterations` cycles. It stops early if no new scenarios were added in the last iteration (coverage saturation).

### Running the agent

```bash
# Run from the repository root (all defaults apply)
dotnet run --project tools/BddScenarioAgent

# Explore controls only — do not write to the feature file
dotnet run --project tools/BddScenarioAgent -- --dry-run

# Run 10 iterations with verbose logging
dotnet run --project tools/BddScenarioAgent -- --iterations 10 --log-level Debug

# Use a different Ollama model
dotnet run --project tools/BddScenarioAgent -- --model qwen2.5:14b
```

### CLI options

| Option | Default | Description |
|---|---|---|
| `--feature-file <path>` | `tests/CalcAutomation.BDD/Features/Calculator.feature` | `.feature` file to extend |
| `--steps-dir <path>` | `tests/CalcAutomation.BDD/StepDefinitions` | Directory with step definition `.cs` files |
| `--steps-file <path>` | `tests/CalcAutomation.BDD/StepDefinitions/CalculatorSteps.cs` | File where new steps are auto-implemented |
| `--bdd-project <path>` | `tests/CalcAutomation.BDD/CalcAutomation.BDD.csproj` | `.csproj` used to validate new steps compile |
| `--proposals-file <path>` | `tools/BddScenarioAgent/proposals/new_steps.md` | Markdown file for steps that could not be auto-implemented |
| `--iterations <n>` | `5` | Maximum scenario-generation iterations |
| `--max-turns <n>` | `20` | Maximum tool-use turns per agent stage |
| `--model <name>` | `gemma4:e4b` | Ollama model to use |
| `--ollama-url <url>` | `http://localhost:11434` | Ollama base URL |
| `--log-level <level>` | `Information` | `Trace\|Debug\|Information\|Warning\|Error` |
| `--dry-run` | — | Explore controls only; do not modify any files |

### Agent tools available

| Tool | Stage | Description |
|---|---|---|
| `explore_calculator_modes` | Explorer | FlaUI live snapshot of all Calculator modes |
| `snapshot_calculator` | Explorer | Single snapshot of current Calculator state |
| `read_file` | Analyzer, Generator, Reviewer, Fixer | Read any file from disk |
| `write_file` | Generator, Fixer | Write/overwrite a file |
| `read_step_patterns` | Analyzer | Scan a directory for Reqnroll `[Given/When/Then]` patterns |
| `append_scenarios` | Generator | Append Gherkin blocks to the feature file |
| `list_defined_steps` | Generator, Reviewer | List all step patterns with method signatures |
| `add_step_definition` | Generator | Insert a new `[When/Given/Then]` method into CalculatorSteps.cs |
| `build_project` | Generator, Fixer, Reviewer | Run `dotnet build` and return full output |
| `run_tests` | *(optional)* | Run `dotnet test` with optional NUnit filter |
| `propose_step` | Generator | Write a proposed step to the Markdown proposals file |

---

## Core Components

### `FlaUiEngine`

Wraps `FlaUI.UIA3` with a snapshot-ref model. Application-agnostic — works with any Windows process.

| Method | Description |
|---|---|
| `Launch(app, args?)` | Start an application, returns handle like `"w1"` |
| `Snapshot(handle?)` | Walk the accessibility tree, assign refs, return text tree |
| `Click(ref)` | Invoke pattern first (no focus required), falls back to mouse click |
| `GetText(ref)` | Read element Name or Value pattern |
| `Type(text, ref?)` | Focus element and type text |
| `Close(handle)` | Close the application (tries Close button, WindowPattern, then process kill) |

> Refs are valid only until the next `Snapshot()` call on the same window. Always re-snapshot before looking up a ref after any interaction that might change the tree.

### `CalculatorAgent`

Runs the Claude tool-use loop against Anthropic's hosted API:

1. Send user instruction + tool schemas to Claude (`POST /v1/messages`)
2. Claude responds with `tool_use` content blocks
3. Dispatch each tool call to `FlaUiEngine`
4. Append tool results and repeat until `stop_reason == "end_turn"`
5. Return Claude's final text answer

**Tools exposed to Claude:**
`windows_launch`, `windows_snapshot`, `windows_click`, `windows_get_text`, `windows_type`, `windows_focus`, `windows_close`

These match the `fla-ui` MCP server tool schema so the same FlaUiEngine can be used interchangeably from tests or from Claude Code via MCP.

### `OllamaCalculatorAgent`

Same tool-use loop pattern as `CalculatorAgent` but talks to a local Ollama instance via its **OpenAI-compatible** `/v1/chat/completions` endpoint. Key differences from the Claude agent:

- **Message format:** uses OpenAI `tool_calls` / `tool` role instead of Anthropic `tool_use` / `tool_result`
- **Argument normalization:** some local models serialize tool arguments as a JSON string rather than a JSON object; `NormalizeArgs()` handles both transparently
- **`finish_reason` quirk:** Gemma4 returns `finish_reason="stop"` even when tool calls are present; the agent checks for `tool_calls` presence before trusting `finish_reason`
- **More explicit prompts:** the system prompt walks through each step of the calculator workflow in detail (snapshot first, clear, click one button at a time) to compensate for local model limitations
- **Configurable model and URL** via constructor parameters (defaults: `gemma4:e4b`, `http://localhost:11434`)

### `AnthropicClient`

Minimal HTTP client for the Anthropic Messages API — no third-party SDK dependency.

```
POST https://api.anthropic.com/v1/messages
x-api-key: <key>
anthropic-version: 2023-06-01
```

### `OllamaClient`

HTTP client for Ollama's OpenAI-compatible chat endpoint.

```
POST http://localhost:11434/v1/chat/completions
```

Inserts the system prompt as the first message in the list (OpenAI convention) and sets `stream: false`.

### BDD Layer (`CalcAutomation.BDD`)

Built on **Reqnroll** (the .NET successor to SpecFlow), the BDD project exposes the same direct FlaUI tests as human-readable Gherkin scenarios.

**How it works:**

1. `CalculatorHooks` launches `calc.exe` in `[BeforeScenario]` and closes it in `[AfterScenario]`
2. `CalculatorContext` is injected into both the hook class and step-definition class, sharing the `FlaUiEngine` handle and ref-indexing helpers across the scenario lifetime
3. `CalculatorSteps` maps Gherkin phrases to FlaUI calls — QA never touches this file unless adding a new phrase pattern
4. `Calculator.feature` is the only file QA needs to edit

**Adding a new test case** (no C# required):

To add `8 + 2 = 10` as a parameterised row, add one line to the Examples table in `Calculator.feature`:

```gherkin
| 8 | + | 2 | 10 |
```

To add a new named scenario, copy an existing `Scenario:` block and change the numbers.

**Supported step phrases:**

| Pattern | Example | Notes |
|---|---|---|
| `I add {n} and {m}` | I add 7 and 5 | |
| `I subtract {n} from {m}` | I subtract 4 from 10 | computes minuend − subtrahend |
| `I multiply {n} by {m}` | I multiply 3 by 3 | |
| `I divide {n} by {m}` | I divide 20 by 4 | |
| `I calculate {n} {op} {m}` | I calculate 6 * 7 | operators: `+` `-` `*` `/` |
| `the display should show {n}` | the display should show 12 | integer; strips thousands separators before comparing |
| `the display should show {string}` | the display should show "0.25" | quoted; for decimal values |
| `the display should contain {string}` | the display should contain "Cannot divide by zero" | partial match; for error messages and repeating decimals |

---

## Test Coverage

### `CalcAutomation.Tests` — Direct FlaUI

| Test | Type | Scenario |
|---|---|---|
| `Snapshot_ReturnsNonEmptyTree` | Direct | Accessibility tree is readable and contains refs |
| `Addition_SevenPlusFive_Equals12` | Direct | 7 + 5 = 12 |
| `Subtraction_TenMinusFour_Equals6` | Direct | 10 − 4 = 6 |
| `Multiplication_ThreeTimesThree_Equals9` | Direct | 3 × 3 = 9 |
| `Division_TwentyDividedByFour_Equals5` | Direct | 20 ÷ 4 = 5 |
| `Agent_Adds_7_And_5` | Claude | NL: "Add 7 and 5" → 12 |
| `Agent_Multiplies_6_By_8` | Claude | NL: "Multiply 6 by 8" → 48 |
| `Agent_Performs_Chain_Calculation` | Claude | NL: "100 ÷ 4 − 3" → 22 |
| `Agent_Verifies_Division_By_Zero_Behavior` | Claude | NL: edge-case exploration |
| `Ollama_Adds_5_And_3` | Ollama | Step-by-step: 5 + 3 = 8 |
| `Ollama_Multiplies_4_By_7` | Ollama | Step-by-step: 4 × 7 = 28 |
| `Ollama_Subtracts_3_From_10` | Ollama | Step-by-step: 10 − 3 = 7 |
| `Ollama_Divides_20_By_4` | Ollama | Step-by-step: 20 ÷ 4 = 5 |

### `CalcAutomation.BDD` — Gherkin / Reqnroll

**Named scenarios:**

| Scenario | Input | Expected |
|---|---|---|
| Add two numbers | 7 + 5 | 12 |
| Subtract two numbers | 10 − 4 | 6 |
| Multiply two numbers | 3 × 3 | 9 |
| Divide two numbers | 20 ÷ 4 | 5 |
| Division by zero shows an error | 5 ÷ 0 | contains "Cannot divide by zero" |
| Subtracting a larger number gives a negative result | 3 − 10 | −7 |
| Multiplying any number by zero gives zero | 99999 × 0 | 0 |
| Division producing a decimal result | 1 ÷ 4 | "0.25" |
| Division producing a repeating decimal | 2 ÷ 3 | contains "0.6666666666" |

**Parameterised outline — additional arithmetic:**

| left | op | right | expected |
|---|---|---|---|
| 8 | + | 2 | 10 |
| 15 | − | 7 | 8 |
| 6 | × | 7 | 42 |
| 18 | ÷ | 3 | 6 |
| 100 | ÷ | 4 | 25 |
| 9 | × | 9 | 81 |

**Parameterised outline — boundary and sign-change values:**

| left | op | right | expected |
|---|---|---|---|
| 999999999 | + | 1 | 1000000000 |
| 2000000 | × | 3 | 6000000 |
| 100 | − | 200 | −100 |
| 7 | − | 7 | 0 |
| 0 | + | 0 | 0 |
| 1000000 | ÷ | 4 | 250000 |

---

## Why FlaUI over Coordinate-Based Automation

| | Coordinate clicking | FlaUI + Ref |
|---|---|---|
| Window resize | Breaks | Works |
| DPI scaling | Breaks | Works |
| Element discovery | Manual pixel hunt | Accessibility tree |
| AI compatibility | Screenshots + vision model | Text tree + any text model |
| CI headless support | Needs display server | Native UIA3 |

---

## Extending to Other Applications

`FlaUiEngine` is application-agnostic. To automate a different app:

```csharp
var handle = _fla.Launch(@"C:\Program Files\MyApp\app.exe");
var tree = _fla.Snapshot(handle);
// parse tree, find refs, interact
```

For AI-driven testing of any app, pass a different instruction to `CalculatorAgent.RunAsync()` or `OllamaCalculatorAgent.RunAsync()` — or point the system prompt at a different executable.

---

## Known Issues & Gotchas

These were discovered during development and are worth knowing before you hit them.

### 1. Target framework must be `net10.0-windows`, not `net10.0`

FlaUI.UIA3 wraps the Windows `Accessibility.dll` COM interop assembly, which is only available when targeting a Windows-specific TFM. Using `net10.0` results in:

```
System.IO.FileNotFoundException: Could not load file or assembly 'Accessibility, Version=4.0.0.0'
```

**Fix:** use `<TargetFramework>net10.0-windows</TargetFramework>` in every project that references FlaUI.

---

### 2. `calc.exe` is a UWP launcher stub — `Application.Launch` always fails

On Windows 10/11, `calc.exe` is not the real Calculator. It is a thin broker that launches the UWP app and then exits immediately. FlaUI's `Application.Launch("calc.exe")` therefore throws:

```
System.Exception: Could not find process with id: XXXXX
```

**Fix:** use `Application.LaunchStoreApp` with the Calculator's Application User Model ID (AUMID):

```csharp
Application.LaunchStoreApp("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App");
```

`FlaUiEngine.Launch` handles this automatically for `calc.exe`. For other UWP apps, find the AUMID with:

```powershell
Get-AppxPackage | Where-Object Name -like "*Calculator*" | Select-Object PackageFamilyName
```

---

### 3. UWP windows are hosted in `ApplicationFrameHost` — `GetMainWindow` returns `null`

Even after `LaunchStoreApp` succeeds, `app.GetMainWindow()` returns `null` because the Calculator's visible window HWND belongs to the `ApplicationFrameHost` process, not the Calculator's own process.

`GetAllTopLevelWindows` also fails for the same reason — it filters by the app's process ID, which doesn't own the window.

**Fix:** search the desktop element tree by window title instead:

```csharp
var desktop = _automation.GetDesktop();
var window = desktop.FindAllChildren()
    .FirstOrDefault(el => string.Equals(el.Name, "Calculator", StringComparison.OrdinalIgnoreCase));
```

`FlaUiEngine.Snapshot` does this automatically when `GetMainWindow` returns `null`.

---

### 4. Title search can match unrelated windows — use exact match first

A substring search for `"Calculator"` will also match a Notepad window titled `"CalculatorTests.cs - Notepad"`, returning the wrong window. Always prefer an exact name match, falling back to `StartsWith` only if no exact match is found.

```csharp
window = topLevel.FirstOrDefault(el => string.Equals(el.Name, hint, ...))
    ?? topLevel.FirstOrDefault(el => el.Name.StartsWith(hint, ...));
```

---

### 5. Some UIA3 properties throw `PropertyNotSupportedException`

Elements at the desktop level (and some shell elements) do not support all UIA3 properties. Accessing `el.AutomationId`, `el.Name`, or `el.ControlType` directly can throw:

```
FlaUI.Core.Exceptions.PropertyNotSupportedException: The requested property 'AutomationId [#30011]' is not supported
```

**Fix:** always use `TryGetValue`:

```csharp
var name = el.Properties.Name.TryGetValue(out var n) ? n : string.Empty;
var aid  = el.Properties.AutomationId.TryGetValue(out var id) ? id : string.Empty;
```

---

### 6. `el.Click()` uses the mouse — requires window focus

`AutomationElement.Click()` in FlaUI moves the mouse cursor to the element's centre and clicks. If the target window is not in the foreground (common during automated test runs), the click lands behind the active window and is ignored.

**Fix:** use the UIA **Invoke pattern** instead, which triggers the button programmatically with no focus requirement:

```csharp
var invoke = el.Patterns.Invoke.PatternOrDefault;
if (invoke != null)
    invoke.Invoke();   // reliable, no focus needed
else
    el.Click();        // fallback for non-invokable elements
```

`FlaUiEngine.Click` always tries `Invoke` first, matching the behaviour of the fla-ui MCP server.

---

### 7. Snapshot ref regex must not require an immediately-closing `]`

The snapshot format includes AutomationId alongside the ref:

```
- button "Seven" [ref=w1e47 id="num7Button"]
```

A regex like `\[ref=(\w+)\]` (requiring `]` right after the ref ID) will never match. Use:

```csharp
var refMatch = Regex.Match(line, @"\[ref=(\w+)");
```

---

### 8. Gemma4 emits `finish_reason="stop"` even with tool calls

When using Ollama with the `gemma4:e4b` model, the `/v1/chat/completions` response sometimes includes both `tool_calls` in the message **and** `finish_reason="stop"`. The OpenAI spec says `finish_reason` should be `"tool_calls"` when tool calls are present, but Gemma4 does not follow this.

**Symptom:** agent loop exits after the first tool call, ignoring the model's intent.

**Fix:** check for the presence of `tool_calls` in the message content *before* checking `finish_reason`:

```csharp
var toolCalls = message["tool_calls"]?.AsArray();
if (toolCalls != null && toolCalls.Count > 0)
{
    // execute tools regardless of finish_reason
}
else if (finishReason == "stop")
{
    break; // genuine end of turn
}
```

`OllamaCalculatorAgent` applies this check on every turn.

---

### 9. Some local models return tool arguments as a JSON string, not a JSON object

The OpenAI spec says `function.arguments` in a tool call should be a **JSON object** (parsed). Some local models (including older Ollama builds) return it as a **JSON string** — i.e., the arguments field itself is a quoted, escaped JSON string:

```json
{ "arguments": "{\"handle\": \"w1\"}" }   // string — needs parsing
{ "arguments": { "handle": "w1" } }       // object — ready to use
```

**Fix:** normalize before dispatching:

```csharp
JsonNode NormalizeArgs(JsonNode? raw) =>
    raw is JsonValue v && v.TryGetValue<string>(out var s)
        ? JsonNode.Parse(s)!
        : raw ?? new JsonObject();
```

`OllamaCalculatorAgent.NormalizeArgs()` handles both forms transparently.

---

### 10. BDD tests require STA thread and no parallel execution

Reqnroll with NUnit generates test fixtures that run on MTA threads by default. FlaUI.UIA3 requires **STA** (single-threaded apartment), and the Calculator window cannot be shared across parallel test runs.

**Fix:** add assembly-level NUnit attributes in `AssemblyAttributes.cs`:

```csharp
[assembly: Apartment(ApartmentState.STA)]
[assembly: Parallelizable(ParallelScope.None)]
```

---

### 11. Windows Calculator formats large numbers with thousands separators

The Calculator displays numbers ≥ 1,000 with comma separators (e.g. `1,000,000,000` instead of `1000000000`). A plain `Does.Contain("1000000000")` assertion will fail.

**Fix:** strip commas from the display text before comparing, while keeping the original in the failure message for readability:

```csharp
[Then("the display should show {int}")]
public void ThenTheDisplayShouldShow(int expected)
{
    var text = GetDisplayText();
    var normalized = text.Replace(",", "");
    Assert.That(normalized, Does.Contain(expected.ToString()),
        $"Expected display to contain '{expected}', but it shows: '{text}'");
}
```

The `{string}` and `{contains}` step variants compare against the raw display text and are not affected.

---

### 12. BddScenarioAgent FlaUI calls require an STA thread

The agent tools in `CalcExplorerTools` call FlaUI from within a Microsoft Agent Framework tool callback, which runs on a thread-pool thread (MTA). FlaUI.UIA3 COM interop requires STA.

**Fix:** wrap all FlaUI operations in a dedicated `Thread` with `SetApartmentState(ApartmentState.STA)` before starting:

```csharp
var thread = new Thread(() => { /* FlaUI work */ });
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join(TimeSpan.FromSeconds(60));
```

`CalcExplorerTools` applies this pattern for both `ExploreCalculatorModes` and `SnapshotCalculator`.
