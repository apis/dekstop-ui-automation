# CalcAutomation — Windows UI Test Automation with FlaUI + Claude AI

A .NET 10 solution that automates `calc.exe` using two complementary approaches:

1. **Direct FlaUI** — deterministic, fast tests that click by accessibility ref IDs
2. **AI-driven agent** — natural-language test instructions executed by Claude via tool use

---

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│  Test Layer  (NUnit)                                        │
│  ┌─────────────────────┐  ┌───────────────────────────────┐ │
│  │  CalculatorTests    │  │  AgentCalculatorTests         │ │
│  │  (direct FlaUI)     │  │  (natural language → Claude)  │ │
│  └────────┬────────────┘  └──────────────┬────────────────┘ │
└───────────│──────────────────────────────│──────────────────┘
            │                              │
            ▼                              ▼
┌───────────────────────┐    ┌─────────────────────────────┐
│     FlaUiEngine       │◄───│      CalculatorAgent        │
│  Launch / Snapshot    │    │   (Anthropic tool-use loop) │
│  Click / GetText      │    └─────────────────────────────┘
│  Type / Close         │                 │
└───────────────────────┘                 ▼
            │                 ┌───────────────────────┐
            ▼                 │    AnthropicClient    │
     Windows UIA3             │  POST /v1/messages    │
     (FlaUI.UIA3)             │  with tool schemas    │
            │                 └───────────────────────┘
            ▼
       calc.exe
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

Claude reads this tree, maps names to refs, and issues tool calls — no coordinates ever involved.

---

## Project Structure

```
CalcAutomation/
├── CalcAutomation.slnx
├── docs/
│   └── README.md                      ← this file
├── src/
│   └── CalcAutomation.Core/
│       ├── FlaUiEngine.cs             ← FlaUI wrapper (launch/snapshot/click/get_text)
│       ├── AnthropicClient.cs         ← HTTP client for Anthropic Messages API
│       ├── CalculatorAgent.cs         ← AI agent tool-use loop
│       └── Models/
│           └── AnthropicModels.cs     ← Request / response types
└── tests/
    └── CalcAutomation.Tests/
        ├── CalculatorTests.cs         ← Direct FlaUI tests (no AI)
        └── AgentCalculatorTests.cs    ← AI-driven natural-language tests
```

---

## Prerequisites

| Requirement | Version |
|---|---|
| .NET SDK | 10.0+ |
| Windows | 10 / 11 (UIA3 required) |
| calc.exe | Ships with Windows |
| ANTHROPIC_API_KEY | Only for `AgentCalculatorTests` |

> **Note:** FlaUI.UIA3 5.x targets .NET Framework but runs fine on .NET 10 via COM interop. The NU1701 restore warnings are expected and harmless.

---

## Running the Tests

### Direct FlaUI tests (fast, no API key needed)

```bash
dotnet test --filter "Category!=Agent"
```

These tests:
- Launch `calc.exe` once per fixture
- Snapshot the accessibility tree
- Parse element names into a `name → ref` dictionary
- Click buttons by ref and assert the display value

### AI-driven tests (requires Anthropic API key)

```powershell
$env:ANTHROPIC_API_KEY = "sk-ant-..."
dotnet test --filter "Category=Agent"
```

These tests pass a plain-English instruction to `CalculatorAgent`, which runs a tool-use loop with Claude until the task is complete.

**Example:**
```
"Open calc.exe. Add 7 and 5. Tell me the result shown on the display."
→ Agent result: "The display shows 12."
```

---

## Core Components

### `FlaUiEngine`

Wraps `FlaUI.UIA3` with a snapshot-ref model.

| Method | Description |
|---|---|
| `Launch(app, args?)` | Start an application, returns handle like `"w1"` |
| `Snapshot(handle?)` | Walk the accessibility tree, assign refs, return text tree |
| `Click(ref)` | Invoke or mouse-click an element by ref |
| `GetText(ref)` | Read element Name or Value pattern |
| `Type(text, ref?)` | Focus element and type text |
| `Close(handle)` | Close the application |

> Refs are valid only until the next `Snapshot()` call on the same window.

### `CalculatorAgent`

Runs the Claude tool-use loop:

1. Send user instruction + tool schemas to Claude
2. Claude responds with `tool_use` blocks
3. Dispatch each tool call to `FlaUiEngine`
4. Append tool results and repeat until `stop_reason == "end_turn"`
5. Return Claude's final text answer

**Tools exposed to Claude** (matching the fla-ui MCP schema):
- `windows_launch`
- `windows_snapshot`
- `windows_click`
- `windows_get_text`
- `windows_type`
- `windows_close`

### `AnthropicClient`

Minimal HTTP client for the Anthropic Messages API — no third-party SDK dependency.

```
POST https://api.anthropic.com/v1/messages
x-api-key: <key>
anthropic-version: 2023-06-01
```

---

## Test Coverage

| Test | Type | Scenario |
|---|---|---|
| `Snapshot_ReturnsNonEmptyTree` | Direct | Accessibility tree is readable |
| `Addition_SevenPlusFive_Equals12` | Direct | 7 + 5 = 12 |
| `Subtraction_TenMinusFour_Equals6` | Direct | 10 − 4 = 6 |
| `Multiplication_ThreeTimesThree_Equals9` | Direct | 3 × 3 = 9 |
| `Division_TwentyDividedByFour_Equals5` | Direct | 20 ÷ 4 = 5 |
| `Agent_Adds_7_And_5` | Agent | NL: "Add 7 and 5" → 12 |
| `Agent_Multiplies_6_By_8` | Agent | NL: "Multiply 6 by 8" → 48 |
| `Agent_Performs_Chain_Calculation` | Agent | NL: "100 ÷ 4 − 3" → 22 |
| `Agent_Verifies_Division_By_Zero_Behavior` | Agent | NL: edge case exploration |

---

## Why FlaUI over Coordinate-Based Automation

| | Coordinate clicking | FlaUI + Ref |
|---|---|---|
| Window resize | Breaks | Works |
| DPI scaling | Breaks | Works |
| Element discovery | Manual pixel hunt | Accessibility tree |
| AI compatibility | Screenshots + vision model | Text tree + any model |
| CI headless support | Needs display server | Native UIA3 |

---

## Extending to Other Applications

`FlaUiEngine` is application-agnostic. To automate a different app:

```csharp
var handle = _fla.Launch(@"C:\Program Files\MyApp\app.exe");
var tree = _fla.Snapshot(handle);
// parse tree, find refs, interact
```

For AI-driven testing of any app, pass a different instruction to `CalculatorAgent.RunAsync()` — or point the system prompt at a different executable.

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
