using MoonSharp.Interpreter;

namespace EngineNet.ScriptEngines.Lua.Global;

using Core.DiscImages;

/// <summary>
/// Optical disc image (.iso) inspection, virtual streaming, and extraction module for Lua scripts.
/// Exposes the global <c>iso</c> table, <c>sdk.iso</c>, and <c>sdk.extract_iso</c>.
/// </summary>
internal static class LuaIso {
    /* :: :: Methods :: START :: */

    /// <summary>
    /// Registers the <c>iso</c> global module, <c>sdk.iso</c>, and <c>sdk.extract_iso</c> in <paramref name="_LuaWorld"/>.
    /// </summary>
    internal static void CreateIsoModule(LuaWorld _LuaWorld) {
        _LuaWorld.IsoModule[key: "open"] = DynValue.NewCallback(callBack: (_, args) => OpenIso(_LuaWorld: _LuaWorld, args: args));
        _LuaWorld.IsoModule[key: "is_iso"] = DynValue.NewCallback(callBack: (_, args) => ProbeIsIso(args: args));
        _LuaWorld.IsoModule[key: "extract"] = DynValue.NewCallback(callBack: (_, args) => ExtractIsoBatch(_LuaWorld: _LuaWorld, args: args));

        _LuaWorld.Sdk.Table[key: "iso"] = _LuaWorld.IsoModule;
        _LuaWorld.Sdk.Table[key: "extract_iso"] = _LuaWorld.IsoModule.Get(key: "extract");
        _LuaWorld.LuaScript.Globals[key: "iso"] = _LuaWorld.IsoModule;
    }

