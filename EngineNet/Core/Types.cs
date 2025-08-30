using System;

namespace RemakeEngine.Core;

public static class Types {
    public const string RemakePrefix = "@@REMAKE@@ ";
}

public sealed class GameInfo {
    public string OpsFile {
        get;
    }
    public string GameRoot {
        get;
    }
    public string? ExePath {
        get;
    }
    public string? Title {
        get;
    }

    public GameInfo(string opsFile, string gameRoot, string? exePath = null, string? title = null) {
        OpsFile = opsFile;
        GameRoot = gameRoot;
        ExePath = exePath;
        Title = title;
    }
}
