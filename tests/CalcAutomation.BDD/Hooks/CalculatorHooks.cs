using CalcAutomation.BDD.Support;
using CalcAutomation.Core;
using Reqnroll;

namespace CalcAutomation.BDD.Hooks;

[Binding]
public sealed class CalculatorHooks
{
    // Named mutex shared with CalcAutomation.Tests — ensures only one test assembly
    // accesses the single-instance Windows Calculator at a time, even when dotnet test
    // runs both assemblies in parallel worker processes.
    private static readonly Mutex CalcMutex = new(false, "CalcAutomationCalculator");

    private readonly CalculatorContext _ctx;

    public CalculatorHooks(CalculatorContext ctx) => _ctx = ctx;

    [BeforeScenario]
    public void LaunchCalculator()
    {
        try { CalcMutex.WaitOne(); } catch (AbandonedMutexException) { /* previous run crashed — OK */ }
        _ctx.Fla = new FlaUiEngine();
        _ctx.Handle = _ctx.Fla.Launch("calc.exe");
    }

    [AfterScenario]
    public void CloseCalculator()
    {
        try
        {
            if (_ctx.Handle != null)
                _ctx.Fla?.Close(_ctx.Handle);
            _ctx.Fla?.Dispose();
        }
        finally
        {
            CalcMutex.ReleaseMutex();
        }
    }
}
