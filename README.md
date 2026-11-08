# PenguinTools

Tools for converting CHUNITHM charts, music, jackets, and stages.

图形界面请移至 [PenguinTools.UI](https://github.com/ChuniPingu/PenguinTools.UI/releases/latest)。

For the graphical interface, visit [PenguinTools.UI](https://github.com/ChuniPingu/PenguinTools.UI/releases/latest).

## Prerequisites

For Windows builds: Git, the .NET SDK, and Visual Studio C++ build tools.

Use the versions selected by [global.json](global.json), [Common.props](Common.props), and native dependency configuration. See [development setup](docs/development.md) for prerequisites and publish profiles.

## Quick start

```powershell
git clone --recurse-submodules https://github.com/ChuniPingu/PenguinTools.git
cd PenguinTools
dotnet restore PenguinTools.slnx
dotnet build PenguinTools.slnx -c Release
dotnet run --project PenguinTools.CLI -- --help
```

Builds prepare pinned native tools automatically; a verified tool cache works offline.

## Common commands

| Command                                               | Purpose                                          |
| ----------------------------------------------------- | ------------------------------------------------ |
| `git submodule update --init --recursive`              | Initialize dependencies after an incomplete clone |
| `dotnet build PenguinTools.slnx -c Release`             | Build managed projects                           |
| `dotnet run --project PenguinTools.Tests -c Release`   | Run tests                                        |
| `.\scripts\check-style.ps1`                            | Check C# formatting and style after restore       |
| `.\build.ps1`                                         | Publish Windows CLI profiles                     |

## Documentation

- [Fan charting workflow](docs/charting-workflow.md)
- [CLI usage, configuration, and output](docs/cli.md)
- [Development and releases](docs/development.md)
- [Project architecture](docs/architecture.md)
- [Testing and local fixtures](docs/testing.md)
- [Chart conversion and compatibility](docs/chart-conversion.md)
- [Chart formats](docs/formats/README.md)
- [Third-party notices](THIRD-PARTY-NOTICES.md)

Contributions use the [MIT license](LICENSE). See the development guide for contribution details.

## Disclaimer

This project is created solely for study and self-evaluation purposes.

"CHUNITHM" is a trademark of SEGA Corporation. ® SEGA. All rights reserved.

"UMIGURI" and "Margrete" are software by inonote. © inonote.
