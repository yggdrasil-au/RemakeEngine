namespace EngineNet.Core.ScriptEngines;

internal sealed class QuickBmsScriptAction : Helpers.IAction {
    private readonly string _scriptPath;   // path to .bms
    private readonly string _moduleRoot;   // Game_Root
    private readonly string _projectRoot;  // Project root (for Tools.local.json)
    private readonly string _inputDir;
    private readonly string _outputDir;
    private readonly string? _extension;

    public QuickBmsScriptAction(
        string scriptPath,
        string moduleRoot,
        string projectRoot,
        string inputDir,
        string outputDir,
        string? extension) {
        _scriptPath = scriptPath;
        _moduleRoot = moduleRoot;
        _projectRoot = projectRoot;
        _inputDir = inputDir;
        _outputDir = outputDir;
        _extension = string.IsNullOrWhiteSpace(extension) ? null : extension;
    }

    public async System.Threading.Tasks.Task ExecuteAsync(Tools.IToolResolver tools, System.Threading.CancellationToken cancellationToken = default) {
        // Validate script and directories
        if (string.IsNullOrWhiteSpace(_scriptPath) || !System.IO.File.Exists(_scriptPath)) {
            throw new System.IO.FileNotFoundException("BMS script not found", _scriptPath);
        }
        if (string.IsNullOrWhiteSpace(_inputDir) || !System.IO.Directory.Exists(_inputDir)) {
            throw new System.IO.DirectoryNotFoundException($"Input directory not found: {_inputDir}");
        }
        if (string.IsNullOrWhiteSpace(_outputDir)) {
            throw new System.ArgumentException("Output directory is required.");
        }
        System.IO.Directory.CreateDirectory(_outputDir);

        // Determine required QuickBMS version from module Tools.toml
        string toolsToml = System.IO.Path.Combine(_moduleRoot, "Tools.toml");
        string? requiredVersion = null;
        try {
            if (System.IO.File.Exists(toolsToml)) {
                List<Dictionary<string, object?>> toolDefs = Tools.SimpleToml.ReadTools(toolsToml);
                Dictionary<string, object?>? qbms = toolDefs.FirstOrDefault(t => t.TryGetValue("name", out object? n) && string.Equals(n?.ToString(), "QuickBMS", System.StringComparison.OrdinalIgnoreCase));
                if (qbms is not null && qbms.TryGetValue("version", out object? v)) {
                    requiredVersion = v?.ToString();
                }
            }
        } catch { /* ignore parse issues; best-effort */ }

        // Resolve installed QuickBMS from Tools.local.json
        string toolsLocal = new[] {
            System.IO.Path.Combine(_projectRoot, "Tools.local.json"),
            System.IO.Path.Combine(_projectRoot, "tools.local.json"),
        }.FirstOrDefault(System.IO.File.Exists) ?? string.Empty;

        string? installedExe = null;
        string? installedVersion = null;
        if (!string.IsNullOrEmpty(toolsLocal)) {
            try {
                using System.IO.FileStream fs = System.IO.File.OpenRead(toolsLocal);
                using System.Text.Json.JsonDocument doc = await System.Text.Json.JsonDocument.ParseAsync(fs);
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object && doc.RootElement.TryGetProperty("QuickBMS", out System.Text.Json.JsonElement qbms) && qbms.ValueKind == System.Text.Json.JsonValueKind.Object) {
                    if (qbms.TryGetProperty("exe", out System.Text.Json.JsonElement exe) && exe.ValueKind == System.Text.Json.JsonValueKind.String) {
                        installedExe = exe.GetString();
                    }
                    if (qbms.TryGetProperty("version", out System.Text.Json.JsonElement ver) && ver.ValueKind == System.Text.Json.JsonValueKind.String) {
                        installedVersion = ver.GetString();
                    }
                }
            } catch {
#if DEBUG
                Program.Direct.Console.WriteLine("Warning: Failed to parse Tools.local.json for QuickBMS info.");
#endif

            }
        }

        // Enforce required version (if declared)
        if (!string.IsNullOrWhiteSpace(requiredVersion)) {
            if (string.IsNullOrWhiteSpace(installedVersion) || !string.Equals(installedVersion, requiredVersion, System.StringComparison.OrdinalIgnoreCase)) {
                throw new System.InvalidOperationException($"Missing QuickBMS {requiredVersion} - please run the 'Download Tools' operation. Tools.local.json shows '{installedVersion ?? "<not installed>"}'.");
            }
        }

        // Resolve exe path (prefer Tools.local.json; fallback to tool resolver)
        string resolvedExe = installedExe ?? tools.ResolveToolPath("QuickBMS");
        if (string.IsNullOrWhiteSpace(resolvedExe) || !System.IO.File.Exists(resolvedExe)) {
            throw new System.IO.FileNotFoundException("QuickBMS is not installed or could not be resolved. Run the 'Download Tools' operation.", resolvedExe);
        }

        // Build args for built-in extractor and run
        List<string> extractorArgs = new List<string> {
            "--quickbms", resolvedExe,
            "--script", _scriptPath,
            "--input", _inputDir,
            "--output", _outputDir,
        };
        if (!string.IsNullOrWhiteSpace(_extension)) {
            extractorArgs.Add("--extension");
            extractorArgs.Add(_extension!);
        }

        // Run synchronously; extractor already streams output
        bool ok = Helpers.QuickBmsExtractor.Run(extractorArgs);
        if (!ok) {
            throw new System.InvalidOperationException("QuickBMS extraction failed.");
        }

        await System.Threading.Tasks.Task.CompletedTask;
    }
}
