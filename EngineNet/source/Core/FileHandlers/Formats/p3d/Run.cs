namespace EngineNet.Core.FileHandlers.Formats.p3d;

internal static class Main {

    private const string UnsupportedRootParityNote = "[p3d] Note: Parser parity is currently aligned with p3dtoolsRust root handling. Unsupported root variants (for example DataFileCompressed) are intentionally reported as unsupported.";

    /// <summary>
    /// Runs p3d parser/list/export workflows.
    /// Supported args: [path] [--recurse] [--list] [-o|--out folder] [--parse-only] [--export gltf|obj] [--mode parse|list|gltf|obj]
    /// Limitation: To maintain parity with p3dtoolsRust, only DataFile root chunks are currently supported.
    /// Compressed/root variants (for example DataFileCompressed) are expected to fail with an unsupported message.
    /// </summary>
    internal static bool Run(List<string> args, System.Threading.CancellationToken cancellationToken) {
        try {
            if (args.Count == 0) {
                Shared.IO.Diagnostics.Log("[p3d] Usage: p3d <file-or-directory> [--recurse] [--list] [-o|--out folder] [--parse-only] [--export gltf|obj] [--mode parse|list|gltf|obj]");
                Shared.IO.UI.EngineSdk.PrintLine("Usage: p3d <file-or-directory> [--recurse] [--list] [-o|--out folder] [--parse-only] [--export gltf|obj] [--mode parse|list|gltf|obj]");
                return false;
            }

            P3dRunOptions options = ParseOptions(args);
            P3dRunMode mode = ResolveMode(options);

            Shared.IO.Diagnostics.Log(UnsupportedRootParityNote);

            bool requireRecursiveFlag = mode != P3dRunMode.ParseOnly;
            List<string> files = EnumerateP3dFiles(options.InputPath, options.Recurse, requireRecursiveFlag);
            if (files.Count == 0) {
                Shared.IO.Diagnostics.Log($"[p3d] No .p3d files found for '{options.InputPath}'.");
                Shared.IO.UI.EngineSdk.PrintLine($"No .p3d files found for '{options.InputPath}'.", System.ConsoleColor.Red);
                return false;
            }

            int success = 0;
            int failed = 0;
            foreach (string file in files) {
                cancellationToken.ThrowIfCancellationRequested();

                try {
                    byte[] bytes = System.IO.File.ReadAllBytes(file);
                    List<Chunk> chunks = P3dParser.ParseFile(bytes);

                    switch (mode) {
                        case P3dRunMode.ParseOnly:
                            Shared.IO.Diagnostics.Log($"[p3d] OK {System.IO.Path.GetFileName(file)} | chunks={chunks.Count}");
                            Shared.IO.UI.EngineSdk.PrintLine($"[OK] {System.IO.Path.GetFileName(file)} | chunks={chunks.Count}", System.ConsoleColor.Green);
                            break;
                        case P3dRunMode.ListHighLevel:
                            ListHighLevelTypes(file, chunks);
                            break;
                        case P3dRunMode.ExportGltf:
                            if (string.IsNullOrWhiteSpace(options.OutputDirectory)) {
                                throw new P3dParseException("Output directory is required in export mode.");
                            }

                            P3dGltfExporter.ExportAllToGltf(file, chunks, options.OutputDirectory);
                            Shared.IO.Diagnostics.Log($"[p3d] Exported {System.IO.Path.GetFileName(file)}");
                            Shared.IO.UI.EngineSdk.PrintLine($"[OK] Exported {System.IO.Path.GetFileName(file)}", System.ConsoleColor.Green);
                            break;
                        case P3dRunMode.ExportObj:
                            if (string.IsNullOrWhiteSpace(options.OutputDirectory)) {
                                throw new P3dParseException("Output directory is required in export mode.");
                            }

                            P3dObjExporter.ExportAllToObj(file, chunks, options.OutputDirectory);
                            Shared.IO.Diagnostics.Log($"[p3d] Exported OBJ {System.IO.Path.GetFileName(file)}");
                            Shared.IO.UI.EngineSdk.PrintLine($"[OK] Exported OBJ {System.IO.Path.GetFileName(file)}", System.ConsoleColor.Green);
                            break;
                    }

                    success++;
                } catch (Exception ex) {
                    Shared.IO.Diagnostics.Bug($"[p3d] Per-file processing failed for '{file}'.", ex);
                    failed++;
                    string details = ex.InnerException is null ? ex.Message : $"{ex.Message} | {ex.InnerException.Message}";
                    Shared.IO.Diagnostics.Log($"[p3d] FAIL {System.IO.Path.GetFileName(file)} | {details}");
                    Shared.IO.UI.EngineSdk.PrintLine($"[FAIL] {System.IO.Path.GetFileName(file)} | {details}", System.ConsoleColor.Red);
                }
            }

            string modeName = mode switch {
                P3dRunMode.ParseOnly => "parse",
                P3dRunMode.ListHighLevel => "list",
                P3dRunMode.ExportGltf => "export",
                P3dRunMode.ExportObj => "export-obj",
                _ => "unknown",
            };

            Shared.IO.Diagnostics.Log($"[p3d] Completed ({modeName}) | success={success} failed={failed} total={files.Count}");
            System.ConsoleColor summaryColor = failed == 0 ? System.ConsoleColor.Green : System.ConsoleColor.Red;
            Shared.IO.UI.EngineSdk.PrintLine($"[p3d] Completed ({modeName}) | success={success} failed={failed} total={files.Count}", summaryColor);
            return failed == 0;
        } catch (Exception ex) {
            Shared.IO.Diagnostics.Bug($"[p3d] Run catch triggered: {ex}");
            string details = ex.InnerException is null ? ex.Message : $"{ex.Message} | {ex.InnerException.Message}";
            Shared.IO.Diagnostics.Log($"Error: {details}");
            Shared.IO.UI.EngineSdk.PrintLine($"Error: {details}", System.ConsoleColor.Red);
            return false;
        }
    }

