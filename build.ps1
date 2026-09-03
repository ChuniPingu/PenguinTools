$ErrorActionPreference = 'Stop'

$nativeBuilds = @(
    'External/mua/scripts/build.ps1',
    'External/ffmpeg/scripts/build.ps1'
)

foreach ($nativeBuild in $nativeBuilds) {
    Write-Host "Building $nativeBuild..."
    & (Join-Path $PSScriptRoot $nativeBuild)
    if ($LASTEXITCODE -ne 0) {
        throw "Native build failed: $nativeBuild"
    }
}

$publishTargets = @(
    @{
        Project = 'PenguinTools.CRI/PenguinTools.CRI.csproj'
        Profile = 'WinX64-NativeAOT'
    },
    @{
        Project = 'PenguinTools.CLI/PenguinTools.CLI.csproj'
        Profile = 'WinX64-NativeAOT'
        CriProfile = 'WinX64-NativeAOT'
    },
    @{
        Project = 'PenguinTools.CRI/PenguinTools.CRI.csproj'
        Profile = 'WinX64'
    },
    @{
        Project = 'PenguinTools.CLI/PenguinTools.CLI.csproj'
        Profile = 'WinX64'
        CriProfile = 'WinX64'
    }
)

foreach ($target in $publishTargets) {
    Write-Host "Publishing $($target.Project) [$($target.Profile)]..."
    $publishArgs = @(
        'publish', $target.Project,
        "-p:PublishProfile=$($target.Profile)",
        '/p:DebugType=None',
        '/p:DebugSymbols=false'
    )
    if ($target.CriProfile) {
        $publishArgs += "-p:PenguinToolsCriPublishProfile=$($target.CriProfile)"
    }

    & dotnet @publishArgs

    if ($LASTEXITCODE -ne 0) {
        throw "Publish failed for '$($target.Profile)'."
    }
}

if (-not $env:CI) { pause }
