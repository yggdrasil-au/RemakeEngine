using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EngineNet.Core.Util;

/// <summary>
/// Simple, reusable console progress panel with an optional list of active jobs.
/// Rendering is resilient to hosts that don't support cursor positioning.
/// </summary>
public static class ConsoleProgress {
    public sealed class ActiveProcess {
        public string Tool = string.Empty;   // e.g., ffmpeg, vgmstream, txd
        public string File = string.Empty;   // file name only
        public DateTime StartedUtc = DateTime.UtcNow;
    }

    private static readonly object s_consoleLock = new();

    /// <summary>
    /// Starts a background task that periodically draws a progress panel until the token is cancelled.
    /// </summary>
    public static Task StartPanel(
        int total,
        Func<(int processed, int ok, int skip, int err)> snapshot,
        Func<List<ActiveProcess>> activeSnapshot,
        string label,
        CancellationToken token) {
        return Task.Run(() => {
            int panelTop;
            int lastLines;
            TryInitProgressPanel(out panelTop, out lastLines);

            int spinnerIndex = 0;
            char[] spinner = new[] { '|', '/', '-', '\\' };
            while (!token.IsCancellationRequested) {
                (int processed, int ok, int skip, int err) s = snapshot();
                List<ActiveProcess> actives = activeSnapshot();
                List<string> lines = BuildPanelLines(total, s, actives, spinner[spinnerIndex % spinner.Length], label);
                spinnerIndex = (spinnerIndex + 1) & 0x7fffffff;
                DrawPanel(lines, ref panelTop, ref lastLines);
                Thread.Sleep(200);
            }

            // Final draw
            (int processed, int ok, int skip, int err) finalS = snapshot();
            List<ActiveProcess> finalAct = activeSnapshot();
            List<string> finalLines = BuildPanelLines(total, finalS, finalAct, ' ', label);
            DrawPanel(finalLines, ref panelTop, ref lastLines);
        });
    }

    private static void TryInitProgressPanel(out int panelTop, out int lastLines) {
        panelTop = 0;
        lastLines = 0;
        try {
            lock (s_consoleLock) {
                try {
                    Console.Clear();
                    panelTop = 0;
                    lastLines = 0;
                    return;
                } catch {
                    // Fallback: cannot clear (e.g., redirected output). Reserve rows instead.
                }

                panelTop = Console.CursorTop;
                int reserve = EstimateMaxPanelLines();
                for (int i = 0; i < reserve; i++) {
                    Console.WriteLine();
                }

                try { Console.SetCursorPosition(0, panelTop); } catch { /* ignore */ }
                lastLines = reserve;
            }
        } catch {
            // As a last resort, keep defaults (top=0, lastLines=0)
        }
    }

    private static int EstimateMaxPanelLines() {
        int procs = 8;
        try { procs = Math.Max(1, Math.Min(16, Environment.ProcessorCount)); } catch { /* ignore */ }
        // 1 (progress) + 1 (header/none) + procs (active job lines) + 1 (overflow)
        return 1 + 1 + procs + 1;
    }

    private static List<string> BuildPanelLines(int total, (int processed, int ok, int skip, int err) s, List<ActiveProcess> actives, char spinner, string label) {
        List<string> lines = new List<string>(2 + actives.Count);
        if (total < 0) total = 0;

        double percent = Math.Clamp(total == 0 ? 1.0 : (double)s.processed / Math.Max(1, total), 0.0, 1.0);
        int width = 30;
        try { width = Math.Max(10, Math.Min(40, Console.WindowWidth - 60)); } catch { /* ignore */ }
        int filled = (int)Math.Round(percent * width);
        StringBuilder bar = new StringBuilder(width + 48);
        bar.Append(label);
        bar.Append(' ');
        bar.Append('[');
        for (int i = 0; i < width; i++) {
            bar.Append(i < filled ? '#' : '-');
        }
        bar.Append(']');
        bar.Append(' ');
        bar.Append((int)Math.Round(percent * 100));
        bar.Append('%');
        bar.Append(' ');
        bar.Append(s.processed);
        bar.Append('/');
        bar.Append(total);
        bar.Append(" (ok="); bar.Append(s.ok);
        bar.Append(", skip="); bar.Append(s.skip);
        bar.Append(", err="); bar.Append(s.err);
        bar.Append(')');
        lines.Add(bar.ToString());

        if (actives.Count == 0) {
            lines.Add("Active: none");
        } else {
            lines.Add($"Active: {actives.Count}");
            int max = 8;
            try { max = Math.Max(1, Math.Min(16, Environment.ProcessorCount)); } catch { /* ignore */ }
            DateTime now = DateTime.UtcNow;
            foreach (ActiveProcess job in actives.OrderBy(j => j.StartedUtc).Take(max)) {
                TimeSpan elapsed = now - job.StartedUtc;
                string elStr = elapsed.TotalHours >= 1
                    ? $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
                    : $"{elapsed.Minutes:00}:{elapsed.Seconds:00}";
                string file = job.File;
                int maxFile = 50;
                try { maxFile = Math.Max(18, Console.WindowWidth - 20); } catch { /* ignore */ }
                if (file.Length > maxFile) {
                    file = file.Substring(0, maxFile - 3) + "...";
                }
                lines.Add($"  {spinner} {job.Tool} · {file} · {elStr}");
            }
            if (actives.Count > 8) {
                lines.Add($"  … and {actives.Count - Math.Min(actives.Count, 8)} more");
            }
        }
        return lines;
    }

    private static void DrawPanel(IReadOnlyList<string> lines, ref int panelTop, ref int lastLines) {
        lock (s_consoleLock) {
            try {
                Console.SetCursorPosition(0, panelTop);
                int width;
                try { width = Math.Max(20, Console.WindowWidth - 1); } catch { width = 120; }
                for (int i = 0; i < lines.Count; i++) {
                    string line = lines[i];
                    if (line.Length > width) {
                        line = line.Substring(0, width);
                    }
                    Console.Write(line.PadRight(width));
                    if (i < lines.Count - 1) {
                        Console.Write('\n');
                    }
                }
                for (int i = lines.Count; i < lastLines; i++) {
                    Console.Write('\n');
                    Console.Write(new string(' ', Math.Max(20, Console.WindowWidth - 1)));
                }
                lastLines = lines.Count;
                Console.SetCursorPosition(0, panelTop + lastLines);
            } catch {
                try {
                    Console.Write("\r" + (lines.Count > 0 ? lines[0] : string.Empty));
                } catch { /* ignore */ }
            }
        }
    }
}
