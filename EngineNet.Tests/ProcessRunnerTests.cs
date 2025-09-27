using System;
using System.Collections.Generic;
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
        String tmpBat = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "enginenet_evt_" + Guid.NewGuid().ToString("N") + ".cmd");
        System.IO.File.WriteAllText(tmpBat, "@echo off\r\necho hello\r\necho " + EngineNet.Core.Sys.Types.RemakePrefix + "{\"event\":\"progress\",\"percent\":50}\r\n");

        EngineNet.Core.Sys.ProcessRunner runner = new EngineNet.Core.Sys.ProcessRunner();
        List<(String line, String stream)> outputs = new List<(String line, String stream)>();
        List<Dictionary<String, Object?>> events = new List<Dictionary<String, Object?>>();

        List<String> parts = new List<String> { "cmd.exe", "/c", tmpBat };

        Boolean ok = runner.Execute(
            parts,
            opTitle: "script-test",
            onOutput: (string l, string s) => outputs.Add((l, s)),
            onEvent: e => events.Add(e)
        );

        try { System.IO.File.Delete(tmpBat); } catch { }

        Assert.True(ok);
        Assert.Contains(outputs, x => x.line.Contains("hello"));
        Assert.Contains(events, e => (e.TryGetValue("event", out Object? v) ? v?.ToString() : null) == "end");
        Assert.Contains(events, e => (e.TryGetValue("event", out Object? v) ? v?.ToString() : null) == "progress");
    }
}
