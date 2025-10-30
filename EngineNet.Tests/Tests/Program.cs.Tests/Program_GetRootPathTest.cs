
namespace EngineNet.Tests;

/// <summary>
/// Reflection-based tests for public helpers on EngineNet.Program.
/// </summary>
public sealed partial class ProgramTests {

    // =============================================================================
    // GetRootPath tests
    // =============================================================================

    /// <summary>
    /// GetRootPath returns the string immediately following "--root" (case-sensitive).
    /// </summary>
    /*[Fact] TODO: FIX
    public void GetRootPath_ReturnsValue_AfterFlag() {
        // ARRANGE
        string expected = Path.Combine(_testRoot, "myproj");
        string[] args = new[] { "build", "--root", expected, "--other", "x" };

        // ACT
        object? result = ProgramTests.GetProgramMethod("GetRootPath", typeof(string[]))
            .Invoke(null, new object[] { args });

        // ASSERT
        Assert.Equal(expected, Assert.IsType<string>(result));
    }*/

    /// <summary>
    /// GetRootPath is case-sensitive: "--ROOT" is not recognized by current implementation.
    /// This test documents the behavior to avoid surprises.
    /// </summary>
    [Fact]
    public void GetRootPath_IgnoresUppercaseFlag_DocumentsCaseSensitivity() {
        // ARRANGE
        string[] args = new[] { "--ROOT", _testRoot };

        // ACT
        object? result = ProgramTests.GetProgramMethod("GetRootPath", typeof(string[]))
            .Invoke(null, new object[] { args });

        // ASSERT
        Assert.Null(result); // Expected: not found because code checks args[i] == "--root"
    }
}


