# PenguinTools

An all-in-one toolbox for converting custom assets for **CHUNITHM** (charts, music, jackets, stages, etc.).

## Building

### 0. Prerequisites

- Git
- .NET 10 SDK
- Rust 1.97
- Visual Studio 2022 C++ tools
- vcpkg

### 1. Getting the code

Clone with submodules:

```bash
git clone --recurse-submodules https://github.com/ChuniPingu/PenguinTools.git
cd PenguinTools
```

If you already cloned without them:

```bash
git submodule update --init --recursive
```

### 2. Native media tools

Build the Rust image tool from the [`mua`](External/mua) submodule:

```powershell
cd External/mua
.\scripts\build.ps1
```

Then build the minimal standalone FFmpeg executable used for audio conversion:

```powershell
cd ../ffmpeg
.\scripts\build.ps1
```

### 3. PenguinTools

```bash
dotnet restore PenguinTools.slnx
dotnet build PenguinTools.slnx -c Release
```

Output lands in `PenguinTools.CLI/bin/<Configuration>/net10.0/` for the CLI.

## Contributing

### Before opening a PR

Keep changes small and focused. Match the existing style — there's an [`.editorconfig`](.editorconfig) for formatting and naming. If you're fixing a bug, mention how to reproduce it. For anything larger, open an issue first so we can agree on scope before you put in the work.

### Licensing

Contributions are under the same license as the project ([MIT](LICENSE)).

## Disclaimer

This project is created solely for study and self-evaluation purposes.
It does not condone the piracy, operation, modification, or reverse engineering of CHUNITHM arcade games, or any
Sega-licensed games, as these actions may violate Japanese and international laws.
Is also does not condone the modification, redistribution, repurposing, or reverse engineering of UMIGURI and Margrete.

"CHUNITHM" is a trademark of SEGA Corporation. ® SEGA. All rights reserved.

"UMIGURI" and "Margrete" are software by inonote. © inonote.
