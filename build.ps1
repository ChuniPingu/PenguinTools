$ErrorActionPreference = 'Stop'

$publishTargets = @(
    @{
        Project   = 'PenguinTools.CLI/PenguinTools.CLI.csproj'
        Profile   = 'WinX64-NativeAOT'
    },
    @{
        Project   = 'PenguinTools.CLI/PenguinTools.CLI.csproj'
        Profile   = 'WinX64-SelfContained-SingleFile'
        ZipSource = 'PenguinTools.CLI/bin/Release/net10.0/publish/WinX64-SelfContained-SingleFile'
        ZipDest   = 'PenguinTools.CLI/bin/Release/net10.0/publish/PenguinTools.CLI-win-x64.zip'
    }
)

foreach ($target in $publishTargets) {
    Write-Host "Publishing $($target.Project) [$($target.Profile)]..."
    dotnet publish $target.Project -p:PublishProfile=$($target.Profile) /p:DebugType=None /p:DebugSymbols=false

    if ($LASTEXITCODE -ne 0) {
        throw "Publish failed for '$($target.Profile)'."
    }

    if ($target.ZipDest) {
        Write-Host "Compressing $($target.Profile)..."
        if (Test-Path $target.ZipDest) { Remove-Item $target.ZipDest }
        Compress-Archive -Path "$($target.ZipSource)\*" -DestinationPath $target.ZipDest
    }
}

if (-not $env:CI) { pause }