    /// <summary>
    /// Implements <c>iso.open(path)</c>, returning <c>(IsoReader, nil)</c> on success or <c>(nil, error_message)</c> on failure.
    /// </summary>
    private static DynValue OpenIso(LuaWorld _LuaWorld, CallbackArguments args) {
        int offset = args.Count > 0 && args[index: 0].Type == DataType.Table ? 1 : 0;
        if (args.Count <= offset || args[index: offset].Type != DataType.String) {
            return DynValue.NewTuple(values: [DynValue.Nil, DynValue.NewString(str: "iso.open(path) requires a string file path")]);
        }

        string rawPath = args[index: offset].String;
        if (!Security.TryGetAllowedCanonicalPathWithPrompt(path: rawPath, canonicalPath: out string safePath)) {
            return DynValue.NewTuple(values: [DynValue.Nil, DynValue.NewString(str: $"Access denied to path: '{rawPath}'")]);
        }

        IIsoReader? reader = null;
        try {
            reader = IsoFactory.Open(filePath: safePath);
            _LuaWorld.RegisterDisposable(disposable: reader);
            Table handleTable = CreateIsoReaderTable(_LuaWorld: _LuaWorld, reader: reader);
            return DynValue.NewTuple(values: [DynValue.NewTable(table: handleTable), DynValue.Nil]);
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"[LuaIso.cs::OpenIso()]:: catch: {ex.Message}");
            if (reader is not null) {
                _LuaWorld.UnregisterDisposable(disposable: reader);
                reader.Dispose();
            }
            return DynValue.NewTuple(values: [DynValue.Nil, DynValue.NewString(str: ex.Message)]);
        }
    }

    /// <summary>
    /// Implements <c>iso.is_iso(path)</c>, returning <c>(boolean, format_or_nil)</c>.
    /// </summary>
    private static DynValue ProbeIsIso(CallbackArguments args) {
        int offset = args.Count > 0 && args[index: 0].Type == DataType.Table ? 1 : 0;
        if (args.Count <= offset || args[index: offset].Type != DataType.String) {
            return DynValue.NewTuple(values: [DynValue.False, DynValue.Nil]);
        }

        string rawPath = args[index: offset].String;
        if (!Security.TryGetAllowedCanonicalPathWithPrompt(path: rawPath, canonicalPath: out string safePath)) {
            return DynValue.NewTuple(values: [DynValue.False, DynValue.Nil]);
        }

        bool isDisc = IsoFactory.TryProbeFormat(filePath: safePath, detectedFormat: out string? format);
        return DynValue.NewTuple(values: [
            DynValue.NewBoolean(v: isDisc),
            isDisc && format is not null ? DynValue.NewString(str: format) : DynValue.Nil
        ]);
    }

    /// <summary>
    /// Implements <c>sdk.extract_iso(iso_path, dest_dir [, options])</c>, returning <c>(boolean, error_or_nil)</c>.
    /// </summary>
    private static DynValue ExtractIsoBatch(LuaWorld _LuaWorld, CallbackArguments args) {
        int offset = args.Count > 0 && args[index: 0].Type == DataType.Table && args.Count >= 3 && args[index: 1].Type == DataType.String ? 1 : 0;
        if (args.Count <= offset + 1 || args[index: offset].Type != DataType.String || args[index: offset + 1].Type != DataType.String) {
            return DynValue.NewTuple(values: [DynValue.False, DynValue.NewString(str: "sdk.extract_iso(iso_path, dest_dir [, options]) requires iso_path and dest_dir strings")]);
        }

        string isoPath = args[index: offset].String;
        string destDir = args[index: offset + 1].String;

        if (!Security.TryGetAllowedCanonicalPathWithPrompt(path: isoPath, canonicalPath: out string safeIsoPath)) {
            return DynValue.NewTuple(values: [DynValue.False, DynValue.NewString(str: $"Access denied to ISO path: '{isoPath}'")]);
        }
        if (!Security.TryGetAllowedCanonicalPathWithPrompt(path: destDir, canonicalPath: out string safeDestDir)) {
            return DynValue.NewTuple(values: [DynValue.False, DynValue.NewString(str: $"Access denied to destination path: '{destDir}'")]);
        }

        string? filter = null;
        bool overwrite = true;
        System.IProgress<IsoProgressInfo>? progressReporter = null;

        if (args.Count > offset + 2) {
            DynValue optArg = args[index: offset + 2];
            if (optArg.Type == DataType.String) {
                filter = optArg.String;
            } else if (optArg.Type == DataType.Table) {
                Table optTable = optArg.Table;
                DynValue patVal = optTable.Get(key: "pattern");
                if (patVal.Type == DataType.Nil) {
                    patVal = optTable.Get(key: "filter");
                }
                if (patVal.Type == DataType.String) {
                    filter = patVal.String;
                }
                DynValue ovVal = optTable.Get(key: "overwrite");
                if (ovVal.Type == DataType.Boolean) {
                    overwrite = ovVal.Boolean;
                }
                progressReporter = TryCreateProgressReporter(progressDyn: optTable.Get(key: "progress_panel"));
            }
        }

        try {
            using IIsoReader reader = IsoFactory.Open(filePath: safeIsoPath);
            IsoExtractResult result = reader.ExtractAll(
                options: new IsoExtractOptions {
                    Destination = safeDestDir,
                    Filter = filter,
                    Overwrite = overwrite
                },
                progress: progressReporter
            );

            if (!result.Success) {
                return DynValue.NewTuple(values: [DynValue.False, DynValue.NewString(str: result.Error ?? "ISO extraction failed")]);
            }
            return DynValue.NewTuple(values: [DynValue.True, DynValue.Nil]);
        } catch (System.Exception ex) {
            Shared.IO.Diagnostics.Bug($"[LuaIso.cs::ExtractIsoBatch()]:: catch: {ex.Message}");
            return DynValue.NewTuple(values: [DynValue.False, DynValue.NewString(str: ex.Message)]);
        }
    }

    /// <summary>
    /// Creates the MoonSharp table representing an <c>IsoReader</c> instance, supporting both colon (<c>reader:method()</c>) and dot (<c>reader.method()</c>) invocations.
    /// </summary>
    private static Table CreateIsoReaderTable(LuaWorld _LuaWorld, IIsoReader reader) {
        Table handleTable = new(owner: _LuaWorld.LuaScript);

        handleTable[key: "format"] = DynValue.NewCallback(callBack: (_, _) => DynValue.NewString(str: reader.Format));
        handleTable[key: "volume_id"] = DynValue.NewCallback(callBack: (_, _) => DynValue.NewString(str: reader.VolumeLabel));
        handleTable[key: "total_size"] = DynValue.NewCallback(callBack: (_, _) => DynValue.NewNumber(num: reader.TotalSize));
        handleTable[key: "partition_offset"] = DynValue.NewCallback(callBack: (_, _) => DynValue.NewNumber(num: reader.PartitionOffset));

        handleTable[key: "exists"] = DynValue.NewCallback(callBack: (_, args) => {
            int offset = args.Count > 0 && args[index: 0].Type == DataType.Table ? 1 : 0;
            if (args.Count <= offset || args[index: offset].Type != DataType.String) {
                return DynValue.False;
            }
            return DynValue.NewBoolean(v: reader.FileExists(internalPath: args[index: offset].String));
        });

        handleTable[key: "get_entry"] = DynValue.NewCallback(callBack: (_, args) => {
            int offset = args.Count > 0 && args[index: 0].Type == DataType.Table ? 1 : 0;
            if (args.Count <= offset || args[index: offset].Type != DataType.String) {
                return DynValue.Nil;
            }
            IsoEntry? entry = reader.GetEntry(internalPath: args[index: offset].String);
            return entry is null ? DynValue.Nil : DynValue.NewTable(table: ToIsoEntryTable(script: _LuaWorld.LuaScript, entry: entry));
        });

        handleTable[key: "list_files"] = DynValue.NewCallback(callBack: (_, args) => {
            int offset = args.Count > 0 && args[index: 0].Type == DataType.Table ? 1 : 0;
            string subPath = "/";
            bool recursive = true;

            if (args.Count > offset && args[index: offset].Type == DataType.String) {
                subPath = args[index: offset].String;
            }
            if (args.Count > offset + 1 && args[index: offset + 1].Type == DataType.Boolean) {
                recursive = args[index: offset + 1].Boolean;
            }

            System.Collections.Generic.IReadOnlyList<IsoEntry> entries = reader.ListEntries(subPath: subPath, recursive: recursive);
            Table listTable = new(owner: _LuaWorld.LuaScript);
            for (int i = 0; i < entries.Count; i++) {
                listTable[key: i + 1] = DynValue.NewTable(table: ToIsoEntryTable(script: _LuaWorld.LuaScript, entry: entries[index: i]));
            }
            return DynValue.NewTable(table: listTable);
        });

        handleTable[key: "read_file"] = DynValue.NewCallback(callBack: (_, args) => {
            int offset = args.Count > 0 && args[index: 0].Type == DataType.Table ? 1 : 0;
            if (args.Count <= offset || args[index: offset].Type != DataType.String) {
                return DynValue.NewTuple(values: [DynValue.Nil, DynValue.NewString(str: "reader:read_file(internal_path [, max_bytes]) requires an internal_path string")]);
            }

            string internalPath = args[index: offset].String;
            int maxBytes = -1;
            if (args.Count > offset + 1 && args[index: offset + 1].Type == DataType.Number) {
                maxBytes = (int)args[index: offset + 1].Number;
            }

            try {
                byte[] bytes = reader.ReadFileBytes(internalPath: internalPath, maxBytes: maxBytes);
                // Preserve exact raw byte values 0..255 in MoonSharp string using Latin1 encoding
                string rawString = System.Text.Encoding.Latin1.GetString(bytes: bytes);
                return DynValue.NewTuple(values: [DynValue.NewString(str: rawString), DynValue.Nil]);
            } catch (System.Exception ex) {
                Shared.IO.Diagnostics.Bug($"[LuaIso.cs::read_file()]:: catch: {ex.Message}");
                return DynValue.NewTuple(values: [DynValue.Nil, DynValue.NewString(str: ex.Message)]);
            }
        });

        handleTable[key: "extract_file"] = DynValue.NewCallback(callBack: (_, args) => {
            int offset = args.Count > 0 && args[index: 0].Type == DataType.Table ? 1 : 0;
            if (args.Count <= offset + 1 || args[index: offset].Type != DataType.String || args[index: offset + 1].Type != DataType.String) {
                return DynValue.NewTuple(values: [DynValue.False, DynValue.NewString(str: "reader:extract_file(internal_path, host_destination [, overwrite]) requires internal_path and host_destination strings")]);
            }

            string internalPath = args[index: offset].String;
            string hostDest = args[index: offset + 1].String;
            bool overwrite = args.Count <= offset + 2 || args[index: offset + 2].Type != DataType.Boolean || args[index: offset + 2].Boolean;

            if (!Security.TryGetAllowedCanonicalPathWithPrompt(path: hostDest, canonicalPath: out string safeDest)) {
                return DynValue.NewTuple(values: [DynValue.False, DynValue.NewString(str: $"Access denied to destination path: '{hostDest}'")]);
            }

            try {
                reader.ExtractFile(internalPath: internalPath, destinationPath: safeDest, overwrite: overwrite);
                return DynValue.NewTuple(values: [DynValue.True, DynValue.Nil]);
            } catch (System.Exception ex) {
                Shared.IO.Diagnostics.Bug($"[LuaIso.cs::extract_file()]:: catch: {ex.Message}");
                return DynValue.NewTuple(values: [DynValue.False, DynValue.NewString(str: ex.Message)]);
            }
        });

        handleTable[key: "extract"] = DynValue.NewCallback(callBack: (_, args) => {
            int offset = args.Count > 0 && args[index: 0].Type == DataType.Table && ReferenceEquals(objA: args[index: 0].Table, objB: handleTable) ? 1 : 0;
            if (args.Count <= offset) {
                return DynValue.NewTable(table: ToIsoExtractResultTable(script: _LuaWorld.LuaScript, result: new IsoExtractResult {
                    Success = false,
                    Error = "reader:extract(options) requires an options table or destination string"
                }));
            }

            string? dest = null;
            string? filter = null;
            bool overwrite = false;
            System.IProgress<IsoProgressInfo>? progressReporter = null;

            DynValue firstArg = args[index: offset];
            if (firstArg.Type == DataType.Table) {
                Table optTable = firstArg.Table;
                DynValue destVal = optTable.Get(key: "destination");
                if (destVal.Type == DataType.Nil) {
                    destVal = optTable.Get(key: "output");
                }
                if (destVal.Type == DataType.String) {
                    dest = destVal.String;
                }

                DynValue patVal = optTable.Get(key: "pattern");
                if (patVal.Type == DataType.Nil) {
                    patVal = optTable.Get(key: "filter");
                }
                if (patVal.Type == DataType.String) {
                    filter = patVal.String;
                }

                DynValue ovVal = optTable.Get(key: "overwrite");
                if (ovVal.Type == DataType.Boolean) {
                    overwrite = ovVal.Boolean;
                }

                progressReporter = TryCreateProgressReporter(progressDyn: optTable.Get(key: "progress_panel"));
            } else if (firstArg.Type == DataType.String) {
                dest = firstArg.String;
                if (args.Count > offset + 1 && args[index: offset + 1].Type == DataType.String) {
                    filter = args[index: offset + 1].String;
                }
                if (args.Count > offset + 2 && args[index: offset + 2].Type == DataType.Boolean) {
                    overwrite = args[index: offset + 2].Boolean;
                }
            }

            if (string.IsNullOrWhiteSpace(dest)) {
                return DynValue.NewTable(table: ToIsoExtractResultTable(script: _LuaWorld.LuaScript, result: new IsoExtractResult {
                    Success = false,
                    Error = "Destination path is required in IsoExtractOptions."
                }));
            }

            if (!Security.TryGetAllowedCanonicalPathWithPrompt(path: dest, canonicalPath: out string safeDest)) {
                return DynValue.NewTable(table: ToIsoExtractResultTable(script: _LuaWorld.LuaScript, result: new IsoExtractResult {
                    Success = false,
                    Error = $"Access denied to destination path: '{dest}'"
                }));
            }

            IsoExtractResult extractResult = reader.ExtractAll(
                options: new IsoExtractOptions {
                    Destination = safeDest,
                    Filter = filter,
                    Overwrite = overwrite
                },
                progress: progressReporter
            );

            return DynValue.NewTable(table: ToIsoExtractResultTable(script: _LuaWorld.LuaScript, result: extractResult));
        });

        handleTable[key: "close"] = DynValue.NewCallback(callBack: (_, _) => {
            try {
                _LuaWorld.UnregisterDisposable(disposable: reader);
                reader.Dispose();
            } catch (System.Exception ex) {
                Shared.IO.Diagnostics.Bug($"[LuaIso.cs::close()]:: catch: {ex.Message}");
            }
            return DynValue.Nil;
        });

        handleTable[key: "dispose"] = handleTable.Get(key: "close");
        return handleTable;
    }

    private static Table ToIsoEntryTable(Script script, IsoEntry entry) {
        Table tbl = new(owner: script);
        tbl[key: "path"] = entry.Path;
        tbl[key: "name"] = entry.Name;
        tbl[key: "size"] = entry.Size;
        tbl[key: "is_dir"] = entry.IsDirectory;
        tbl[key: "sector"] = entry.StartSector;
        tbl[key: "offset"] = entry.AbsoluteOffset;
        return tbl;
    }

    private static Table ToIsoExtractResultTable(Script script, IsoExtractResult result) {
        Table tbl = new(owner: script);
        tbl[key: "success"] = result.Success;
        tbl[key: "files_extracted"] = result.FilesExtracted;
        tbl[key: "files_skipped"] = result.FilesSkipped;
        tbl[key: "bytes_extracted"] = result.BytesExtracted;
        if (result.Error is not null) {
            tbl[key: "error"] = result.Error;
        }
        return tbl;
    }

    private static System.IProgress<IsoProgressInfo>? TryCreateProgressReporter(DynValue progressDyn) {
        if (progressDyn.Type != DataType.UserData || progressDyn.UserData?.Object is null) {
            return null;
        }

        object progObj = progressDyn.UserData.Object;
        if (progObj is Shared.IO.UI.EngineSdk.PanelProgress panelProgress) {
            int lastProcessed = 0;
            return new System.Progress<IsoProgressInfo>(handler: info => {
                int delta = info.ProcessedFiles - lastProcessed;
                if (delta > 0) {
                    lastProcessed = info.ProcessedFiles;
                    panelProgress.SetTotal(total: System.Math.Max(val1: 1, val2: info.TotalFiles));
                    panelProgress.SetLabel(label: $"Extracting {info.CurrentFile}");
                    panelProgress.Update(inc: delta);
                }
            });
        }

        if (progObj is LuaConsoleProgress consoleProgress) {
            int lastProcessed = 0;
            return new System.Progress<IsoProgressInfo>(handler: info => {
                int delta = info.ProcessedFiles - lastProcessed;
                if (delta > 0) {
                    lastProcessed = info.ProcessedFiles;
                    consoleProgress.SetTotal(total: System.Math.Max(val1: 1, val2: info.TotalFiles));
                    consoleProgress.SetLabel(label: $"Extracting {info.CurrentFile}");
                    consoleProgress.Update(incProcessed: delta, incOk: delta, incSkip: 0, incErr: 0);
                }
            });
        }

        return null;
    }

    /* :: :: Methods :: END :: */
}
