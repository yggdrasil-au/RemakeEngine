namespace EngineNet.ScriptEngines.Lua.Global;

/// <summary>
/// Lua wrapper for fully controllable console progress panels.
/// </summary>
internal sealed class LuaConsoleProgress : System.IDisposable {
    private readonly System.Threading.CancellationTokenSource _cts;
    private readonly System.Threading.Tasks.Task _panelTask;
    private readonly List<Shared.IO.UI.EngineSdk.SdkConsoleProgress.ActiveProcess> _activeJobs = new List<Shared.IO.UI.EngineSdk.SdkConsoleProgress.ActiveProcess>();
    private readonly Lock _jobsLock = new Lock();

    private long _processed;
    private long _total;
    private int _ok;
    private int _skip;
    private int _err;
    private string _label;
    private readonly string _id;

    public LuaConsoleProgress(long total, string id, string label) {
        _total = System.Math.Max(0, total);
        _id = id;
        _label = label;
        _cts = new System.Threading.CancellationTokenSource();
        _panelTask = Shared.IO.UI.EngineSdk.SdkConsoleProgress.StartPanel(
            total: () => System.Threading.Interlocked.Read(ref _total),
            snapshot: () => (
                System.Threading.Interlocked.Read(ref _processed),
                System.Threading.Volatile.Read(ref _ok),
                System.Threading.Volatile.Read(ref _skip),
                System.Threading.Volatile.Read(ref _err)
            ),
            activeSnapshot: () => {
                lock (_jobsLock) {
                    return new List<Shared.IO.UI.EngineSdk.SdkConsoleProgress.ActiveProcess>(_activeJobs);
                }
            },
            label: () => System.Threading.Volatile.Read(ref _label),
            token: _cts.Token,
            id: _id
        );
    }

    /// <summary>
    /// Gets the progress identifier.
    /// </summary>
    public string GetId() {
        return _id;
    }

    /// <summary>
    /// Gets the total count.
    /// </summary>
    public long GetTotal() {
        return System.Threading.Interlocked.Read(ref _total);
    }

    /// <summary>
    /// Gets the processed count.
    /// </summary>
    public long GetProcessed() {
        return System.Threading.Interlocked.Read(ref _processed);
    }

    /// <summary>
    /// Gets the ok count.
    /// </summary>
    public int GetOk() {
        return System.Threading.Volatile.Read(ref _ok);
    }

    /// <summary>
    /// Gets the skip count.
    /// </summary>
    public int GetSkip() {
        return System.Threading.Volatile.Read(ref _skip);
    }

    /// <summary>
    /// Gets the error count.
    /// </summary>
    public int GetErr() {
        return System.Threading.Volatile.Read(ref _err);
    }

    /// <summary>
    /// Gets the label text.
    /// </summary>
    public string GetLabel() {
        return System.Threading.Volatile.Read(ref _label);
    }

    /// <summary>
    /// Sets the total count.
    /// </summary>
    public void SetTotal(long total) {
        System.Threading.Interlocked.Exchange(ref _total, System.Math.Max(0, total));
    }

    /// <summary>
    /// Sets the label text.
    /// </summary>
    public void SetLabel(string? label) {
        System.Threading.Volatile.Write(ref _label, label ?? string.Empty);
    }

    /// <summary>
    /// Sets the counter values.
    /// </summary>
    public void SetStats(long processed, int ok, int skip, int err) {
        System.Threading.Interlocked.Exchange(ref _processed, System.Math.Max(0, processed));
        System.Threading.Interlocked.Exchange(ref _ok, System.Math.Max(0, ok));
        System.Threading.Interlocked.Exchange(ref _skip, System.Math.Max(0, skip));
        System.Threading.Interlocked.Exchange(ref _err, System.Math.Max(0, err));
    }

    /// <summary>
    /// Updates the counters by the provided increments.
    /// </summary>
    public void Update(long incProcessed = 1, int incOk = 1, int incSkip = 0, int incErr = 0) {
        long addProcessed = System.Math.Max(0, incProcessed);
        int addOk = System.Math.Max(0, incOk);
        int addSkip = System.Math.Max(0, incSkip);
        int addErr = System.Math.Max(0, incErr);

        if (addProcessed > 0) {
            System.Threading.Interlocked.Add(ref _processed, addProcessed);
        }
        if (addOk > 0) {
            System.Threading.Interlocked.Add(ref _ok, addOk);
        }
        if (addSkip > 0) {
            System.Threading.Interlocked.Add(ref _skip, addSkip);
        }
        if (addErr > 0) {
            System.Threading.Interlocked.Add(ref _err, addErr);
        }
    }

    /// <summary>
    /// Adds a job entry to the active list.
    /// </summary>
    public void AddJob(string? tool, string? file) {
        string safeTool = tool ?? string.Empty;
        string safeFile = file ?? string.Empty;
        lock (_jobsLock) {
            _activeJobs.Add(new Shared.IO.UI.EngineSdk.SdkConsoleProgress.ActiveProcess {
                Tool = safeTool,
                File = safeFile,
                StartedUtc = System.DateTime.UtcNow
            });
        }
    }

