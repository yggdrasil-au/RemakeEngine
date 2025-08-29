using System;
using System.Collections.Concurrent;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace RemakeEngine.Core;

public sealed class ProcessRunner
{
    public delegate void OutputHandler(string line, string streamName);
    public delegate void EventHandler(Dictionary<string, object?> evt);
    public delegate string? StdinProvider();

    public bool Execute(
        IList<string> commandParts,
        string opTitle,
        OutputHandler? onOutput = null,
        EventHandler? onEvent = null,
        StdinProvider? stdinProvider = null,
        IDictionary<string, object?>? envOverrides = null,
        CancellationToken cancellationToken = default)
    {
        if (commandParts is null || commandParts.Count < 2)
        {
            onOutput?.Invoke($"Operation '{opTitle}' has no script to execute. Skipping.", "stderr");
            return false;
        }

        // Verbose: show exactly what will be executed
        try
        {
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("\nExecuting command:");
            Console.ResetColor();
            Console.WriteLine("  " + FormatCommand(commandParts));
            Console.WriteLine($"  cwd: {Directory.GetCurrentDirectory()}");
            if (envOverrides != null && envOverrides.Count > 0)
            {
                Console.WriteLine("  env overrides:");
                foreach (var kv in envOverrides)
                {
                    Console.WriteLine($"    {kv.Key}={kv.Value}");
                }
            }
        }
        catch { /* ignore formatting errors */ }

        var psi = new ProcessStartInfo
        {
            FileName = commandParts[0],
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        for (int i = 1; i < commandParts.Count; i++)
            psi.ArgumentList.Add(commandParts[i]);

        foreach (DictionaryEntry de in Environment.GetEnvironmentVariables())
            psi.Environment[de.Key.ToString()!] = de.Value?.ToString() ?? string.Empty;

        if (envOverrides != null)
            foreach (var kv in envOverrides)
                psi.Environment[kv.Key] = kv.Value?.ToString() ?? string.Empty;

        // Encourage line-buffered UTF-8 for child Python
        if (!psi.Environment.ContainsKey("PYTHONUNBUFFERED"))
            psi.Environment["PYTHONUNBUFFERED"] = "1";
        if (!psi.Environment.ContainsKey("PYTHONIOENCODING"))
            psi.Environment["PYTHONIOENCODING"] = "utf-8";

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var q = new BlockingCollection<(string stream, string line)>(boundedCapacity: 1000);

        DataReceivedEventHandler outHandler = (_, e) => { if (e.Data != null) q.Add(("stdout", e.Data)); };
        DataReceivedEventHandler errHandler = (_, e) => { if (e.Data != null) q.Add(("stderr", e.Data)); };

        try
        {
            if (!proc.Start())
                throw new InvalidOperationException("Failed to start process");

            proc.OutputDataReceived += outHandler;
            proc.ErrorDataReceived += errHandler;
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            bool awaitingPrompt = false;
            string? lastPromptMsg = null;
            bool suppressPromptEcho = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ENGINE_SUPPRESS_PROMPT_ECHO"));

            void SendToChild(string? text)
            {
                try
                {
                    proc.StandardInput.WriteLine(text ?? string.Empty);
                    proc.StandardInput.Flush();
                }
                catch { /* ignore */ }
            }

            string? HandleLine(string line, string streamName)
            {
                if (line.StartsWith(Types.RemakePrefix, StringComparison.Ordinal))
                {
                    var payload = line.Substring(Types.RemakePrefix.Length).Trim();
                    try
                    {
                        var evt = JsonSerializer.Deserialize<Dictionary<string, object?>>(payload) ?? new();
                        if (evt.TryGetValue("event", out var evType) && (evType?.ToString() ?? "") == "prompt")
                        {
                            if (!suppressPromptEcho)
                                onEvent?.Invoke(evt);
                            return evt.TryGetValue("message", out var msg) ? msg?.ToString() ?? "Input required" : "Input required";
                        }
                        onEvent?.Invoke(evt);
                        return null;
                    }
                    catch
                    {
                        onOutput?.Invoke(line, streamName);
                        return null;
                    }
                }
                else
                {
                    onOutput?.Invoke(line, streamName);
                    return null;
                }
            }

            while (!proc.HasExited)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    TryTerminate(proc);
                    onEvent?.Invoke(new Dictionary<string, object?> { ["event"] = "end", ["success"] = false, ["exit_code"] = 130 });
                    return false;
                }

                if (!q.TryTake(out var item, 100))
                    continue;

                var promptMsg = HandleLine(item.line, item.stream);
                if (promptMsg != null)
                {
                    awaitingPrompt = true;
                    lastPromptMsg = promptMsg;
                }

                if (awaitingPrompt && !proc.HasExited)
                {
                    string? ans;
                    try { ans = stdinProvider?.Invoke(); }
                    catch { ans = string.Empty; }
                    SendToChild(ans);
                    awaitingPrompt = false;
                    lastPromptMsg = null;
                }
            }

            // Drain any remaining
            while (q.TryTake(out var item))
            {
                var promptMsg = HandleLine(item.line, item.stream);
                if (promptMsg != null)
                {
                    string? ans;
                    try { ans = stdinProvider?.Invoke(); }
                    catch { ans = string.Empty; }
                    SendToChild(ans);
                }
            }

            var rc = proc.ExitCode;
            if (rc == 0)
            {
                onEvent?.Invoke(new Dictionary<string, object?> { ["event"] = "end", ["success"] = true, ["exit_code"] = 0 });
                return true;
            }
            else
            {
                onEvent?.Invoke(new Dictionary<string, object?> { ["event"] = "end", ["success"] = false, ["exit_code"] = rc });
                return false;
            }
        }
        catch (OperationCanceledException)
        {
            TryTerminate(proc);
            onEvent?.Invoke(new Dictionary<string, object?> { ["event"] = "end", ["success"] = false, ["exit_code"] = 130 });
            return false;
        }
        catch (FileNotFoundException)
        {
            onEvent?.Invoke(new Dictionary<string, object?> { ["event"] = "error", ["kind"] = "FileNotFoundError", ["message"] = "Command or script not found." });
            return false;
        }
        catch (Exception ex)
        {
            onEvent?.Invoke(new Dictionary<string, object?> { ["event"] = "error", ["kind"] = "Exception", ["message"] = ex.Message });
            return false;
        }
        finally
        {
            try
            {
                proc.OutputDataReceived -= outHandler;
                proc.ErrorDataReceived -= errHandler;
            }
            catch { /* ignore */ }
        }
    }

    private static void TryTerminate(Process proc)
    {
        try
        {
            if (!proc.HasExited)
            {
                proc.Kill(entireProcessTree: true);
            }
        }
        catch { /* ignore */ }
    }

    private static string FormatCommand(IList<string> parts)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < parts.Count; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(QuoteArg(parts[i] ?? string.Empty));
        }
        return sb.ToString();
    }

    private static string QuoteArg(string arg)
    {
        if (string.IsNullOrEmpty(arg)) return "\"\"";
        bool needsQuotes = arg.IndexOfAny(new[] { ' ', '\t', '"' }) >= 0;
        if (!needsQuotes) return arg;
        // Escape embedded quotes by backslash
        var escaped = arg.Replace("\"", "\\\"");
        return "\"" + escaped + "\"";
    }
}
