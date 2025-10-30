// -----------------------------------------------------------------------------
// File: EngineNet.Tests/ProgramTests.cs
// Purpose: Tests for EngineNet.Program helper methods via reflection (no need
//          for InternalsVisibleTo). Exercises:
//            - GetRootPath
//            - TryFindProjectRoot
//            - CreateToolResolver (file precedence)
// Notes:
//   - We avoid calling Program.Main(...) because it spins up real subsystems.
//   - We reflect the public Program type from the EngineNet assembly.
// -----------------------------------------------------------------------------


namespace EngineNet.Tests;

/// <summary>
/// Reflection-based tests for public helpers on EngineNet.Program.
/// </summary>
public sealed partial class ProgramTests:IDisposable {

    // =============================================================================
    // TryFindProjectRoot tests
    // =============================================================================

    /// <summary>
    /// TryFindProjectRoot walks upward to the first directory that contains
    /// EngineApps/Games and returns that directory path.
    /// </summary>
    [Fact]
    public void TryFindProjectRoot_FindsNearestAncestor() {
        // ARRANGE
        // Build a tree:
        //   _testRoot/
        //     ProjectA/
        //       EngineApps/
        //         Games/
        //       Sub/
        //         Deeper/
        string projectRoot = Path.Combine(_testRoot, "ProjectA");
        string reg = Path.Combine(projectRoot, "EngineApps");
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
    /// TryFindProjectRoot returns null when no ancestor contains EngineApps/Games.
    /// </summary>
    [Fact]
    public void TryFindProjectRoot_ReturnsNull_WhenNotFound() {
        // ARRANGE: a folder structure without EngineApps/Games
        string lonely = Path.Combine(_testRoot, "Lonely");
        Directory.CreateDirectory(lonely);

        // ACT
        object? result = GetProgramMethod("TryFindProjectRoot", typeof(string))
            .Invoke(null, new object?[] { lonely });

        // ASSERT
        Assert.Null(result);
    }

}
