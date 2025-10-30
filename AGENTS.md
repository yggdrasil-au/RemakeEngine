# AGENTS.md

## Setup commands
- Install dotnet sdk 9.0.306: https://dotnet.microsoft.com/en-us/download/dotnet/9.0
- Build: `dotnet build RemakeEngine.sln -c Debug`
- Run tests: `dotnet test EngineNet.Tests\EngineNet.Tests.csproj -c Debug --no-build --logger "trx;LogFileName=test_results.trx"`

## Use Exact Code style
read [Style.md](Style.md)

## Use Spec files
This project uses spec files to document design and behavior. See [spec.spec.md](spec.spec.md) for guidelines on writing and maintaining spec files.

## CONTRIBUTING
See [CONTRIBUTING.md](CONTRIBUTING.md) for contribution guidelines and workflows.

## main Readme
See [README.md](README.md) for an overview of the Remake Engine project.

## submodules
sub modules are created manually, as the modules should usually be downloaded by the engine itself, but for development purposes, you can write this file.
.gitmodules

This is the main game module used to develop the engine, it is also a real project for creating a remake of The Simpsons Game 2007 from PS3.
```
[submodule "EngineApps/Games/TheSimpsonsGame-PS3"]
    path = EngineApps/Games/TheSimpsonsGame-PS3
    url = https://github.com/Superposition28/TheSimpsonsGame-PS3.git
```
This is the documentation repository for the Remake Engine.
```
[submodule "RemakeEngineDocs"]
    path = RemakeEngineDocs
    url = https://github.com/yggdrasil-au/RemakeEngineDocs.git
```

