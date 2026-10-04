# Development

## Windows prerequisites

Install Git, the .NET SDK selected by [global.json](../global.json), Rust with the Windows MSVC target, and Visual Studio C++ x64 build tools. Managed target frameworks and package versions are declared in [Common.props](../Common.props) and the project files.

The native image tool uses [rust-toolchain.toml](../External/mua/rust-toolchain.toml). Native media builds need a Microsoft vcpkg checkout; set `VCPKG_ROOT` to it. Install LLVM and set `LIBCLANG_PATH` to its bin directory when required by the image tool's native dependencies.

Initialize the pinned dependencies:

```powershell
git submodule update --init --recursive
```

## Managed builds

```powershell
dotnet restore PenguinTools.slnx
dotnet build PenguinTools.slnx -c Release
dotnet run --project PenguinTools.CLI -- --help
```

Managed CLI output is under `PenguinTools.CLI/bin/<Configuration>/<TargetFramework>/`. The target framework comes from `Common.props`.

## Native tools and publishing

From the repository root:

```powershell
.\External\mua\scripts\build.ps1
.\External\ffmpeg\scripts\build.ps1
```

mua publishes its image executable and notices under `External/mua/target/release/mua/`. FFmpeg publishes under `External/ffmpeg/bin/`.

For the complete Windows payload:

```powershell
.\build.ps1
```

The script builds both native tools and publishes the CLI using `WinX64-NativeAOT` and `WinX64` profiles. NativeAOT is self-contained; `WinX64` requires the matching .NET runtime. CRI operations are included as a managed library in both profiles.

CLI publish output is under `PenguinTools.CLI/bin/Release/<TargetFramework>/publish/<Profile>/`. Ship the CLI together with its `assets/` directory, including the native tools and applicable license notices.

## Checks and contributions

```powershell
dotnet restore PenguinTools.slnx
./scripts/check-style.ps1
dotnet build PenguinTools.slnx -c Release -p:Platform=x64 --no-restore
dotnet test --project PenguinTools.Tests/PenguinTools.Tests.csproj -c Release --no-build
```

Follow [the project boundaries](architecture.md) and `.editorconfig`. Keep contributions focused and include reproduction steps for bug fixes. Discuss larger changes in an issue before implementing them. Contributions use the [MIT license](../LICENSE).

Tests using local sample files may skip. See [testing](testing.md) for fixture configuration and native tool requirements.

## CLI releases

The version comes from `Common.props`. Commit the intended source state, create and push its `vX.Y.Z` tag, then run:

```powershell
.\release.ps1 -Repository ChuniPingu/PenguinTools -NotesFile path/to/notes.md
```

Select the repository you are publishing to. The script defaults to `ChuniPingu/PenguinTools`, requires the remote version tag and a clean working tree, builds both publish profiles, and uploads CLI ZIP files to GitHub Releases.

Without `-NotesFile` or `-Notes`, it generates a public compare/release link. Check that the target repository is public before using that default.

`-SkipBuild` reuses publish output; `-Draft` and `-Prerelease` mark newly created releases; `-Clobber` replaces assets of an existing release. Artifacts are written under `artifacts/release/vX.Y.Z/`.

Keep credentials and private implementation details out of release notes and archives. Preserve the [third-party notices](../THIRD-PARTY-NOTICES.md).
