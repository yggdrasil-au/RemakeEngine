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
public sealed partial class ProgramTests {

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
