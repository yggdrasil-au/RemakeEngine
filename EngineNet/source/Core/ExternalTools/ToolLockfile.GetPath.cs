namespace EngineNet.Core.ExternalTools;

/// <summary>
/// Centralized tool lockfile naming and path helpers.
/// Update this in one place to rename the lockfile.
/// </summary>
internal static class ToolLockfile {
    internal const string ToolLockfileName = "Tools.installed.json";

    internal static string GetPath(string rootPath) {
        return System.IO.Path.Combine(rootPath, ToolLockfileName);
    }
}
