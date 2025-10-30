using System.Collections.Generic;
using System.Threading.Tasks;

namespace EngineNet.Interface.GUI;

/// <summary>
/// GUI Specific utility methods, to simplify interaction between each Page and the engines Methods.
/// Provides helpers for running engine operations while piping output into the shared
/// <see cref="OperationOutputService"/> and surfacing SDK prompts through the GUI.
/// </summary>
internal static class Utils {

    /// <summary>
    /// Executes an engine operation while routing output, events, and prompts to the GUI services.
    /// </summary>
    /// <typeparam name="TResult">Type returned by the underlying executor.</typeparam>
    /// <param name="engine">Engine instance.</param>
    /// <param name="moduleName">Name of the module/game for contextual logging.</param>
    /// <param name="operationName">Friendly operation name displayed to the user.</param>
    /// <param name="executor">Callback that runs the actual engine work.</param>
    /// <param name="autoPromptResponses">Optional automatic prompt answers.</param>
    internal static async Task<TResult> ExecuteEngineOperationAsync<TResult>(
        Core.Engine engine,
        string moduleName,
        string operationName,
        System.Func<Core.ProcessRunner.OutputHandler, Core.ProcessRunner.EventHandler, Core.ProcessRunner.StdinProvider, Task<TResult>> executor,
        IDictionary<string, string>? autoPromptResponses = null) {

        OperationOutputService outputService = OperationOutputService.Instance;
        outputService.StartOperation(operationName, moduleName);

        object promptLock = new object();
        string? lastPromptMessage = null;
        string? lastPromptId = null;
        bool lastPromptSecret = false;

        void CapturePrompt(Dictionary<string, object?> evt) {
            if (!evt.TryGetValue("event", out object? typeObj)) {
                return;
            }

            if (typeObj?.ToString() == "prompt") {
                lock (promptLock) {
                    lastPromptMessage = evt.TryGetValue("message", out object? msg) ? msg?.ToString() : "Input required";
                    lastPromptId = evt.TryGetValue("id", out object? idObj) ? idObj?.ToString() : null;
                    lastPromptSecret = evt.TryGetValue("secret", out object? secretObj) && secretObj is bool b && b;
                }
            }
        }

        Core.ProcessRunner.EventHandler eventHandler = evt => {
            CapturePrompt(evt);
            outputService.HandleEvent(evt);
        };

        Core.ProcessRunner.OutputHandler outputHandler = (string line, string stream) => outputService.AddOutput(line, stream);

        Core.ProcessRunner.StdinProvider stdinProvider = () => {
            string? promptMessage;
            string? promptId;
            bool promptSecret;
            lock (promptLock) {
                promptMessage = lastPromptMessage ?? "Input required";
                promptId = lastPromptId;
                promptSecret = lastPromptSecret;
            }

            string? response = null;
            try {
                global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () => {
                    try {
                        string title = !string.IsNullOrWhiteSpace(promptId) ? promptId : "Input Required";
                        response = await PromptHelpers.TextAsync(title, promptMessage ?? "Enter value", defaultValue: null, promptSecret);
                    } catch (System.Exception ex) {
                        outputService.AddOutput($"Prompt dialog failed: {ex.Message}", stream: "stderr");
                        response = string.Empty;
                    }
                }).Wait();
            } catch (System.Exception ex) {
                outputService.AddOutput(text: $"Prompt dispatch failed: {ex.Message}", stream: "stderr");
                response = string.Empty;
            }

            return response ?? string.Empty;
        };

        System.Action<Dictionary<string, object?>>? previousSink = Core.Utils.EngineSdk.LocalEventSink;
        bool previousMute = Core.Utils.EngineSdk.MuteStdoutWhenLocalSink;
        Dictionary<string, string> previousAutoPrompts = new Dictionary<string, string>(Core.Utils.EngineSdk.AutoPromptResponses);

        try {
            if (autoPromptResponses != null && autoPromptResponses.Count > 0) {
                Core.Utils.EngineSdk.AutoPromptResponses.Clear();
                foreach (KeyValuePair<string, string> kvp in autoPromptResponses) {
                    Core.Utils.EngineSdk.AutoPromptResponses[kvp.Key] = kvp.Value;
                }
            }

            Core.Utils.EngineSdk.LocalEventSink = evt => {
                CapturePrompt(evt);
                outputService.HandleEvent(evt);
            };
            Core.Utils.EngineSdk.MuteStdoutWhenLocalSink = true;

            return await executor(outputHandler, eventHandler, stdinProvider).ConfigureAwait(false);
        } finally {
            Core.Utils.EngineSdk.LocalEventSink = previousSink;
            Core.Utils.EngineSdk.MuteStdoutWhenLocalSink = previousMute;

            Core.Utils.EngineSdk.AutoPromptResponses.Clear();
            foreach (KeyValuePair<string, string> kvp in previousAutoPrompts) {
                Core.Utils.EngineSdk.AutoPromptResponses[kvp.Key] = kvp.Value;
            }
        }
    }
}