    private static void ListHighLevelTypes(string file, IReadOnlyList<Chunk> chunks) {
        Shared.IO.Diagnostics.Log($"[p3d] Listing {System.IO.Path.GetFileName(file)}");
        Shared.IO.UI.EngineSdk.PrintLine($"Listing {System.IO.Path.GetFileName(file)}");
        List<HighLevelType> highLevelTypes = P3dHighLevel.ParseHighLevelTypes(chunks);

        foreach (HighLevelType highLevelType in highLevelTypes) {
            switch (highLevelType) {
                case HighLevelType.MeshType meshType:
                    Shared.IO.Diagnostics.Log($"Mesh: {meshType.Mesh.Name}");
                    Shared.IO.UI.EngineSdk.PrintLine($"Mesh: {meshType.Mesh.Name}");
                    break;
                case HighLevelType.SkinType skinType:
                    Shared.IO.Diagnostics.Log($"Skin: {skinType.Skin.Name}");
                    Shared.IO.UI.EngineSdk.PrintLine($"Skin: {skinType.Skin.Name}");
                    break;
            }
        }
    }

    private static P3dRunMode ResolveMode(P3dRunOptions options) {
        if (options.List) {
            return P3dRunMode.ListHighLevel;
        }

        if (!string.IsNullOrWhiteSpace(options.OutputDirectory) && !options.ParseOnly) {
            return options.ExportFormat switch {
                P3dExportFormat.Obj => P3dRunMode.ExportObj,
                _ => P3dRunMode.ExportGltf,
            };
        }

        return P3dRunMode.ParseOnly;
    }

    private static List<string> EnumerateP3dFiles(string inputPath, bool recurse, bool requireRecursiveFlag) {
        string fullPath = System.IO.Path.GetFullPath(inputPath);
        if (System.IO.File.Exists(fullPath)) {
            return fullPath.EndsWith(".p3d", StringComparison.OrdinalIgnoreCase)
                ? new List<string> { fullPath }
                : new List<string>();
        }

        if (!System.IO.Directory.Exists(fullPath)) {
            throw new P3dParseException($"Input path does not exist: {inputPath}");
        }

        if (requireRecursiveFlag && !recurse) {
            throw new P3dParseException("Not recursing into directory without --recurse flag.");
        }

        return System.IO.Directory
            .EnumerateFiles(fullPath, "*.p3d", recurse ? System.IO.SearchOption.AllDirectories : System.IO.SearchOption.TopDirectoryOnly)
            .ToList();
    }

