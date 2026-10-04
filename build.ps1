$ErrorActionPreference = 'Stop'

$publishTargets = @(
    @{
        Project = 'PenguinTools.CLI/PenguinTools.CLI.csproj'
        Profile = 'WinX64-NativeAOT'
    },
    @{
        Project = 'PenguinTools.CLI/PenguinTools.CLI.csproj'
        Profile = 'WinX64'
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

    & dotnet @publishArgs

    if ($LASTEXITCODE -ne 0) {
        throw "Publish failed for '$($target.Profile)'."
    }
}

if (-not $env:CI) {
    pause
}
