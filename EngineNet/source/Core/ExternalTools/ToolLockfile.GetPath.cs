namespace EngineNet.Core.ExternalTools;

/// <summary>
/// Centralized tool lockfile naming and path helpers.
/// Update this in one place to rename the lockfile.
/// </summary>
public static class ToolLockfile {
    public const string ToolLockfileName = "Tools.installed.json";

    internal static string GetPath(string rootPath) {
        return System.IO.Path.Combine(rootPath, ToolLockfileName);
    }
}
