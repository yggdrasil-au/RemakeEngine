namespace EngineNet.Tests;

public sealed class LuaEngineUnitTests {
    [Fact]
    public async Task LuaGlobals_EmitPromptAndPrint_Work() {
        // Arrange: minimal Lua that exercises emit/print/prompt and argv
        string code = @"
            emit('unit_test_start', { part = 'globals' })
            sdk.color_print('green', 'hello world')
            warn('be careful')
            error('oops')
            local ans = prompt('say something', 'unit_id', false)
            if argv and #argv > 0 then
              emit('argv_seen', { first = argv[1] })
            end
            emit('unit_test_done', { ok = true, ans = ans })
        ";

        string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lua_unit_" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tempDir);
        string luaPath = System.IO.Path.Combine(tempDir, "unit.lua");
        await System.IO.File.WriteAllTextAsync(path: luaPath, contents: code);

        string root = FindProjectRoot() ?? System.IO.Directory.GetCurrentDirectory();
        EngineNet.Core.Tools.IToolResolver tools = new EngineNet.Core.Tools.PassthroughToolResolver();
        EngineNet.Core.EngineConfig cfg = new EngineNet.Core.EngineConfig(path: System.IO.Path.Combine(root, "project.json"));
        EngineNet.Core.Engine engine = new EngineNet.Core.Engine(rootPath: root, tools: tools, engineConfig: cfg);

        Dictionary<string, object?> games = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase) {
            ["temp"] = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase) {
                ["game_root"] = root
            }
        };
        string game = "temp";

        Dictionary<string, object?> op = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase) {
            ["script_type"] = "lua",
            ["script"] = luaPath,
            ["args"] = new List<object?> { "ARG1" }
        };

        List<Dictionary<string, object?>> events = new List<Dictionary<string, object?>>();
        System.Action<Dictionary<string, object?>>? prevSink = EngineNet.Core.Utils.EngineSdk.LocalEventSink;
        bool prevMute = EngineNet.Core.Utils.EngineSdk.MuteStdoutWhenLocalSink;
        Dictionary<string, string> prevAuto = new Dictionary<string, string>(EngineNet.Core.Utils.EngineSdk.AutoPromptResponses, System.StringComparer.OrdinalIgnoreCase);
        try {
            EngineNet.Core.Utils.EngineSdk.LocalEventSink = (evt) => { events.Add(new Dictionary<string, object?>(evt, System.StringComparer.OrdinalIgnoreCase)); };
            EngineNet.Core.Utils.EngineSdk.MuteStdoutWhenLocalSink = true;
            EngineNet.Core.Utils.EngineSdk.AutoPromptResponses.Clear();
            EngineNet.Core.Utils.EngineSdk.AutoPromptResponses["unit_id"] = "abc";

            bool ok = await engine.RunSingleOperationAsync(
                currentGame: game,
                games: games,
                op: op,
                promptAnswers: new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase),
                cancellationToken: default
            ).ConfigureAwait(false);

            Assert.True(ok);
            // Verify key events
            Assert.Contains(events, e => string.Equals(e.GetValueOrDefault("event")?.ToString(), "unit_test_start", System.StringComparison.OrdinalIgnoreCase));
            Assert.Contains(events, e => string.Equals(e.GetValueOrDefault("event")?.ToString(), "unit_test_done", System.StringComparison.OrdinalIgnoreCase));
            Assert.Contains(events, e => string.Equals(e.GetValueOrDefault("event")?.ToString(), "warning", System.StringComparison.OrdinalIgnoreCase));
            Assert.Contains(events, e => string.Equals(e.GetValueOrDefault("event")?.ToString(), "error", System.StringComparison.OrdinalIgnoreCase));
            Assert.Contains(events, e => string.Equals(e.GetValueOrDefault("event")?.ToString(), "print", System.StringComparison.OrdinalIgnoreCase) &&
                                         (e.GetValueOrDefault("message")?.ToString() ?? string.Empty).Contains("hello world", System.StringComparison.OrdinalIgnoreCase));
            Assert.Contains(events, e => string.Equals(e.GetValueOrDefault("event")?.ToString(), "argv_seen", System.StringComparison.OrdinalIgnoreCase) &&
                                         string.Equals((e.GetValueOrDefault("first")?.ToString() ?? string.Empty), "ARG1", System.StringComparison.Ordinal));
        } finally {
            EngineNet.Core.Utils.EngineSdk.LocalEventSink = prevSink;
            EngineNet.Core.Utils.EngineSdk.MuteStdoutWhenLocalSink = prevMute;
            EngineNet.Core.Utils.EngineSdk.AutoPromptResponses.Clear();
            foreach (KeyValuePair<string, string> kv in prevAuto) {
                EngineNet.Core.Utils.EngineSdk.AutoPromptResponses[kv.Key] = kv.Value;
            }
            try { System.IO.File.Delete(luaPath); } catch { /* ignore */ }
            try { System.IO.Directory.Delete(tempDir, recursive: true); } catch { /* ignore */ }
        }
    }

    private static string? FindProjectRoot() {
        string? dir = System.IO.Path.GetFullPath(System.IO.Directory.GetCurrentDirectory());
        try {
            while (!string.IsNullOrEmpty(dir)) {
                string games = System.IO.Path.Combine(dir!, "EngineApps", "Games");
                if (System.IO.Directory.Exists(games)) {
                    return dir!;
                }
                System.IO.DirectoryInfo? parent = System.IO.Directory.GetParent(dir!);
                if (parent is null) break;
                dir = parent.FullName;
            }
        } catch { /* ignore */ }
        return null;
    }
}
