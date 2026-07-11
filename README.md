# PenguinTools

An all-in-one toolbox for converting custom assets for **CHUNITHM** (charts, music, jackets, stages, etc.).

## Usage

### CLI

```bash
PenguinTools.CLI chart inspect song.mgxc
PenguinTools.CLI chart convert song.mgxc song.c2s
PenguinTools.CLI chart convert song.c2s song.ugc
PenguinTools.CLI audio extract music.acb ./audio --paired-input music.awb
PenguinTools.CLI music extract ./music0001/Music.xml ./ugc-music
PenguinTools.CLI option scan ./charts
PenguinTools.CLI option build ./charts ./output
PenguinTools.CLI music build song.mgxc ./output
```

`option build` automatically loads `<input>/options.json` when present. Use `--config <path>` to select another
file or `--no-config --option-name AXXX` to build from defaults and command-line overrides.

## Contributing

Issues and pull requests are welcome.

### Prerequisites

- Git with submodule support
- .NET SDK matching [`global.json`](global.json)
- The CLI (`PenguinTools.CLI`) targets plain `net10.0` and builds on Windows, Linux, and macOS

### Getting the code

Clone with submodules (the solution references vendored libraries under `External/`):

```bash
git clone --recurse-submodules https://github.com/Foahh/PenguinTools.git
cd PenguinTools
```

If you already cloned without them:

```bash
git submodule update --init --recursive
```

### Build

#### 1. `mua`

Build the Rust media tools from the [`mua`](External/mua) submodule:

```powershell
cd External/mua
.\scripts\build.ps1
```

This produces a publish folder at `External/mua/target/release/mua/` containing the three executables and legal notices.

Prerequisites: Rust 1.97 (via `rust-toolchain.toml`), Visual Studio 2022 C++ tools, LLVM/libclang, and `VCPKG_ROOT`
pointing at a Microsoft vcpkg checkout.

To refresh the workspace FFmpeg overlay port from the pinned vcpkg baseline:

```powershell
cd External/mua
.\scripts\refresh-ffmpeg-port.ps1
```

#### 2. PenguinTools

```bash
dotnet restore PenguinTools.slnx
dotnet build PenguinTools.slnx -c Release
```

Output lands in `PenguinTools.CLI/bin/<Configuration>/net10.0/` for the CLI.

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
