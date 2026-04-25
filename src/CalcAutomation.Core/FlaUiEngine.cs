using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;

namespace CalcAutomation.Core;

/// <summary>
/// FlaUI-backed automation engine that implements the snapshot-ref interaction model.
/// Refs (e.g. "w1e5") are stable within a single snapshot call; re-snapshot to refresh.
///
/// UWP note: Windows 11 Calculator (and similar UWP apps) host their window in the
/// ApplicationFrameHost process, so GetMainWindow() can't find it via the app process.
/// We fall back to a desktop element search by window title when needed.
/// </summary>
public sealed class FlaUiEngine : IDisposable
{
    private readonly UIA3Automation _automation = new();
    private readonly Dictionary<string, Application> _windows = new();
    private readonly Dictionary<string, string> _titleHints = new();   // handle → expected window title
    private readonly Dictionary<string, string> _processHints = new(); // handle → process name to kill on close
    private readonly Dictionary<string, AutomationElement> _refs = new();
    private readonly ILogger<FlaUiEngine> _logger;
    private int _wndCounter;
    private int _refCounter;
    private string? _lastHandle;

    public FlaUiEngine(ILogger<FlaUiEngine>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<FlaUiEngine>.Instance;
    }

    /// <summary>Launches an application and returns a window handle like "w1".</summary>
    public string Launch(string app, string[]? args = null)
    {
        _logger.LogInformation("Launching {App}", app);
        Application application;
        var titleHint = DeriveTitleHint(app);

        try
        {
            application = args?.Length > 0
                ? Application.Launch(BuildStartInfo(app, args))
                : Application.Launch(app);
            application.WaitWhileMainHandleIsMissing(TimeSpan.FromSeconds(10));
        }
        catch (Exception ex)
        {
            // UWP apps (e.g. calc.exe on Windows 10/11) use a launcher stub that exits immediately.
            // Fall back to LaunchStoreApp for known UWP apps.
            _logger.LogWarning("Direct launch failed ({Msg}), trying UWP/attach fallbacks", ex.Message);
            Thread.Sleep(1500);

            var aumid = ResolveAumid(app);
            if (aumid != null)
            {
                _logger.LogInformation("Activating UWP AUMID={Aumid}", aumid);
                application = Application.LaunchStoreApp(aumid);
                // UWP window is in ApplicationFrameHost — WaitWhileMainHandleIsMissing
                // may throw again; that's OK. We'll find the window via desktop search.
                try { application.WaitWhileMainHandleIsMissing(TimeSpan.FromSeconds(5)); } catch { }
            }
            else
            {
                var proc = Process.GetProcesses()
                    .FirstOrDefault(p => p.MainWindowTitle.Contains(titleHint, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException(
                        $"Could not attach to any process for '{app}'. " +
                        $"No process with window title containing '{titleHint}' was found.");
                application = Application.Attach(proc);
            }
        }

        Thread.Sleep(1000); // allow UWP/Win32 UI to fully render

        var handle = $"w{++_wndCounter}";
        _windows[handle] = application;
        _titleHints[handle] = titleHint;
        _processHints[handle] = ResolveProcessName(app) ?? string.Empty;
        _lastHandle = handle;
        _logger.LogInformation("Launched/attached, window handle={Handle} titleHint={Title}", handle, titleHint);
        return handle;
    }

    /// <summary>
    /// Walks the accessibility tree and returns a human-readable snapshot with ref IDs.
    /// Clears previous refs — old refs are invalid after a new snapshot.
    /// </summary>
    public string Snapshot(string? handle = null)
    {
        handle ??= _lastHandle ?? throw new InvalidOperationException("No window is open. Call Launch first.");
        var app = _windows.TryGetValue(handle, out var a) ? a
            : throw new KeyNotFoundException($"Window handle '{handle}' not found.");

        // Try GetMainWindow first (works for classic Win32 apps).
        // For UWP apps whose window lives in ApplicationFrameHost, fall back to desktop search.
        var window = GetWindow(app, handle);
        _refs.Clear();
        _refCounter = 0;

        var sb = new StringBuilder();
        WalkElement(window, 0, handle, sb);
        _logger.LogDebug("Snapshot produced {Lines} lines", sb.ToString().Split('\n').Length);
        return sb.ToString();
    }

    private AutomationElement GetWindow(Application app, string handle)
    {
        // Attempt 1: standard GetMainWindow (works for Win32 apps)
        try
        {
            var w = app.GetMainWindow(_automation, TimeSpan.FromSeconds(5));
            if (w != null) return w;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("GetMainWindow failed: {Msg}", ex.Message);
        }

        // Attempt 2: search all desktop top-level windows by title hint
        // This handles UWP apps hosted in ApplicationFrameHost
        var hint = _titleHints.GetValueOrDefault(handle, "");
        _logger.LogInformation("Falling back to desktop window search, titleHint={Hint}", hint);

        var desktop = _automation.GetDesktop();
        var topLevel = desktop.FindAllChildren();

        AutomationElement? window = null;
        if (!string.IsNullOrEmpty(hint))
        {
            // Prefer exact match first to avoid false positives (e.g. "CalculatorTests.cs - Notepad")
            window = topLevel.FirstOrDefault(el =>
                string.Equals(el.Name, hint, StringComparison.OrdinalIgnoreCase))
                ?? topLevel.FirstOrDefault(el =>
                    el.Name.StartsWith(hint, StringComparison.OrdinalIgnoreCase));
        }

        window ??= topLevel.FirstOrDefault(el =>
            el.ControlType == ControlType.Window && el.Name.Length > 0);

        return window
            ?? throw new InvalidOperationException(
                $"Could not find any window for handle '{handle}' " +
                $"(titleHint='{hint}'). Is the application running?");
    }

    private void WalkElement(AutomationElement el, int depth, string wnd, StringBuilder sb)
    {
        var refId = $"{wnd}e{++_refCounter}";
        _refs[refId] = el;

        var indent = new string(' ', depth * 2);
        var type = el.Properties.ControlType.TryGetValue(out var ct) ? ct.ToString().ToLower() : "unknown";
        var name = el.Properties.Name.TryGetValue(out var n) ? n : string.Empty;
        var aid = el.Properties.AutomationId.TryGetValue(out var id) && !string.IsNullOrEmpty(id)
            ? $" id=\"{id}\"" : "";
        sb.AppendLine($"{indent}- {type} \"{name}\" [ref={refId}{aid}]");

        try
        {
            foreach (var child in el.FindAllChildren())
                WalkElement(child, depth + 1, wnd, sb);
        }
        catch (Exception ex)
        {
            sb.AppendLine($"{indent}  [WALK_ERROR: {ex.GetType().Name}: {ex.Message}]");
            _logger.LogWarning("Could not enumerate children of {Ref}: {Type}: {Msg}", refId, ex.GetType().Name, ex.Message);
        }
    }

    /// <summary>
    /// Clicks an element identified by its ref.
    /// Prefers the UIA Invoke pattern (no focus required) and falls back to a mouse click.
    /// </summary>
    public void Click(string refId)
    {
        _logger.LogInformation("Click ref={Ref}", refId);
        var el = GetRef(refId);
        try
        {
            var invoke = el.Patterns.Invoke.PatternOrDefault;
            if (invoke != null)
            {
                invoke.Invoke();
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Invoke pattern failed for {Ref}, falling back to mouse click: {Msg}", refId, ex.Message);
        }
        el.Click();
    }

    /// <summary>Returns the Name (label text) of an element, or the text-box value.</summary>
    public string GetText(string refId)
    {
        var el = GetRef(refId);
        try
        {
            var vp = el.Patterns.Value.PatternOrDefault;
            if (vp != null) return vp.Value.Value ?? string.Empty;
        }
        catch { }
        return el.Name ?? string.Empty;
    }

    /// <summary>Types text into an element (focuses it first).</summary>
    public void Type(string text, string? refId = null)
    {
        if (refId != null) GetRef(refId).Focus();
        FlaUI.Core.Input.Keyboard.Type(text);
    }

    /// <summary>Focuses a specific element.</summary>
    public void Focus(string refId) => GetRef(refId).Focus();

    /// <summary>
    /// Closes the window for the given handle.
    /// Strategy (tried in order):
    ///   1. Invoke the window's "Close" button via UIA (reliable for UWP/Win32).
    ///   2. UIA WindowPattern.Close() (Win32 apps).
    ///   3. app.Close() / app.Kill() as last resort.
    /// </summary>
    public void Close(string handle)
    {
        if (!_windows.TryGetValue(handle, out var app)) return;

        _logger.LogInformation("Closing window handle={Handle}", handle);

        var closed = false;

        // Strategy 1: find the Close button by AutomationId and invoke it.
        // This is equivalent to the user clicking the × and works for both UWP and Win32.
        try
        {
            var window = GetWindow(app, handle);
            var closeBtn = window.FindFirstDescendant(cf => cf.ByAutomationId("Close"))
                ?? window.FindFirstDescendant(cf => cf.ByName("Close")
                    .And(cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button)));
            var invoke = closeBtn?.Patterns.Invoke.PatternOrDefault;
            if (invoke != null)
            {
                invoke.Invoke();
                closed = true;
                _logger.LogInformation("Closed via Close-button invoke");
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Close-button invoke failed: {Msg}", ex.Message);
        }

        // Strategy 2: UIA WindowPattern (works for classic Win32 windows)
        if (!closed)
        {
            try
            {
                var window = GetWindow(app, handle);
                window.Patterns.Window.PatternOrDefault?.Close();
                closed = true;
                _logger.LogInformation("Closed via WindowPattern");
            }
            catch (Exception ex)
            {
                _logger.LogDebug("WindowPattern close failed: {Msg}", ex.Message);
            }
        }

        // Strategy 3: process-level fallback
        if (!closed)
            try { app.Close(); } catch { }

        // Always terminate the backing process.
        // For UWP apps, app.Process may be stale (launcher stub already exited), so we also
        // kill by the known process name to ensure cleanup.
        try { app.Kill(); } catch { }

        var processHint = _processHints.GetValueOrDefault(handle);
        if (!string.IsNullOrEmpty(processHint))
            foreach (var p in Process.GetProcessesByName(processHint))
                try { p.Kill(entireProcessTree: true); } catch { }

        _processHints.Remove(handle);

        _windows.Remove(handle);
        _titleHints.Remove(handle);
        if (_lastHandle == handle) _lastHandle = _windows.Keys.LastOrDefault();
    }

    private AutomationElement GetRef(string refId) =>
        _refs.TryGetValue(refId, out var el) ? el
            : throw new KeyNotFoundException($"Ref '{refId}' not found. Call Snapshot() to refresh element refs.");

    private static ProcessStartInfo BuildStartInfo(string app, string[] args)
    {
        var psi = new ProcessStartInfo(app);
        foreach (var a in args) psi.ArgumentList.Add(a);
        return psi;
    }

    // Maps exe launcher name to the actual UWP process name that must be killed on teardown.
    // UWP launchers exit immediately; the real process has a different name.
    private static string? ResolveProcessName(string app) =>
        Path.GetFileNameWithoutExtension(app).ToLowerInvariant() switch
        {
            "calc" => "CalculatorApp",
            _ => null
        };

    private static string DeriveTitleHint(string app) =>
        Path.GetFileNameWithoutExtension(app).ToLowerInvariant() switch
        {
            "calc" => "Calculator",
            _ => Path.GetFileNameWithoutExtension(app)
        };

    private static string? ResolveAumid(string app) =>
        Path.GetFileNameWithoutExtension(app).ToLowerInvariant() switch
        {
            "calc" => "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App",
            _ => null
        };

    public void Dispose()
    {
        // Close all open windows first (important for UWP apps that outlive their process object)
        foreach (var handle in _windows.Keys.ToList())
            try { Close(handle); } catch { }
        _automation.Dispose();
    }
}
