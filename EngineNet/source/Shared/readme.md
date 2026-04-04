# EngineNet.Shared

EngineNet.Shared is the reusable support library for the engine's serialization helpers and other low-level utilities that are consumed by Core and ScriptEngines.

## Responsibilities
- Provide TOML helpers for module tools, config, and operation manifests.
- Provide JSON helpers and DOM-to-plain-object conversion utilities.
- Provide YAML helpers for script-facing and runtime document conversion.
- Provide diagnostics and logging helpers used by Core and script runtimes.
- Keep shared parsing logic separate from Core so it can be referenced without a cycle.

## Area Map
- `Serialization/Toml/`: TOML read/write helpers built on Tomlyn.
- `Serialization/Json/`: JSON loading helpers and document-model conversion.
- `Serialization/Yaml/`: YAML parsing and serialization helpers.
- `Serialization/DocModelConverter.cs`: shared DOM-to-plain-object conversion helpers.
- `IO/Diagnostics.cs`: shared logging, trace, and exception logging helper (namespace `EngineNet.Shared.IO`).
- `IO/UI/`: engine SDK bridge helpers used by scripts and interfaces for output/events (namespace `EngineNet.Shared.IO.UI`).

## Relationship To Other Projects
- `EngineNet.Core` references this library for manifest, config, and tool parsing.
- `EngineNet.Shared.IO.Diagnostics` is implemented here so runtime logging stays available without a Core-to-Shared cycle.
- `EngineNet` references this library transitively through Core and also lists it directly in the solution for clarity.

## Related Docs
- [../../readme.md](../../readme.md)
- [../Core/readme.md](../Core/readme.md)
- [../../../Readme.md](../../../Readme.md)
