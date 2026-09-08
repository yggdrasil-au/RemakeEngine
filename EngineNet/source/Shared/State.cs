
namespace EngineNet.Shared;

/// <summary>
/// Runtime state shared by Core and Interface after project separation.
/// </summary>
public static class State {
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

    /// <summary>
    /// Configures runtime state from the host entry point.
    /// </summary>
    public static void ConfigureRuntime(string rootPath, bool isGui, bool isTui, bool isCli) {
        RootPath = rootPath;
        IsGui = isGui;
        IsTui = isTui;
        IsCli = isCli;
    }

}
