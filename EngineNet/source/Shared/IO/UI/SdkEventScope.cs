namespace EngineNet.Shared.IO.UI;

/// <summary>
/// Disposable scope to set Shared.IO.UI.EngineSdk.LocalEventSink, MuteStdoutWhenLocalSink
/// and optionally seed AutoPromptResponses, restoring prior values on dispose.
/// </summary>
public sealed class SdkEventScope:System.IDisposable {
    private readonly System.Action<Dictionary<string, object?>>? _prevSink;
    private readonly bool _prevMute;
    private readonly Dictionary<string, string> _prevAuto;

    public SdkEventScope(
        System.Action<Dictionary<string, object?>>? sink,
        bool muteStdout,
        IDictionary<string, string>? autoPromptResponses) {
        _prevSink = Shared.IO.UI.EngineSdk.LocalEventSink;
        _prevMute = Shared.IO.UI.EngineSdk.MuteStdoutWhenLocalSink;
        _prevAuto = new Dictionary<string, string>(Shared.IO.UI.EngineSdk.AutoPromptResponses, System.StringComparer.OrdinalIgnoreCase);

        Shared.IO.UI.EngineSdk.LocalEventSink = sink;
        Shared.IO.UI.EngineSdk.MuteStdoutWhenLocalSink = muteStdout;

        if (autoPromptResponses == null) return;

        Shared.IO.UI.EngineSdk.AutoPromptResponses.Clear();
        foreach (KeyValuePair<string, string> kv in autoPromptResponses) {
            Shared.IO.UI.EngineSdk.AutoPromptResponses[kv.Key] = kv.Value;
        }
    }

    public void Dispose() {
        Shared.IO.UI.EngineSdk.LocalEventSink = _prevSink;
        Shared.IO.UI.EngineSdk.MuteStdoutWhenLocalSink = _prevMute;
        Shared.IO.UI.EngineSdk.AutoPromptResponses.Clear();
        foreach (KeyValuePair<string, string> kv in _prevAuto) {
            Shared.IO.UI.EngineSdk.AutoPromptResponses[kv.Key] = kv.Value;
        }
    }
}

