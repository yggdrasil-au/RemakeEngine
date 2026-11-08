using System.Collections.Concurrent;

namespace EngineNet.Core.Services.CommandService;

/// <summary>
/// Owns the bounded stdout/stderr queue and its callback lifetime.
/// Process event threads enqueue lines; the runner alone consumes them and invokes caller callbacks.
/// </summary>
internal sealed class ProcessOutputQueue : IDisposable {
    private readonly BlockingCollection<(string Stream, string Line)> Lines = new(boundedCapacity: 1000);
    private readonly CancellationToken CancellationToken;
    private int FailureLogged;
    private bool IsOpen = true;

    internal ProcessOutputQueue(CancellationToken CancellationToken) {
        this.CancellationToken = CancellationToken;
        StdoutHandler = (_, Event) => Enqueue("stdout", Event.Data);
        StderrHandler = (_, Event) => Enqueue("stderr", Event.Data);
    }

    internal DataReceivedEventHandler StdoutHandler { get; }
    internal DataReceivedEventHandler StderrHandler { get; }

    internal bool TryTake(out (string Stream, string Line) Item, int TimeoutMilliseconds = 0) {
        return Lines.TryTake(out Item, TimeoutMilliseconds);
    }

    /// <summary>Stops new callbacks before completing the queue; shutdown failures retain their existing diagnostics.</summary>
    internal void Complete() {
        Volatile.Write(ref IsOpen, false);
        try {
            Lines.CompleteAdding();
        } catch (ObjectDisposedException Error) {
            Shared.IO.Diagnostics.Bug($"Output queue disposed before completion: {Error.Message}");
        } catch (InvalidOperationException Error) {
            Shared.IO.Diagnostics.Bug($"Failed to complete output queue: {Error.Message}");
        }
    }

    public void Dispose() {
        Lines.Dispose();
    }

    private void Enqueue(string Stream, string? Line) {
        if (Line == null || !Volatile.Read(ref IsOpen)) {
            return;
        }
        try {
            Lines.Add((Stream, Line), CancellationToken);
        } catch (ObjectDisposedException Error) {
            LogFailureOnce(Error);
        } catch (InvalidOperationException Error) {
            LogFailureOnce(Error);
        } catch (OperationCanceledException Error) {
            LogFailureOnce(Error);
        }
    }

    private void LogFailureOnce(Exception Error) {
        if (Interlocked.Exchange(ref FailureLogged, 1) == 0) {
            Shared.IO.Diagnostics.Bug($"Output queue add failed: {Error.Message}");
        }
    }
}
