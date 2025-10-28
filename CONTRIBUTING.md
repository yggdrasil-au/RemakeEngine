# Contributing to Remake Engine

[![SonarQube Cloud](https://sonarcloud.io/images/project_badges/sonarcloud-light.svg)](https://sonarcloud.io/summary/new_code?id=yggdrasil-au_RemakeEngine2)

Thank you for helping build Remake Engine. This guide explains our development philosophy, local workflows, and the expectations for every pull request.

## Development Principles
- **Module-first improvements:** We evolve the engine while building or maintaining real game modules. Avoid speculative engine changes that are not driven by a concrete module requirement.
- **UI exception:** Improvements to the Avalonia GUI that improve usability or coherence are welcome even without a specific module driver.
- **Specs stay in sync:** Every engine component is documented in `EngineNet/specs/`. When behaviour changes, update the matching `.spec.md` file (format described in `EngineNet/specs/spec.spec.md`).

## C# code style
see [Style.md](Style.md)


## Ways to Use the Engine
- **Simple GUI:** One-click "run all" and launch flows; not intended for watching streamed output or deep debugging.
- **Interactive CLI:** Menu-driven experience with prompt collection and live output.
- **Developer CLI:** Direct command invocation for fine-grained control, e.g.
  ```pwsh
  dotnet run --project EngineNet -- --game_module "RemakeRegistry/Games/demo" --script_type engine --script rename-folders
  ```
  The developer CLI is evolving; expect argument shapes to change as new capabilities land.

## Quick Start for Contributors
### Prerequisites
- .NET 8 SDK
- Git
- PowerShell 7+ on Windows (documentation examples use PowerShell syntax)

### Local Build and Test
```pwsh
# Restore dependencies, build, and run tests
dotnet build RemakeEngine.sln
dotnet test RemakeEngine.sln --nologo

# Launch the interactive CLI during development
dotnet run --project EngineNet -- --tui
```

## GitHub Actions Workflows
Every pull request and push to `main` runs a trio of CI pipelines defined under `.github/workflows/`:

- `SonarQube.yml` builds on Windows, executes the test suite with coverage, and pushes the results to SonarCloud.
- `build.yml` repeats the Windows build with a runner-hosted Sonar scanner for redundancy and quicker iterations.
- `SonarQubeBuild.yml` executes the SonarSource scan action on Ubuntu so that Linux analysis settings stay honest.

Workflows must stay green before a change can merge. Match the CI locally by running `dotnet build RemakeEngine.sln` and `dotnet test RemakeEngine.sln --nologo`; add `--collect "XPlat Code Coverage"` when you need to debug coverage gaps.

### Release Workflows
- `on tagged release -- Win,Linux,Mac .NET Test, Build, Release.yml` triggers when you push a tag like `v2.5.0`. It runs Debug and Release builds/tests across Windows, macOS, and Linux, then publishes self-contained binaries for the main runtimes and uploads them to a GitHub Release.
- `on win tagged release -- Win64 .NET Build & Test.yml` triggers on `win-v*` tags for Windows-only drops. It builds/tests in Debug and Release configurations and uploads a zipped `win-x64` artifact.

If you are planning a release, coordinate the tag with maintainers so secrets (Sonar token, release token) are available.

## Contribution Workflow
1. **Discuss first (recommended):** Open an issue describing the problem, the game/module context, and the change you propose. Include logs, spec references, or manifests where relevant.
2. **Work on a feature branch:** Use `feature/<short-name>`, `fix/<short-name>`, or `chore/<short-name>` naming and follow Conventional Commits (`feat:`, `fix:`, `docs:`, etc.).
3. **Update tests and docs:**
   - Add or adjust tests under `EngineNet.Tests/` when behaviour changes.
   - Update affected module data under `RemakeRegistry/` and public docs in `RemakeEngineDocs/`.
   - Keep the relevant spec in `EngineNet/specs/` accurate.
4. **Submit a pull request:** Explain the intent, link to the driving module or issue, and include reproduction steps if fixing a bug. Ensure CI (build + tests + SonarCloud) is green.

### Pull Request Checklist
- [ ] `dotnet build RemakeEngine.sln`
- [ ] `dotnet test RemakeEngine.sln`
- [ ] Behavioural changes covered by automated tests
- [ ] Matching specification in `EngineNet/specs/` updated (see `spec.spec.md`)
- [ ] Docs, manifests, or sample data refreshed when required
- [ ] No unintentional breaking changes; include migration notes when needed

## Specifications (`EngineNet/specs`)
- Each `.spec.md` file describes the purpose, intent, goals, invariants, gaps, and TODOs for its corresponding code.
- Follow the template defined in `EngineNet/specs/spec.spec.md`.
- Keep specs concise but actionable; they are used during reviews to verify design and behaviour.

## Local Quality Expectations
- Keep changes small and focused to streamline review.
- Prefer explicit naming and targeted comments over clever implementations.
- Run build + tests locally before pushing.

## Contributor License Agreement (CLA)
By submitting code you agree to the terms outlined in [CLA.md](CLA.md). PRs from contributors who decline the CLA cannot be merged.

## Need Help?
- Open an issue with details about the module you are working on, the problem you encountered, and what you have attempted.
- For tooling ergonomics (Developer CLI, scripts, etc.), include concrete examples so we can iterate quickly.

[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=yggdrasil-au_RemakeEngine2&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=yggdrasil-au_RemakeEngine2)
[![Bugs](https://sonarcloud.io/api/project_badges/measure?project=yggdrasil-au_RemakeEngine2&metric=bugs)](https://sonarcloud.io/summary/new_code?id=yggdrasil-au_RemakeEngine2)
[![Code Smells](https://sonarcloud.io/api/project_badges/measure?project=yggdrasil-au_RemakeEngine2&metric=code_smells)](https://sonarcloud.io/summary/new_code?id=yggdrasil-au_RemakeEngine2)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=yggdrasil-au_RemakeEngine2&metric=coverage)](https://sonarcloud.io/summary/new_code?id=yggdrasil-au_RemakeEngine2)
[![Duplicated Lines](https://sonarcloud.io/api/project_badges/measure?project=yggdrasil-au_RemakeEngine2&metric=duplicated_lines_density)](https://sonarcloud.io/summary/new_code?id=yggdrasil-au_RemakeEngine2)
[![Maintainability Rating](https://sonarcloud.io/api/project_badges/measure?project=yggdrasil-au_RemakeEngine2&metric=sqale_rating)](https://sonarcloud.io/summary/new_code?id=yggdrasil-au_RemakeEngine2)
[![Reliability Rating](https://sonarcloud.io/api/project_badges/measure?project=yggdrasil-au_RemakeEngine2&metric=reliability_rating)](https://sonarcloud.io/summary/new_code?id=yggdrasil-au_RemakeEngine2)
[![Security Rating](https://sonarcloud.io/api/project_badges/measure?project=yggdrasil-au_RemakeEngine2&metric=security_rating)](https://sonarcloud.io/summary/new_code?id=yggdrasil-au_RemakeEngine2)
[![Technical Debt](https://sonarcloud.io/api/project_badges/measure?project=yggdrasil-au_RemakeEngine2&metric=sqale_index)](https://sonarcloud.io/summary/new_code?id=yggdrasil-au_RemakeEngine2)
[![Lines of Code](https://sonarcloud.io/api/project_badges/measure?project=yggdrasil-au_RemakeEngine2&metric=ncloc)](https://sonarcloud.io/summary/new_code?id=yggdrasil-au_RemakeEngine2)
[![Vulnerabilities](https://sonarcloud.io/api/project_badges/measure?project=yggdrasil-au_RemakeEngine2&metric=vulnerabilities)](https://sonarcloud.io/summary/new_code?id=yggdrasil-au_RemakeEngine2)