    /// <summary>
    /// Removes a job entry by file name.
    /// </summary>
    public void RemoveJob(string? file) {
        string target = file ?? string.Empty;
        lock (_jobsLock) {
            for (int i = 0; i < _activeJobs.Count; i++) {
                if (!string.Equals(_activeJobs[i].File, target, System.StringComparison.Ordinal)) {
                    continue;
                }
                _activeJobs.RemoveAt(i);
                break;
            }
        }
    }

    /// <summary>
    /// Completes the panel task and emits final events.
    /// </summary>
    public void Complete() {
        try {
            if (!_cts.IsCancellationRequested) {
                _cts.Cancel();
            }
            try {
                _panelTask.Wait();
            } catch (System.AggregateException ex) {
                Shared.IO.Diagnostics.Bug($"[LuaConsoleProgress::Complete()] Failed while waiting for panel task completion: {ex}");
            } catch (System.ObjectDisposedException ex) {
                Shared.IO.Diagnostics.Bug($"[LuaConsoleProgress::Complete()] Panel task disposed while waiting: {ex}");
            }
        } catch (System.ObjectDisposedException ex) {
            Shared.IO.Diagnostics.Bug($"[LuaConsoleProgress::Complete()] Cancellation source disposed: {ex}");
        } catch (System.InvalidOperationException ex) {
            Shared.IO.Diagnostics.Bug($"[LuaConsoleProgress::Complete()] Failed to cancel panel task: {ex}");
        }
    }

    /// <summary>
    /// Disposes the panel and cancellation resources.
    /// </summary>
    public void Dispose() {
        Complete();
        _cts.Dispose();
    }
}

/// <summary>
/// SDK module providing script, panel, and console progress helpers for Lua scripts.
/// </summary>
internal static class Progress {

    /// <summary>
    /// Defines the progress API for Lua scripts, separating script-wide completion from panel rendering.
    /// </summary>
    /// <param name="_LuaWorld"></param>
    internal static void CreateProgressModule(LuaWorld _LuaWorld) {
        Shared.IO.UI.EngineSdk.ScriptProgress? activeScriptProgress = null;

        MoonSharp.Interpreter.Table progressTable = _LuaWorld.Progress;
        MoonSharp.Interpreter.Table scriptTable = new MoonSharp.Interpreter.Table(_LuaWorld.LuaScript);
        MoonSharp.Interpreter.Table panelTable = new MoonSharp.Interpreter.Table(_LuaWorld.LuaScript);
        MoonSharp.Interpreter.Table consoleTable = new MoonSharp.Interpreter.Table(_LuaWorld.LuaScript);

        // progress.script.* is for overall script completion (GUI only).
        scriptTable["start"] = (System.Func<int, string?, Shared.IO.UI.EngineSdk.ScriptProgress>)((total, label) => {
            activeScriptProgress = new Shared.IO.UI.EngineSdk.ScriptProgress(total, "s1", label);
            return activeScriptProgress;
        });

        scriptTable["step"] = (System.Action<string?>)((label) => {
            if (activeScriptProgress == null) {
                return;
            }
            activeScriptProgress.Update(1, label);
            if (!string.IsNullOrEmpty(label)) {
                Shared.IO.UI.EngineSdk.PrintLine($"[Step {activeScriptProgress.Current}/{activeScriptProgress.Total}] {label}", System.ConsoleColor.Magenta);
            }
        });

        scriptTable["add_steps"] = (System.Action<int>)((count) => {
            if (activeScriptProgress != null) {
                activeScriptProgress.SetTotal(activeScriptProgress.Total + count);
            }
        });

        scriptTable["finish"] = () => {
            if (activeScriptProgress != null) {
                activeScriptProgress.Complete();
            }
        };

        // progress.panel.new() is for simple panel progress bars.
        panelTable["new"] = (System.Func<long, string?, string?, Shared.IO.UI.EngineSdk.PanelProgress>)((total, id, label) => {
            string pid = string.IsNullOrEmpty(id) ? "p1" : id;
            Shared.IO.UI.EngineSdk.PanelProgress progress = new Shared.IO.UI.EngineSdk.PanelProgress(total, pid, label);
            _LuaWorld.RegisterDisposable(progress);
            return progress;
        });

        // progress.console.new() is for fully controllable panel progress bars.
        consoleTable["new"] = (System.Func<long, string?, string?, LuaConsoleProgress>)((total, id, label) => {
            string pid = string.IsNullOrEmpty(id) ? "c1" : id;
            LuaConsoleProgress progress = new LuaConsoleProgress(total, pid, label ?? string.Empty);
            _LuaWorld.RegisterDisposable(progress);
            return progress;
        });

        progressTable["script"] = scriptTable;
        progressTable["panel"] = panelTable;
        progressTable["console"] = consoleTable;

        _LuaWorld.LuaScript.Globals["progress"] = progressTable;
    }
}