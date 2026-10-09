# EngineNet.Shared

EngineNet.Shared is the reusable support library for the engine's serialization helpers and other low-level utilities that are consumed by Core and ScriptEngines.

## Responsibilities
- Provide TOML helpers for module tools, config, and operation manifests.
- Provide JSON helpers and DOM-to-plain-object conversion utilities.
- Provide YAML helpers for script-facing and runtime document conversion.
- Provide diagnostics and logging helpers used by Core and script runtimes.
- Provide reusable long-path-aware filesystem operations and scoped short directory aliases for legacy external processes.
- Keep shared parsing logic separate from Core so it can be referenced without a cycle.

## Area Map
- `Serialization/Toml/`: TOML read/write helpers built on Tomlyn.
- `Serialization/Json/`: JSON loading helpers and document-model conversion.
- `Serialization/Yaml/`: YAML parsing and serialization helpers.
- `Serialization/DocModelConverter.cs`: shared DOM-to-plain-object conversion helpers.
- `IO/Diagnostics.cs`: shared logging, trace, and exception logging helper (namespace `EngineNet.Shared.IO`).
- `IO/LongPathIO.cs`: path normalization and file/directory operations; Windows drive and UNC paths receive extended-length prefixes when needed.
- `IO/ShortPathScope.cs`: disposable aliases for paths consumed by legacy external tools. Scratch aliases are created under `<Shared.State.RootPath>/TMP/LongPaths` and removed when the scope is disposed.
- `IO/UI/`: engine SDK bridge helpers used by scripts and interfaces for output/events (namespace `EngineNet.Shared.IO.UI`).

Long-path conversion does not grant access. Callers must perform their own authorization checks before IO. `LongPathIO.GetAbsolutePath` resolves relative paths against an explicitly supplied base directory, or the process current directory when omitted. `LongPathIO.GetRelativePath` removes extended-length prefixes for path comparison so a short root can be compared with a deep enumerated path. `ShortPathScope` requires the configured engine root and reports an error when a required Windows directory alias cannot be created.

## Relationship To Other Projects
- `EngineNet.Core` references this library for manifest, config, and tool parsing.
- `EngineNet.Shared.IO.Diagnostics` is implemented here so runtime logging stays available without a Core-to-Shared cycle.
- `EngineNet` references this library transitively through Core and also lists it directly in the solution for clarity.

## Related Docs
- [../../readme.md](../../readme.md)
- [../Core/readme.md](../Core/readme.md)
- [../../../Readme.md](../../../Readme.md)
