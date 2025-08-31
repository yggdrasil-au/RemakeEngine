using System;
using System.Collections.Generic;
using RemakeEngine.Core;
using Xunit;

namespace EngineNet.Tests;

public class ProcessRunnerTests
{
    [Fact]
    public void Execute_Echoes_Output_And_Parses_Events()
    {
        if (!OperatingSystem.IsWindows())
            return; // Skip on non-Windows in this test set

        // Create a small batch script that emits a normal line and a REMAKE event line
        var tmpBat = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "enginenet_evt_" + Guid.NewGuid().ToString("N") + ".cmd");
        System.IO.File.WriteAllText(tmpBat, "@echo off\r\necho hello\r\necho " + Types.RemakePrefix + "{\"event\":\"progress\",\"percent\":50}\r\n");

        var runner = new ProcessRunner();
        var outputs = new List<(string line, string stream)>();
        var events = new List<Dictionary<string, object?>>();

        var parts = new List<string> { "cmd.exe", "/c", tmpBat };

        var ok = runner.Execute(
            parts,
            opTitle: "script-test",
            onOutput: (l, s) => outputs.Add((l, s)),
            onEvent: e => events.Add(e)
        );

        try { System.IO.File.Delete(tmpBat); } catch { }

        Assert.True(ok);
        Assert.Contains(outputs, x => x.line.Contains("hello"));
        Assert.Contains(events, e => (e.TryGetValue("event", out var v) ? v?.ToString() : null) == "end");
        Assert.Contains(events, e => (e.TryGetValue("event", out var v) ? v?.ToString() : null) == "progress");
    }
}
