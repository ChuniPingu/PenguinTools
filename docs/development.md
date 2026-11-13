# Development

## Windows prerequisites

Install Git, the .NET SDK selected by [global.json](../global.json), and Visual Studio C++ x64 build tools. Managed target frameworks and package versions are declared in [Common.props](../Common.props) and the project files.

Windows x64 builds and CLI binaries require a CPU that supports the [x86-64-v2 instruction set](https://learn.microsoft.com/en-us/dotnet/core/compatibility/jit/11/minimum-hardware-requirements).

Builds automatically prepare the pinned FFmpeg, texconv, and image-tools binaries from [native-tools.json](../PenguinTools.Assets/native-tools.json), verifying hashes and reusing `artifacts/tools` offline.

Initialize the pinned dependencies:

```powershell
git submodule update --init --recursive
```

## Managed builds

Managed builds, style checks, tests, and Sonar analysis need only the two managed codec dependencies:

```powershell
git submodule update --init External/SonicAudioTools External/vgaudio
```

CI initializes these explicitly. Native tools are needed for native media integration and complete release payloads.

```powershell
dotnet restore PenguinTools.slnx
dotnet build PenguinTools.slnx -c Release
dotnet run --project PenguinTools.CLI -- --help
```

Managed CLI output is under `PenguinTools.CLI/bin/<Configuration>/<TargetFramework>/`. The target framework comes from `Common.props`.

## Native tools and publishing

For the complete Windows payload:

```powershell
.\build.ps1
```

The script publishes the CLI using `WinX64-NativeAOT` and `WinX64` profiles. NativeAOT is self-contained; `WinX64` requires the matching .NET runtime.

CLI publish output is under `PenguinTools.CLI/bin/Release/<TargetFramework>/publish/<Profile>/`. Ship the complete folder, including `assets/` and its tool binaries, native libraries, and license notices. Runtime needs no network.

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

The version comes from `Common.props`. Write user-facing notes in `docs/changelog/X.Y.Z.md`, including command migrations when needed. Commit the intended source state, create and push its `vX.Y.Z` tag, then run:

```powershell
.\release.ps1 -Repository ChuniPingu/PenguinTools -NotesFile docs/changelog/X.Y.Z.md
```

Select the repository you are publishing to. The script defaults to `ChuniPingu/PenguinTools`, requires the remote version tag and a clean working tree, builds both publish profiles, and uploads CLI ZIP files to GitHub Releases.

Every release includes a Chinese and English link to the latest PenguinTools.UI release. `-NotesFile` or `-Notes` supplies the CLI notes below that link; without either, the script generates a compare/release link.

`-SkipBuild` reuses publish output; `-Draft` and `-Prerelease` mark newly created releases; `-Clobber` replaces assets of an existing release. Artifacts are written under `artifacts/release/vX.Y.Z/`.

[wallbind](https://github.com/ChuniPingu/wallbind) has its own release and version. Link its installation guide when describing wall exports; CLI archives contain the exporter, while the game plugin is downloaded separately.

Keep credentials and private implementation details out of release notes and archives. Preserve the [third-party notices](../THIRD-PARTY-NOTICES.md).
