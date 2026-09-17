using EngineNet.Shared.IO.UI;

namespace EngineNet.GUI.Services;

/// <summary>
/// GUI Specific utility methods, to simplify interaction between each Page and the engines Methods.
/// Provides helpers for running engine operations while piping output into the shared
/// <see cref="OperationOutputService"/> and surfacing SDK prompts through the GUI.
/// </summary>
public static class EngineOperationRunner {

    /// <summary>
    /// Executes an engine operation while routing output, events, and prompts to the GUI services.
    /// </summary>
    /// <typeparam name="TResult">Type returned by the underlying executor.</typeparam>
    /// <param name="moduleName">Name of the module/game for contextual logging.</param>
    /// <param name="operationName">Friendly operation name displayed to the user.</param>
    /// <param name="executor">Callback that runs the actual engine work.</param>
    /// <param name="autoPromptResponses">Optional automatic prompt answers.</param>
    public static async Task<TResult> RunAsync<TResult>(
        string moduleName,
        string operationName,
        System.Func<Core.ProcessRunner.OutputHandler, Core.ProcessRunner.EventHandler, Core.ProcessRunner.StdinProvider, Task<TResult>> executor,
        IDictionary<string, string>? autoPromptResponses = null
    ) {

        OperationOutputService outputService = OperationOutputService.Instance;
        outputService.StartOperation(operationName: operationName, gameName: moduleName);

        object promptLock = new();
        string? lastPromptMessage = null;
        string? lastPromptId = null;
        bool lastPromptSecret = false;
        string? lastPromptType = null;
        bool lastPromptDefault = false;

        void CapturePrompt(Dictionary<string, object?> evt) {
            if (!evt.TryGetValue(key: "event", out object? typeObj)) {
                return;
            }

            string type = typeObj?.ToString() ?? "";
            if (type == EngineSdk.Events.Prompt || type == EngineSdk.Events.ColorPrompt || type == EngineSdk.Events.Confirm) {
                lock (promptLock) {
                    lastPromptType = type;
                    lastPromptMessage = evt.TryGetValue(key: "message", out object? msg) ? msg?.ToString() : "Input required";
                    lastPromptId = evt.TryGetValue(key: "id", out object? idObj) ? idObj?.ToString() : null;
                    lastPromptSecret = evt.TryGetValue(key: "secret", out object? secretObj) && secretObj is bool b && b;
                    if (type == EngineSdk.Events.Confirm) {
                        lastPromptDefault = evt.TryGetValue(key: "default", out object? defObj) && defObj is bool d && d;
                    }
                }
            }
        }

        Core.ProcessRunner.EventHandler eventHandler = evt => {
            CapturePrompt(evt: evt);
            outputService.HandleEvent(evt: evt);
        };

        Core.ProcessRunner.OutputHandler outputHandler = (string line, string stream) => outputService.AddOutput(text: line, stream: stream);

        Core.ProcessRunner.StdinProvider stdinProvider = () => {
            string? promptMessage;
            string? promptId;
            bool promptSecret;
            string? promptType;
            bool promptDefault;
            lock (promptLock) {
                promptMessage = lastPromptMessage ?? "Input required";
                promptId = lastPromptId;
                promptSecret = lastPromptSecret;
                promptType = lastPromptType;
                promptDefault = lastPromptDefault;
            }

            string? response = null;
            try {
                global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(callback: async () => {
                    try {
                        string title = !string.IsNullOrWhiteSpace(promptId) ? promptId : "Input Required";
                        if (promptType == EngineSdk.Events.Confirm) {
                            bool? res = await OperationOutputService.Instance.RequestConfirmPromptAsync(title: title, promptMessage, defaultValue: promptDefault);
                            response = res.HasValue ? (res.Value ? "y" : "n") : string.Empty;
                        } else {
                            response = await OperationOutputService.Instance.RequestTextPromptAsync(title: title, promptMessage, defaultValue: null, secret: promptSecret);
                        }
                    } catch (System.Exception ex) {
                        outputService.AddOutput(text: $"Prompt dialog failed: {ex.Message}", stream: "stderr");
                        response = string.Empty;
                    }
                }).Wait();
            } catch (System.Exception ex) {
                outputService.AddOutput(text: $"Prompt dispatch failed: {ex.Message}", stream: "stderr");
                response = string.Empty;
            }

            return response ?? string.Empty;
        };

        System.Action<Dictionary<string, object?>> sink = evt => { CapturePrompt(evt: evt); outputService.HandleEvent(evt: evt); };
        using (new Shared.IO.UI.SdkEventScope(sink: sink, muteStdout: true, autoPromptResponses: autoPromptResponses)) {
            return await Task.Run(function: () => executor(arg1: outputHandler, arg2: eventHandler, arg3: stdinProvider)).ConfigureAwait(continueOnCapturedContext: false);
        }
    }
}
