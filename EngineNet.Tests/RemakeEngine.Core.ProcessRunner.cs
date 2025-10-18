namespace RemakeEngine.Core;

public static class ProcessRunner {
    public delegate void OutputHandler(string line, string streamName);
    public delegate void EventHandler(System.Collections.Generic.Dictionary<string, object?> evt);
    public delegate string? StdinProvider();
}
