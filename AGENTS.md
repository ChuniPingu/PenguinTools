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

Non-obvious gotchas:

- The three `External/` git submodules (`SonicAudioTools`, `vgaudio`, `mua`) MUST be initialized
  or the build fails — `PenguinTools.CRI` project-references `SonicAudioTools` and `vgaudio`. The
  update script runs `git submodule update --init --recursive`.
- Building `mua` (Rust media executables) is NOT required for `dotnet build`/`test` or for chart
  conversion. It's only needed at runtime for audio/jacket/stage media conversion, which shells
  out to the `mua*` binaries. Those media CLI subcommands will fail without the built `mua` tools.
- On Linux, one test fails and three are skipped; this is expected and unrelated to setup:
  - `TempFileNamesTests.MakeUnique_UsesFileNameOnly_WhenPathIsProvided` hardcodes a Windows path
    (`C:\...`) and only passes on Windows (`\` is not a path separator on Linux).
  - Three parser tests are `Skip`ped because their sample chart assets are not committed (they load
    from `PenguinTools.Tests/Assets`, which only ships a Fixtures README).
- `dotnet format --verify-no-changes` reports pre-existing whitespace diffs in some committed files
  and in the third-party `External/` submodules. These are not introduced by setup; do not "fix"
  submodule or unrelated files. `scripts/format-all.sh` has a stale default project list
  (`PenguinTools/PenguinTools.csproj` no longer exists) — pass explicit project paths if you use it.
- Chart conversion round-trips work with a plain-text `.sus` chart. Minimal smoke test:
  `dotnet run --project PenguinTools.CLI -- chart convert input.sus output.c2s`.
