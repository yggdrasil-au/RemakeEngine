// System usings
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

// internal usings
using EngineNet.Core.ScriptEngines.Helpers;
using EngineNet.Tools;

namespace EngineNet.Core.ScriptEngines;

public sealed class QuickBmsScriptAction : IAction {
    private readonly String _scriptPath;   // path to .bms
    private readonly String _moduleRoot;   // Game_Root
    private readonly String _projectRoot;  // Project root (for Tools.local.json)
    private readonly String _inputDir;
    private readonly String _outputDir;
    private readonly String? _extension;

    public QuickBmsScriptAction(
        String scriptPath,
        String moduleRoot,
        String projectRoot,
        String inputDir,
        String outputDir,
        String? extension)
    {
        _scriptPath = scriptPath;
        _moduleRoot = moduleRoot;
        _projectRoot = projectRoot;
        _inputDir = inputDir;
        _outputDir = outputDir;
        _extension = String.IsNullOrWhiteSpace(extension) ? null : extension;
    }

    public async Task ExecuteAsync(IToolResolver tools, CancellationToken cancellationToken = default) {
        // Validate script and directories
        if (String.IsNullOrWhiteSpace(_scriptPath) || !File.Exists(_scriptPath)) {
            throw new FileNotFoundException("BMS script not found", _scriptPath);
        }
        if (String.IsNullOrWhiteSpace(_inputDir) || !Directory.Exists(_inputDir)) {
            throw new DirectoryNotFoundException($"Input directory not found: {_inputDir}");
        }
        if (String.IsNullOrWhiteSpace(_outputDir)) {
            throw new ArgumentException("Output directory is required.");
        }
        Directory.CreateDirectory(_outputDir);

        // Determine required QuickBMS version from module Tools.toml
        String toolsToml = Path.Combine(_moduleRoot, "Tools.toml");
        String? requiredVersion = null;
        try {
            if (File.Exists(toolsToml)) {
                List<Dictionary<String, Object?>> toolDefs = SimpleToml.ReadTools(toolsToml);
                Dictionary<String, Object?>? qbms = toolDefs.FirstOrDefault(t => t.TryGetValue("name", out Object? n) && String.Equals(n?.ToString(), "QuickBMS", StringComparison.OrdinalIgnoreCase));
                if (qbms is not null && qbms.TryGetValue("version", out Object? v)) {
                    requiredVersion = v?.ToString();
                }
            }
        } catch { /* ignore parse issues; best-effort */ }

        // Resolve installed QuickBMS from Tools.local.json
        String toolsLocal = new[] {
            Path.Combine(_projectRoot, "Tools.local.json"),
            Path.Combine(_projectRoot, "tools.local.json"),
        }.FirstOrDefault(File.Exists) ?? String.Empty;

        String? installedExe = null;
        String? installedVersion = null;
        if (!String.IsNullOrEmpty(toolsLocal)) {
            try {
                using FileStream fs = File.OpenRead(toolsLocal);
                using JsonDocument doc = JsonDocument.Parse(fs);
                if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("QuickBMS", out JsonElement qbms) && qbms.ValueKind == JsonValueKind.Object) {
                    if (qbms.TryGetProperty("exe", out JsonElement exe) && exe.ValueKind == JsonValueKind.String) {
                        installedExe = exe.GetString();
                    }
                    if (qbms.TryGetProperty("version", out JsonElement ver) && ver.ValueKind == JsonValueKind.String) {
                        installedVersion = ver.GetString();
                    }
                }
            } catch { }
        }

        // Enforce required version (if declared)
        if (!String.IsNullOrWhiteSpace(requiredVersion)) {
            if (String.IsNullOrWhiteSpace(installedVersion) || !String.Equals(installedVersion, requiredVersion, StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidOperationException($"Missing QuickBMS {requiredVersion} — please run the 'Download Tools' operation. Tools.local.json shows '{installedVersion ?? "<not installed>"}'.");
            }
        }

        // Resolve exe path (prefer Tools.local.json; fallback to tool resolver)
        String resolvedExe = installedExe ?? tools.ResolveToolPath("QuickBMS");
        if (String.IsNullOrWhiteSpace(resolvedExe) || !File.Exists(resolvedExe)) {
            throw new FileNotFoundException("QuickBMS is not installed or could not be resolved. Run the 'Download Tools' operation.", resolvedExe);
        }

        // Build args for built-in extractor and run
        List<String> extractorArgs = new List<String> {
            "--quickbms", resolvedExe,
            "--script", _scriptPath,
            "--input", _inputDir,
            "--output", _outputDir,
        };
        if (!String.IsNullOrWhiteSpace(_extension)) {
            extractorArgs.Add("--extension");
            extractorArgs.Add(_extension!);
        }

        // Run synchronously; extractor already streams output
        Boolean ok = QuickBmsExtractor.Run(extractorArgs);
        if (!ok) {
            throw new InvalidOperationException("QuickBMS extraction failed.");
        }

        await Task.CompletedTask;
    }
}
