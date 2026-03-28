namespace EngineNet.Core;

/// <summary>
/// Runtime state shared by Core and Interface after project separation.
/// The entry executable configures these values during startup.
/// </summary>
internal static class Main {
    internal static string RootPath {
        get; private set;
    } = string.Empty;

    internal static bool IsGui {
        get; private set;
    }

    internal static bool IsTui {
        get; private set;
    }

    internal static bool IsCli {
        get; private set;
    }

    private static Func<Task<EngineNet.Core.Engine.IEngineFace>>? EngineFactory {
        get; set;
    }

    /// <summary>
    /// Configures runtime state from the host entry point.
    /// </summary>
    internal static void ConfigureRuntime(string rootPath, bool isGui, bool isTui, bool isCli, Func<Task<EngineNet.Core.Engine.IEngineFace>>? engineFactory = null) {
        Main.RootPath = rootPath;
        Main.IsGui = isGui;
        Main.IsTui = isTui;
        Main.IsCli = isCli;

        if (engineFactory != null) {
            Main.EngineFactory = engineFactory;
        }
    }

    /// <summary>
    /// Creates an engine using the configured host factory.
    /// </summary>
    internal static async Task<EngineNet.Core.Engine.IEngineFace> InitialiseEngineAsync() {
        if (Main.EngineFactory == null) {
            throw new InvalidOperationException("EngineFactory is not configured. Call ConfigureRuntime from the entry point first.");
        }

        return await Main.EngineFactory();
    }
}
