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
    private readonly HashSet<string> _uwpHandles = new();              // handles launched via LaunchStoreApp
    private readonly Dictionary<string, int[]> _windowRuntimeIds = new(); // handle → pinned desktop window RuntimeId
    private readonly Dictionary<string, List<int[]>> _preExistingIds = new(); // handle → RuntimeIds present before launch
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
        var isUwp = false;
        List<int[]> preExisting = [];

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

            var aumid = ResolveAumid(app);
            if (aumid != null)
            {
                isUwp = true;
                // Snapshot which windows already exist BEFORE activation so WaitForWindowReady
                // can identify the newly-appeared window, not a pre-existing one from a parallel
                // test runner or a previous scenario that hasn't fully closed yet.
                preExisting = SnapshotDesktopRuntimeIds(titleHint);
                _logger.LogInformation("Activating UWP AUMID={Aumid} ({Pre} pre-existing Calculator windows)",
                    aumid, preExisting.Count);
                // Retry LaunchStoreApp — the UWP activation broker can briefly return
                // CO_E_SERVER_EXEC_FAILURE (0x80040905) while the previous instance is still
                // shutting down. Back off and retry up to 5 times before giving up.
                application = LaunchStoreAppWithRetry(aumid);
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

        var handle = $"w{++_wndCounter}";
        _windows[handle] = application;
        _titleHints[handle] = titleHint;
        _processHints[handle] = ResolveProcessName(app) ?? string.Empty;
        _lastHandle = handle;
        if (isUwp)
        {
            _uwpHandles.Add(handle);
            _preExistingIds[handle] = preExisting;
        }

        WaitForWindowReady(handle, TimeSpan.FromSeconds(10));

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
        // Primary: if we've pinned this handle to a specific window RuntimeId (done during
        // WaitForWindowReady), find that exact window. This prevents two parallel test
        // assemblies or back-to-back scenarios from stealing each other's windows by title.
        if (_windowRuntimeIds.TryGetValue(handle, out var pinnedId))
        {
            try
            {
                var desktop = _automation.GetDesktop();
                var pinned = desktop.FindAllChildren().FirstOrDefault(el =>
                    el.Properties.RuntimeId.TryGetValue(out var id) && id.SequenceEqual(pinnedId));
                if (pinned != null) return pinned;
                _logger.LogDebug("Pinned RuntimeId no longer on desktop — falling back to title search");
            }
            catch (Exception ex)
            {
                _logger.LogDebug("RuntimeId lookup failed: {Msg}", ex.Message);
            }
        }

        // For known UWP handles, skip GetMainWindow (always times out for apps hosted in
        // ApplicationFrameHost, adding a needless 5-second stall on every Snapshot call).
        if (!_uwpHandles.Contains(handle))
        {
            try
            {
                var w = app.GetMainWindow(_automation, TimeSpan.FromSeconds(5));
                if (w != null) return w;
            }
            catch (Exception ex)
            {
                _logger.LogDebug("GetMainWindow failed: {Msg}", ex.Message);
            }
        }

        // Desktop title search — required for UWP apps in ApplicationFrameHost.
        var hint = _titleHints.GetValueOrDefault(handle, "");
        _logger.LogDebug("Using desktop window search, titleHint={Hint}", hint);

        var desktop2 = _automation.GetDesktop();
        var topLevel = desktop2.FindAllChildren();

        AutomationElement? window = null;
        if (!string.IsNullOrEmpty(hint))
        {
            // Prefer exact match to avoid false positives (e.g. "CalculatorTests.cs - Notepad").
            // All el.Name accesses are guarded with TryGetValue to avoid COMException when an
            // element is being destroyed mid-enumeration (0x80040201).
            window = topLevel.FirstOrDefault(el =>
                        el.Properties.Name.TryGetValue(out var n) &&
                        string.Equals(n, hint, StringComparison.OrdinalIgnoreCase))
                  ?? topLevel.FirstOrDefault(el =>
                        el.Properties.Name.TryGetValue(out var n) &&
                        n.StartsWith(hint, StringComparison.OrdinalIgnoreCase));
        }

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
    public void Close(string? handle)
    {
        if (string.IsNullOrEmpty(handle)) return;
        if (!_windows.TryGetValue(handle, out var app)) return;

        // Capture before dictionaries are cleared.
        var titleHint = _titleHints.GetValueOrDefault(handle, "");
        var runtimeId = _windowRuntimeIds.GetValueOrDefault(handle);

        _logger.LogInformation("Closing window handle={Handle}", handle);

        var closed = false;

        // Strategy 1: find the Close button by AutomationId and invoke it.
        // This is equivalent to the user clicking × and works for both UWP and Win32.
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
        _uwpHandles.Remove(handle);
        _windowRuntimeIds.Remove(handle);
        _preExistingIds.Remove(handle);
        if (_lastHandle == handle) _lastHandle = _windows.Keys.LastOrDefault();

        // Block until this specific window is gone from the desktop so the next scenario's
        // Launch() doesn't find a stale window during desktop title search.
        WaitForWindowGone(titleHint, runtimeId, TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Polls until the window for <paramref name="handle"/> is reachable, has rendered at
    /// least some children, and is a NEW window (not one that existed before Launch was called).
    /// Pins the handle to the found window's RuntimeId for all future GetWindow calls.
    /// </summary>
    private void WaitForWindowReady(string handle, TimeSpan timeout)
    {
        var hint = _titleHints.GetValueOrDefault(handle, "");
        var preExisting = _preExistingIds.GetValueOrDefault(handle, []);
        var sw = Stopwatch.StartNew();

        while (sw.Elapsed < timeout)
        {
            try
            {
                var desktop = _automation.GetDesktop();
                var topLevel = desktop.FindAllChildren();

                // Find a window matching our title hint that did NOT exist before we launched.
                // This prevents stealing a window from a parallel test run or a not-yet-closed
                // previous scenario.
                AutomationElement? target = null;
                foreach (var el in topLevel)
                {
                    if (!el.Properties.Name.TryGetValue(out var name)) continue;
                    if (!string.Equals(name, hint, StringComparison.OrdinalIgnoreCase) &&
                        !name.StartsWith(hint, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!el.Properties.RuntimeId.TryGetValue(out var rId)) continue;
                    if (preExisting.Any(pre => pre.SequenceEqual(rId))) continue; // skip stale windows

                    target = el;
                    break;
                }

                // Fallback: no pre-existing tracking (Win32 path) — use the first title match
                target ??= topLevel.FirstOrDefault(el =>
                    el.Properties.Name.TryGetValue(out var name) &&
                    (string.Equals(name, hint, StringComparison.OrdinalIgnoreCase) ||
                     name.StartsWith(hint, StringComparison.OrdinalIgnoreCase)));

                if (target?.FindAllChildren().Length > 0)
                {
                    if (target.Properties.RuntimeId.TryGetValue(out var rId))
                    {
                        _windowRuntimeIds[handle] = rId;
                        _logger.LogInformation(
                            "Window pinned (RuntimeId) and ready after {Ms}ms (hint='{Hint}')",
                            sw.ElapsedMilliseconds, hint);
                    }
                    else
                    {
                        _logger.LogInformation("Window ready after {Ms}ms (hint='{Hint}', no RuntimeId)",
                            sw.ElapsedMilliseconds, hint);
                    }
                    return;
                }
            }
            catch { }
            Thread.Sleep(100);
        }
        _logger.LogWarning("Window '{Hint}' did not become ready within {Timeout}s — proceeding anyway",
            hint, timeout.TotalSeconds);
    }

    /// <summary>
    /// Polls until the window identified by <paramref name="runtimeId"/> (or by
    /// <paramref name="titleHint"/> as fallback) is no longer on the desktop.
    /// Called at the end of Close() to prevent the next test from finding a stale window.
    /// </summary>
    private void WaitForWindowGone(string titleHint, int[]? runtimeId, TimeSpan timeout)
    {
        if (string.IsNullOrEmpty(titleHint) && runtimeId == null) return;
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            try
            {
                var desktop = _automation.GetDesktop();
                var topLevel = desktop.FindAllChildren();
                bool found;

                if (runtimeId != null)
                {
                    found = topLevel.Any(el =>
                        el.Properties.RuntimeId.TryGetValue(out var id) && id.SequenceEqual(runtimeId));
                }
                else
                {
                    found = topLevel.Any(el =>
                        el.Properties.Name.TryGetValue(out var n) &&
                        string.Equals(n, titleHint, StringComparison.OrdinalIgnoreCase));
                }

                if (!found)
                {
                    _logger.LogInformation("Window '{Hint}' gone after {Ms}ms", titleHint, sw.ElapsedMilliseconds);
                    return;
                }
            }
            catch { }
            Thread.Sleep(100);
        }
        _logger.LogWarning("Window '{Hint}' still visible after {Timeout}s — proceeding anyway",
            titleHint, timeout.TotalSeconds);
    }

    /// <summary>
    /// Returns the RuntimeIds of all desktop top-level windows whose title matches
    /// <paramref name="titleHint"/>. Called before launching a new UWP instance so
    /// WaitForWindowReady can identify only the freshly-appeared window.
    /// </summary>
    /// <summary>
    /// Calls <see cref="Application.LaunchStoreApp"/> with exponential back-off retries.
    /// The UWP activation broker can briefly return CO_E_SERVER_EXEC_FAILURE (0x80040905)
    /// while the previous instance of a single-instance app is still shutting down.
    /// </summary>
    private Application LaunchStoreAppWithRetry(string aumid, int maxAttempts = 5)
    {
        Exception? last = null;
        for (var i = 0; i < maxAttempts; i++)
        {
            try { return Application.LaunchStoreApp(aumid); }
            catch (Exception ex)
            {
                last = ex;
                var delay = 200 * (i + 1); // 200 ms, 400 ms, 600 ms, 800 ms, 1000 ms
                _logger.LogWarning("LaunchStoreApp attempt {N}/{Max} failed ({Msg}), retrying in {Delay}ms",
                    i + 1, maxAttempts, ex.Message, delay);
                Thread.Sleep(delay);
            }
        }
        throw new InvalidOperationException(
            $"LaunchStoreApp('{aumid}') failed after {maxAttempts} attempts.", last);
    }

    private List<int[]> SnapshotDesktopRuntimeIds(string titleHint)
    {
        var ids = new List<int[]>();
        try
        {
            var desktop = _automation.GetDesktop();
            foreach (var el in desktop.FindAllChildren())
            {
                if (!el.Properties.Name.TryGetValue(out var name)) continue;
                if (!string.Equals(name, titleHint, StringComparison.OrdinalIgnoreCase) &&
                    !name.StartsWith(titleHint, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (el.Properties.RuntimeId.TryGetValue(out var id))
                    ids.Add(id);
            }
        }
        catch { }
        return ids;
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
