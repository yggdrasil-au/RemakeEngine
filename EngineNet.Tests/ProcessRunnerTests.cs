
namespace EngineNet.Tests;

public class ProcessRunnerTests
{
    [Fact]
    public void Execute_Echoes_Output_And_Parses_Events()
    {
        if (!OperatingSystem.IsWindows())
            return; // Skip on non-Windows in this test set

        // Use an approved executable (PowerShell) to emit a normal line and a REMAKE event line
        EngineNet.Core.Sys.ProcessRunner runner = new EngineNet.Core.Sys.ProcessRunner();
        List<(String line, String stream)> outputs = new List<(String line, String stream)>();
        List<Dictionary<String, Object?>> events = new List<Dictionary<String, Object?>>();

        String payload = EngineNet.Core.Sys.Types.RemakePrefix + "{\"event\":\"progress\",\"percent\":50}";
        List<String> parts = new List<String> {
            "powershell.exe", "-NoProfile", "-Command",
            $"Write-Output hello; Write-Output '{payload}'"
        };

        Boolean ok = runner.Execute(
            parts,
            opTitle: "script-test",
            onOutput: (string l, string s) => outputs.Add((l, s)),
            onEvent: e => events.Add(e)
        );

        Assert.True(ok);
        Assert.Contains(outputs, x => x.line.Contains("hello"));
        Assert.Contains(events, e => (e.TryGetValue("event", out Object? v) ? v?.ToString() : null) == "end");
        Assert.Contains(events, e => (e.TryGetValue("event", out Object? v) ? v?.ToString() : null) == "progress");
    }
}
