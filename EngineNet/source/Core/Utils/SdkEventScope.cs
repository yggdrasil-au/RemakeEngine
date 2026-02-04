namespace EngineNet.Core.Utils;

using System.Collections.Generic;

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
        _prevSink = Core.UI.EngineSdk.LocalEventSink;
        _prevMute = Core.UI.EngineSdk.MuteStdoutWhenLocalSink;
        _prevAuto = new Dictionary<string, string>(Core.UI.EngineSdk.AutoPromptResponses, System.StringComparer.OrdinalIgnoreCase);

        Core.UI.EngineSdk.LocalEventSink = sink;
        Core.UI.EngineSdk.MuteStdoutWhenLocalSink = muteStdout;

        if (autoPromptResponses != null) {
            Core.UI.EngineSdk.AutoPromptResponses.Clear();
            foreach (KeyValuePair<string, string> kv in autoPromptResponses) {
                Core.UI.EngineSdk.AutoPromptResponses[kv.Key] = kv.Value;
            }
        }
    }

    public void Dispose() {
        Core.UI.EngineSdk.LocalEventSink = _prevSink;
        Core.UI.EngineSdk.MuteStdoutWhenLocalSink = _prevMute;
        Core.UI.EngineSdk.AutoPromptResponses.Clear();
        foreach (KeyValuePair<string, string> kv in _prevAuto) {
            Core.UI.EngineSdk.AutoPromptResponses[kv.Key] = kv.Value;
        }
    }
}

