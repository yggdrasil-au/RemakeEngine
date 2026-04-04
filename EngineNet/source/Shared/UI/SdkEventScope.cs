namespace EngineNet.Shared.UI;

/// <summary>
/// Disposable scope to set EngineSdk.LocalEventSink, MuteStdoutWhenLocalSink
/// and optionally seed AutoPromptResponses, restoring prior values on dispose.
/// </summary>
internal sealed class SdkEventScope:System.IDisposable {
    private readonly System.Action<Dictionary<string, object?>>? _prevSink;
    private readonly bool _prevMute;
    private readonly Dictionary<string, string> _prevAuto;

    internal SdkEventScope(
        System.Action<Dictionary<string, object?>>? sink,
        bool muteStdout,
        IDictionary<string, string>? autoPromptResponses) {
        _prevSink = Shared.UI.EngineSdk.LocalEventSink;
        _prevMute = Shared.UI.EngineSdk.MuteStdoutWhenLocalSink;
        _prevAuto = new Dictionary<string, string>(Shared.UI.EngineSdk.AutoPromptResponses, System.StringComparer.OrdinalIgnoreCase);

        Shared.UI.EngineSdk.LocalEventSink = sink;
        Shared.UI.EngineSdk.MuteStdoutWhenLocalSink = muteStdout;

        if (autoPromptResponses != null) {
            Shared.UI.EngineSdk.AutoPromptResponses.Clear();
            foreach (KeyValuePair<string, string> kv in autoPromptResponses) {
                Shared.UI.EngineSdk.AutoPromptResponses[kv.Key] = kv.Value;
            }
        }
    }

    public void Dispose() {
        Shared.UI.EngineSdk.LocalEventSink = _prevSink;
        Shared.UI.EngineSdk.MuteStdoutWhenLocalSink = _prevMute;
        Shared.UI.EngineSdk.AutoPromptResponses.Clear();
        foreach (KeyValuePair<string, string> kv in _prevAuto) {
            Shared.UI.EngineSdk.AutoPromptResponses[kv.Key] = kv.Value;
        }
    }
}