    private static P3dRunOptions ParseOptions(IReadOnlyList<string> args) {
        P3dRunOptions options = new();

        for (int i = 0; i < args.Count; i++) {
            string current = args[i];

            if (string.IsNullOrWhiteSpace(current)) {
                continue;
            }

            switch (current) {
                case "-i":
                case "--in":
                case "--source":
                    options.InputPath = ReadOptionValue(args, ref i, current);
                    break;
                case "-o":
                case "--out":
                case "--output":
                case "--output_dir":
                    options.OutputDirectory = ReadOptionValue(args, ref i, current);
                    break;
                case "--export":
                case "--export-format":
                    options.ExportFormat = ParseExportFormat(ReadOptionValue(args, ref i, current));
                    break;
                case "-m":
                case "--mode":
                    ApplyModeOption(options, ReadOptionValue(args, ref i, current));
                    break;
                case "-r":
                case "--recurse":
                    options.Recurse = true;
                    break;
                case "--list":
                    options.List = true;
                    break;
                case "--parse":
                case "--parse-only":
                    options.ParseOnly = true;
                    break;
                default:
                    if (current.StartsWith("-", StringComparison.Ordinal)) {
                        throw new P3dParseException($"Unknown argument '{current}'.");
                    }

                    if (string.IsNullOrWhiteSpace(options.InputPath)) {
                        options.InputPath = current;
                    } else {
                        throw new P3dParseException($"Unexpected extra positional argument '{current}'.");
                    }

                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(options.InputPath)) {
            throw new P3dParseException("Missing input path argument for p3d operation.");
        }

        options.InputPath = System.IO.Path.GetFullPath(options.InputPath);
        if (!string.IsNullOrWhiteSpace(options.OutputDirectory)) {
            options.OutputDirectory = System.IO.Path.GetFullPath(options.OutputDirectory);
        }

        return options;
    }

    private static void ApplyModeOption(P3dRunOptions options, string value) {
        string mode = value.Trim().ToLowerInvariant();
        switch (mode) {
            case "parse":
            case "parse-only":
                options.ParseOnly = true;
                options.List = false;
                break;
            case "list":
                options.List = true;
                options.ParseOnly = false;
                break;
            case "gltf":
            case "export":
                options.ParseOnly = false;
                options.List = false;
                options.ExportFormat = P3dExportFormat.Gltf;
                break;
            case "obj":
            case "export-obj":
                options.ParseOnly = false;
                options.List = false;
                options.ExportFormat = P3dExportFormat.Obj;
                break;
            default:
                throw new P3dParseException($"Unknown mode '{value}'. Supported modes: parse, list, gltf, obj.");
        }
    }

    private static P3dExportFormat ParseExportFormat(string value) {
        string export = value.Trim().ToLowerInvariant();
        return export switch {
            "gltf" => P3dExportFormat.Gltf,
            "obj" => P3dExportFormat.Obj,
            _ => throw new P3dParseException($"Unknown export format '{value}'. Supported formats: gltf, obj."),
        };
    }

    private static string ReadOptionValue(IReadOnlyList<string> args, ref int index, string optionName) {
        if (index + 1 >= args.Count) {
            throw new P3dParseException($"Option '{optionName}' expects a value.");
        }

        index++;
        return args[index];
    }

    private enum P3dRunMode {
        ParseOnly,
        ListHighLevel,
        ExportGltf,
        ExportObj,
    }

    private enum P3dExportFormat {
        Gltf,
        Obj,
    }

    private sealed class P3dRunOptions {
        internal string InputPath {
            get; set;
        } = string.Empty;

        internal string? OutputDirectory {
            get; set;
        }

        internal bool Recurse {
            get; set;
        }

        internal bool List {
            get; set;
        }

        internal bool ParseOnly {
            get; set;
        }

        internal P3dExportFormat ExportFormat {
            get; set;
        } = P3dExportFormat.Gltf;
    }
}
