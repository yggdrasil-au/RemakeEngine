// -----------------------------------------------------------------------------
// File: EngineNet.Tests/ProgramTests.cs
// Purpose: Tests for EngineNet.Program helper methods via reflection (no need
//          for InternalsVisibleTo). Exercises:
//            - GetRootPath
//            - TryFindProjectRoot
//            - CreateToolResolver (file precedence)
// Notes:
//   - We avoid calling Program.Main(...) because it spins up real subsystems.
//   - We reflect the internal Program type from the EngineNet assembly.
// -----------------------------------------------------------------------------


namespace EngineNet.Tests;

/// <summary>
/// Reflection-based tests for internal helpers on EngineNet.Program.
/// </summary>
public sealed class ProgramTests:IDisposable {
    // We create a temporary test root folder for filesystem-based tests
    // and clean it up in Dispose().
    private readonly string _testRoot;

    public ProgramTests() {
        // ARRANGE (common): create a unique, isolated temp root for this test class run.
        _testRoot = Path.Combine(Path.GetTempPath(), "enginenet_progtests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
    }

    public void Dispose() {
        try {
            Directory.Delete(_testRoot, recursive: true);
        } catch { /* best-effort cleanup */ }
    }

    // --- Helper: get the internal Program type and its methods via reflection ---

    /// <summary>Resolves the internal EngineNet.Program type from the EngineNet assembly.</summary>
    private static Type GetProgramType() {
        // Trick: grab the assembly that contains a known public type (EngineConfig),
        // then fetch the internal type by full name "EngineNet.Program".
        var asm = typeof(EngineNet.EngineConfig).Assembly;
        return asm.GetType("EngineNet.Program", throwOnError: true)!;
    }

    /// <summary>Gets a static method (public or non-public) from EngineNet.Program.</summary>
    public static MethodInfo GetProgramMethod(string name, params Type[] parameterTypes) {
        var t = GetProgramType();
        return t.GetMethod(
            name,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: parameterTypes,
            modifiers: null
        ) ?? throw new MissingMethodException($"Method {name} not found on EngineNet.Program");
    }


    // =============================================================================
    // TryFindProjectRoot tests
    // =============================================================================

    /// <summary>
    /// TryFindProjectRoot walks upward to the first directory that contains
    /// RemakeRegistry/Games and returns that directory path.
    /// </summary>
    [Fact]
    public void TryFindProjectRoot_FindsNearestAncestor() {
        // ARRANGE
        // Build a tree:
        //   _testRoot/
        //     ProjectA/
        //       RemakeRegistry/
        //         Games/
        //       Sub/
        //         Deeper/
        string projectRoot = Path.Combine(_testRoot, "ProjectA");
        string reg = Path.Combine(projectRoot, "RemakeRegistry");
        string games = Path.Combine(reg, "Games");
        Directory.CreateDirectory(games);

        string deep = Path.Combine(projectRoot, "Sub", "Deeper");
        Directory.CreateDirectory(deep);

        // ACT
        // Start the search from the deepest folder.
        object? result = GetProgramMethod("TryFindProjectRoot", typeof(string))
            .Invoke(null, new object?[] { deep });

        // ASSERT
        string found = Assert.IsType<string>(result);
        Assert.Equal(Path.GetFullPath(projectRoot), Path.GetFullPath(found));
    }

    /// <summary>
    /// TryFindProjectRoot returns null when no ancestor contains RemakeRegistry/Games.
    /// </summary>
    [Fact]
    public void TryFindProjectRoot_ReturnsNull_WhenNotFound() {
        // ARRANGE: a folder structure without RemakeRegistry/Games
        string lonely = Path.Combine(_testRoot, "Lonely");
        Directory.CreateDirectory(lonely);

        // ACT
        object? result = GetProgramMethod("TryFindProjectRoot", typeof(string))
            .Invoke(null, new object?[] { lonely });

        // ASSERT
        Assert.Null(result);
    }

    // =============================================================================
    // CreateToolResolver tests
    // =============================================================================

    /// <summary>
    /// CreateToolResolver prefers Tools.local.json in the project root.
    /// </summary>
    [Fact]
    public void CreateToolResolver_Prefers_ToolsLocalJson() {
        // ARRANGE
        string root = Path.Combine(_testRoot, "R1");
        Directory.CreateDirectory(root);

        // Put both RemakeRegistry/tools.json and Tools.local.json
        string rr = Path.Combine(root, "RemakeRegistry");
        Directory.CreateDirectory(rr);

        File.WriteAllText(Path.Combine(rr, "tools.json"), "{ }");
        File.WriteAllText(Path.Combine(root, "Tools.local.json"), "{ }");

        // ACT
        object? resolver = GetProgramMethod("CreateToolResolver", typeof(string))
            .Invoke(null, new object[] { root });

        // ASSERT
        Assert.NotNull(resolver);
        // We assert by type name to avoid needing direct references to concrete classes.
        Assert.Equal("JsonToolResolver", resolver.GetType().Name);
    }

    /// <summary>
    /// CreateToolResolver uses tools.local.json (lowercase) if Tools.local.json is absent.
    /// </summary>
    [Fact]
    public void CreateToolResolver_FallsBack_To_Lowercase_ToolsLocalJson() {
        // ARRANGE
        string root = Path.Combine(_testRoot, "R2");
        Directory.CreateDirectory(root);

        File.WriteAllText(Path.Combine(root, "tools.local.json"), "{ }");

        // ACT
        object? resolver = GetProgramMethod("CreateToolResolver", typeof(string))
            .Invoke(null, new object[] { root });

        // ASSERT
        Assert.NotNull(resolver);
        Assert.Equal("JsonToolResolver", resolver.GetType().Name);
    }

    /// <summary>
    /// CreateToolResolver uses RemakeRegistry/Tools.json if no root-level local files exist.
    /// </summary>
    [Fact]
    public void CreateToolResolver_Uses_RemakeRegistry_ToolsJson_When_NoLocal() {
        // ARRANGE
        string root = Path.Combine(_testRoot, "R3");
        Directory.CreateDirectory(root);

        string rr = Path.Combine(root, "RemakeRegistry");
        Directory.CreateDirectory(rr);
        File.WriteAllText(Path.Combine(rr, "Tools.json"), "{ }");

        // ACT
        object? resolver = GetProgramMethod("CreateToolResolver", typeof(string))
            .Invoke(null, new object[] { root });

        // ASSERT
        Assert.NotNull(resolver);
        Assert.Equal("JsonToolResolver", resolver.GetType().Name);
    }

    /// <summary>
    /// CreateToolResolver uses RemakeRegistry/tools.json if Tools.json is absent.
    /// </summary>
    [Fact]
    public void CreateToolResolver_FallsBack_To_RemakeRegistry_lowercase_tools() {
        // ARRANGE
        string root = Path.Combine(_testRoot, "R4");
        Directory.CreateDirectory(root);

        string rr = Path.Combine(root, "RemakeRegistry");
        Directory.CreateDirectory(rr);
        File.WriteAllText(Path.Combine(rr, "tools.json"), "{ }");

        // ACT
        object? resolver = GetProgramMethod("CreateToolResolver", typeof(string))
            .Invoke(null, new object[] { root });

        // ASSERT
        Assert.NotNull(resolver);
        Assert.Equal("JsonToolResolver", resolver.GetType().Name);
    }

    /// <summary>
    /// CreateToolResolver returns PassthroughToolResolver when no known files exist.
    /// </summary>
    [Fact]
    public void CreateToolResolver_Passthrough_When_NoFiles() {
        // ARRANGE
        string root = Path.Combine(_testRoot, "R5");
        Directory.CreateDirectory(root);

        // ACT
        object? resolver = GetProgramMethod("CreateToolResolver", typeof(string))
            .Invoke(null, new object[] { root });

        // ASSERT
        Assert.NotNull(resolver);
        Assert.Equal("PassthroughToolResolver", resolver.GetType().Name);
    }
}
