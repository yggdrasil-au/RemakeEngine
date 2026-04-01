namespace EngineNet.Core.FileHandlers.Formats.p3d;

internal static class Main {

    /// <summary>
    /// Runs p3d parser/list/export workflows.
    /// Supported args: [path] [--recurse] [--list] [-o|--out folder] [--parse-only]
    /// </summary>
    internal static bool Run(List<string> args, System.Threading.CancellationToken cancellationToken) {
        try {
            if (args.Count == 0) {
                Core.Diagnostics.Log("[p3d] Usage: p3d <file-or-directory> [--recurse] [--list] [-o|--out folder] [--parse-only]");
                return false;
            }

            P3dRunOptions options = ParseOptions(args);
            P3dRunMode mode = ResolveMode(options);

            bool requireRecursiveFlag = mode != P3dRunMode.ParseOnly;
            List<string> files = EnumerateP3dFiles(options.InputPath, options.Recurse, requireRecursiveFlag);
            if (files.Count == 0) {
                Core.Diagnostics.Log($"[p3d] No .p3d files found for '{options.InputPath}'.");
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
                            Core.Diagnostics.Log($"[p3d] OK {System.IO.Path.GetFileName(file)} | chunks={chunks.Count}");
                            break;
                        case P3dRunMode.ListHighLevel:
                            ListHighLevelTypes(file, chunks);
                            break;
                        case P3dRunMode.ExportGltf:
                            if (string.IsNullOrWhiteSpace(options.OutputDirectory)) {
                                throw new P3dParseException("Output directory is required in export mode.");
                            }

                            P3dGltfExporter.ExportAllToGltf(file, chunks, options.OutputDirectory);
                            Core.Diagnostics.Log($"[p3d] Exported {System.IO.Path.GetFileName(file)}");
                            break;
                    }

                    success++;
                } catch (Exception ex) {
                    failed++;
                    Core.Diagnostics.Log($"[p3d] FAIL {System.IO.Path.GetFileName(file)} | {ex.Message}");
                }
            }

            string modeName = mode switch {
                P3dRunMode.ParseOnly => "parse",
                P3dRunMode.ListHighLevel => "list",
                P3dRunMode.ExportGltf => "export",
                _ => "unknown",
            };

            Core.Diagnostics.Log($"[p3d] Completed ({modeName}) | success={success} failed={failed} total={files.Count}");
            return failed == 0;
        } catch (Exception ex) {
            Core.Diagnostics.Log($"Error: {ex.Message}");
            return false;
        }
    }

    private static void ListHighLevelTypes(string file, IReadOnlyList<Chunk> chunks) {
        Core.Diagnostics.Log($"[p3d] Listing {System.IO.Path.GetFileName(file)}");
        List<HighLevelType> highLevelTypes = P3dHighLevel.ParseHighLevelTypes(chunks);

        foreach (HighLevelType highLevelType in highLevelTypes) {
            switch (highLevelType) {
                case HighLevelType.MeshType meshType:
                    Core.Diagnostics.Log($"Mesh: {meshType.Mesh.Name}");
                    break;
                case HighLevelType.SkinType skinType:
                    Core.Diagnostics.Log($"Skin: {skinType.Skin.Name}");
                    break;
            }
        }
    }

    private static P3dRunMode ResolveMode(P3dRunOptions options) {
        if (options.List) {
            return P3dRunMode.ListHighLevel;
        }

        if (!string.IsNullOrWhiteSpace(options.OutputDirectory) && !options.ParseOnly) {
            return P3dRunMode.ExportGltf;
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
    }
}
