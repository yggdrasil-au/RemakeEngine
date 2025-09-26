
namespace RemakeEngine.Sys;

public static class Types {
    public const String RemakePrefix = "@@REMAKE@@ ";
}

public sealed class GameInfo {
    public String OpsFile {
        get;
    }
    public String GameRoot {
        get;
    }
    public String? ExePath {
        get;
    }
    public String? Title {
        get;
    }

    public GameInfo(String opsFile, String gameRoot, String? exePath = null, String? title = null) {
        OpsFile = opsFile;
        GameRoot = gameRoot;
        ExePath = exePath;
        Title = title;
    }
}
