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
public sealed partial class ProgramTests:IDisposable {
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

}
