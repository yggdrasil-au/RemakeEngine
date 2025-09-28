# EngineNet.csproj

## Purpose
- Define the build targets, language rules, and package dependencies for the EngineNet executable.
- Link the CLI/engine runtime with the Avalonia GUI project via `ProjectReference`.

## Intent
- Multi-target .NET 8 builds across generic, Windows-specific, and optional macOS configurations without forcing unused workloads.
- Keep nullable reference types, explicit usings, and latest C# features enabled for consistency with the codebase.

## Goals
- Ship a console/GUI hybrid application that can run cross-platform while enabling Windows-specific UI features when available.
- Reference scripting, TOML, and SQLite packages (`MoonSharp`, `Jint`, `Tomlyn`, `Microsoft.Data.Sqlite`) required by runtime components.
- Allow developers to opt into the macOS target only when the toolchain is installed (`IncludeMacOS` property gate).

## Must Remain
- `net8.0` stays the shared baseline TFM; Windows UI target remains `net8.0-windows10.0.19041.0` with Windows Forms gated by platform.
- Nullable context stays enabled, implicit usings remain disabled to keep namespaces explicit.
- Package versions are centrally managed here to align scripting engines and data access libraries.

## Unimplemented
- Automated per-platform RID-specific publishing profiles.
- Conditional package references for macOS or Linux specific tooling.

## TODOs
- Evaluate trimming / single-file publishing requirements once deployment targets are known.
- Consider moving package version management to Directory.Packages.props if the solution grows.

## Issues
- Developers lacking Windows workloads may need to disable the Windows TFM manually; documentation around required workloads is minimal.
