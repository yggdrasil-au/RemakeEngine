namespace EngineNet.Core;

/// <summary>
/// Runtime state shared by Core and Interface after project separation.
/// The entry executable configures these values during startup.
/// </summary>
public static class Lib {
    public static string RootPath {
        get; private set;
    } = string.Empty;

    public static bool IsGui {
        get; private set;
    }

    public static bool IsTui {
        get; private set;
    }

    public static bool IsCli {
        get; private set;
    }

    private static Func<Task<EngineNet.Core.Engine.IEngineFace>>? EngineFactory {
        get; set;
    }

    /// <summary>
    /// Configures runtime state from the host entry point.
    /// </summary>
    public static void ConfigureRuntime(string rootPath, bool isGui, bool isTui, bool isCli, Func<Task<EngineNet.Core.Engine.IEngineFace>>? engineFactory = null) {
        RootPath = rootPath;
        IsGui = isGui;
        IsTui = isTui;
        IsCli = isCli;

        if (engineFactory != null) {
            EngineFactory = engineFactory;
        }
    }

    /// <summary>
    /// Creates an engine using the configured host factory.
    /// </summary>
    public static async Task<EngineNet.Core.Engine.IEngineFace> InitialiseEngineAsync() {
        if (EngineFactory == null) {
            throw new InvalidOperationException("EngineFactory is not configured. Call ConfigureRuntime from the entry point first.");
        }

        return await EngineFactory();
    }
}
