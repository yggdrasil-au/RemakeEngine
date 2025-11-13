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
        _prevSink = Core.Utils.EngineSdk.LocalEventSink;
        _prevMute = Core.Utils.EngineSdk.MuteStdoutWhenLocalSink;
        _prevAuto = new Dictionary<string, string>(Core.Utils.EngineSdk.AutoPromptResponses, System.StringComparer.OrdinalIgnoreCase);

        Core.Utils.EngineSdk.LocalEventSink = sink;
        Core.Utils.EngineSdk.MuteStdoutWhenLocalSink = muteStdout;

        if (autoPromptResponses != null) {
            Core.Utils.EngineSdk.AutoPromptResponses.Clear();
            foreach (KeyValuePair<string, string> kv in autoPromptResponses) {
                Core.Utils.EngineSdk.AutoPromptResponses[kv.Key] = kv.Value;
            }
        }
    }

    public void Dispose() {
        Core.Utils.EngineSdk.LocalEventSink = _prevSink;
        Core.Utils.EngineSdk.MuteStdoutWhenLocalSink = _prevMute;
        Core.Utils.EngineSdk.AutoPromptResponses.Clear();
        foreach (KeyValuePair<string, string> kv in _prevAuto) {
            Core.Utils.EngineSdk.AutoPromptResponses[kv.Key] = kv.Value;
        }
    }
}

