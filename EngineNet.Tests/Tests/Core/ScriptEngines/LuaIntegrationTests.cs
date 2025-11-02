namespace EngineNet.Tests;

public sealed class LuaIntegrationTests {
    [Fact]
    public async Task DemoLuaScript_CompletesAndEmitsCompletionEvent() {
        // Arrange: determine project root (walk up until EngineApps/Games exists)
        string root = FindProjectRoot() ?? System.IO.Directory.GetCurrentDirectory();
        EngineNet.Core.Tools.IToolResolver tools = new EngineNet.Core.Tools.PassthroughToolResolver();
        EngineNet.Core.EngineConfig cfg = new EngineNet.Core.EngineConfig(path: System.IO.Path.Combine(root, "project.json"));
        EngineNet.Core.Engine engine = new EngineNet.Core.Engine(rootPath: root, tools: tools, engineConfig: cfg);

        Dictionary<string, object?> games = engine.ListGames();
        string demoRoot = System.IO.Path.Combine(root, "EngineApps", "Games", "demo");
        Assert.True(System.IO.Directory.Exists(demoRoot), userMessage: $"Demo module not found at {demoRoot}");
        if (!games.ContainsKey("demo")) {
            games["demo"] = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase) {
                ["game_root"] = demoRoot
            };
        }

        string game = "demo";
        Dictionary<string, object?> op = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase) {
            ["script_type"] = "lua",
            ["script"] = "{{Game_Root}}/scripts/lua_feature_demo.lua",
            ["args"] = new List<object?> {
                "--module", "{{Game_Root}}",
                "--scratch", "{{Game_Root}}/TMP/lua-demo-test",
                "--note", "Lua demo test run"
            }
        };

        List<Dictionary<string, object?>> events = new List<Dictionary<string, object?>>();

        // Capture EngineSdk events and auto-answer the demo prompt
        System.Action<Dictionary<string, object?>>? prevSink = EngineNet.Core.Utils.EngineSdk.LocalEventSink;
        bool prevMute = EngineNet.Core.Utils.EngineSdk.MuteStdoutWhenLocalSink;
        Dictionary<string, string> prevAuto = new Dictionary<string, string>(EngineNet.Core.Utils.EngineSdk.AutoPromptResponses, System.StringComparer.OrdinalIgnoreCase);
        try {
            EngineNet.Core.Utils.EngineSdk.LocalEventSink = (evt) => { events.Add(new Dictionary<string, object?>(evt, System.StringComparer.OrdinalIgnoreCase)); };
            EngineNet.Core.Utils.EngineSdk.MuteStdoutWhenLocalSink = true;
            EngineNet.Core.Utils.EngineSdk.AutoPromptResponses.Clear();
            EngineNet.Core.Utils.EngineSdk.AutoPromptResponses["demo_prompt"] = "";

            bool ok = await engine.RunSingleOperationAsync(
                currentGame: game,
                games: games,
                op: op,
                promptAnswers: new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase),
                cancellationToken: default
            ).ConfigureAwait(false);

            // Assert: operation succeeded and expected events were observed
            Assert.True(ok);
            Assert.Contains(events, e => string.Equals(e.GetValueOrDefault("event")?.ToString(), "demo_start", System.StringComparison.OrdinalIgnoreCase));
            Assert.Contains(events, e => string.Equals(e.GetValueOrDefault("event")?.ToString(), "comprehensive_demo_complete", System.StringComparison.OrdinalIgnoreCase));
            Assert.Contains(events, e => string.Equals(e.GetValueOrDefault("event")?.ToString(), "print", System.StringComparison.OrdinalIgnoreCase) &&
                                         (e.GetValueOrDefault("message")?.ToString() ?? string.Empty).Contains("Comprehensive Lua API Demo Complete", System.StringComparison.OrdinalIgnoreCase));
        } finally {
            // Restore EngineSdk globals
            EngineNet.Core.Utils.EngineSdk.LocalEventSink = prevSink;
            EngineNet.Core.Utils.EngineSdk.MuteStdoutWhenLocalSink = prevMute;
            EngineNet.Core.Utils.EngineSdk.AutoPromptResponses.Clear();
            foreach (KeyValuePair<string, string> kv in prevAuto) {
                EngineNet.Core.Utils.EngineSdk.AutoPromptResponses[kv.Key] = kv.Value;
            }
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
        } catch {
#if DEBUG
// todo add trace writeline
#endif
/* ignore */
}
        return null;
    }
}
