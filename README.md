# PenguinTools

Tools for converting CHUNITHM charts, music, jackets, and stages.

## Prerequisites

For Windows builds: Git, the .NET SDK, Visual Studio C++ build tools, and a vcpkg checkout for FFmpeg.

Use the versions selected by [global.json](global.json), [Common.props](Common.props), and native dependency configuration. See [development setup](docs/development.md) for environment variables and publish profiles.

## Quick start

```powershell
git clone --recurse-submodules https://github.com/ChuniPingu/PenguinTools.git
cd PenguinTools
dotnet restore PenguinTools.slnx
dotnet build PenguinTools.slnx -c Release
dotnet run --project PenguinTools.CLI -- --help
```

Builds prepare the pinned image tools automatically; a verified tool cache works offline. Audio setup is covered in the development guide.

## Common commands

| Command                                               | Purpose                                             |
| ----------------------------------------------------- | --------------------------------------------------- |
| `git submodule update --init --recursive`             | Initialize dependencies after an incomplete clone   |
| `dotnet build PenguinTools.slnx -c Release`           | Build managed projects                              |
| `dotnet run --project PenguinTools.Tests`             | Run tests                                           |
| `dotnet format PenguinTools.slnx --verify-no-changes` | Check C# formatting                                 |
| `.\build.ps1`                                         | Build native tools and publish Windows CLI profiles |

## Documentation

- [Development and releases](docs/development.md)
- [Project architecture](docs/architecture.md)
- [Testing and local fixtures](docs/testing.md)
- [Chart formats](docs/formats/README.md)
- [Third-party notices](THIRD-PARTY-NOTICES.md)

Contributions use the [MIT license](LICENSE). See the development guide for contribution details.

## Disclaimer

This project is created solely for study and self-evaluation purposes.

"CHUNITHM" is a trademark of SEGA Corporation. ® SEGA. All rights reserved.

"UMIGURI" and "Margrete" are software by inonote. © inonote.
