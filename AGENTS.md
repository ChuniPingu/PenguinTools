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
(`git submodule update --init --recursive`), and the `mua` Rust media tools should be built so
audio/jacket/stage media conversion works at runtime.

### Building `mua` (Rust media tools) on Linux

`mua_wav` links FFmpeg statically, which must come from vcpkg (do NOT link the system FFmpeg).
System build deps (`build-essential`, `libstdc++-14-dev`, `autoconf`, `automake`,
`autoconf-archive`, `libtool`, `nasm`, `yasm`, `pkg-config`), the Rust 1.97 toolchain (pinned by
`External/mua/rust-toolchain.toml`), `libclang-18`, and a bootstrapped vcpkg checkout at
`$HOME/vcpkg` with FFmpeg already installed are part of the VM snapshot. `VCPKG_ROOT`,
`VCPKGRS_TRIPLET=x64-linux`, and `LIBCLANG_PATH` are exported via `~/.bashrc`.

- Install/refresh the vcpkg FFmpeg (only needed if `$HOME/vcpkg/installed/x64-linux` is missing):
  `"$VCPKG_ROOT/vcpkg" install --x-manifest-root=External/mua --x-install-root="$VCPKG_ROOT/installed" --triplet=x64-linux`
- Build the tools (run from `External/mua`):
  `PKG_CONFIG_PATH="$VCPKG_ROOT/installed/x64-linux/lib/pkgconfig" cargo build --workspace --release`

The `PKG_CONFIG_PATH` override is required and non-obvious: without it, `ffmpeg-sys-next` resolves
the pre-installed `/usr/local/lib` FFmpeg via `pkg-config` and links that instead of the vcpkg
build (an older `loudnorm` there is missing the `stats_file` option, so `mua_wav normalize` fails).
Verify with `ldd target/release/mua_wav` — it should list no `libav*`/`libsw*` (statically linked).
Binaries land at `External/mua/target/release/{mua_wav,mua_img}`.
