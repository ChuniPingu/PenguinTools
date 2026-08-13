# AGENTS.md

## Cursor Cloud specific instructions

PenguinTools is a **.NET 10** solution: a cross-platform CLI (`PenguinTools.CLI`, the only
runnable app) plus supporting class libraries (`Core`, `Chart`, `Media`, `CRI`, `Assets`,
`Infrastructure`, `Workflow`, `Application`) and an xUnit test project (`PenguinTools.Tests`).
It converts custom CHUNITHM assets (charts, audio, jackets, stages). The README's Windows
prerequisites (Visual Studio C++ tools, LLVM, vcpkg) are only needed for the native `mua` Rust
media tools; the .NET solution itself builds and runs on Linux.

Standard commands (run from the repo root; `dotnet` is on `PATH` via `~/.bashrc`):

- Restore: `dotnet restore PenguinTools.slnx`
- Build (dev): `dotnet build PenguinTools.slnx` (Debug; do NOT use the Windows-only `build.ps1`/`release.ps1` publish scripts here)
- Test: `dotnet test PenguinTools.slnx --no-build`
- Lint/format check: `dotnet format PenguinTools.slnx --verify-no-changes`
- Run the CLI: `dotnet run --project PenguinTools.CLI -- <args>` (e.g. `-- chart convert in.sus out.c2s`, `-- info`, `-- --help`)

All three `External/` git submodules (`SonicAudioTools`, `vgaudio`, `mua`) must be initialized
(`git submodule update --init --recursive`), and the `mua` Rust media tools should be built
(see the README) so audio/jacket/stage media conversion works at runtime.
