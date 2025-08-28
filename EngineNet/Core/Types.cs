using System;

namespace RemakeEngine.Core;

public static class Types
{
    public const string RemakePrefix = "@@REMAKE@@ ";
}

public sealed class GameInfo
{
    public string OpsFile { get; }
    public string GameRoot { get; }

    public GameInfo(string opsFile, string gameRoot)
    {
        OpsFile = opsFile;
        GameRoot = gameRoot;
    }
}

