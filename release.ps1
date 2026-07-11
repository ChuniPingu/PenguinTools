param(
    [string]$Tag,
    [string]$Title,
    [string]$Notes,
    [string]$NotesFile,
    [string]$Repository = 'Foahh/PenguinTools',
    [switch]$Draft,
    [switch]$Prerelease,
    [switch]$SkipBuild,
    [switch]$AllowDirty,
    [switch]$Clobber
)

$ErrorActionPreference = 'Stop'

function Assert-Command {
    param([Parameter(Mandatory = $true)][string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found on PATH."
    }
}

function Get-ProjectVersion {
    [xml]$commonProps = Get-Content (Join-Path $PSScriptRoot 'Common.props')
    return $commonProps.Project.PropertyGroup.Version
}

function Get-DefaultReleaseNotes {
    return '**Full Changelog**: https://github.com/Foahh/PenguinTools/compare/v1.10.4...v1.11.0'
}

function Compress-PublishOutput {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    if (-not (Test-Path $Source -PathType Container)) {
        throw "Publish output '$Source' does not exist. Run release.ps1 without -SkipBuild first."
    }

    if (Test-Path $Destination) {
        Remove-Item $Destination
    }

    Compress-Archive -Path (Join-Path $Source '*') -DestinationPath $Destination
}

function Copy-ReleaseAsset {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    if (-not (Test-Path $Source -PathType Leaf)) {
        throw "Publish asset '$Source' does not exist. Run release.ps1 without -SkipBuild first."
    }

    Copy-Item -Path $Source -Destination $Destination -Force
}

function Test-GitHubRelease {
    param(
        [Parameter(Mandatory = $true)][string]$Tag,
        [Parameter(Mandatory = $true)][string]$Repository
    )

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        gh release view $Tag --repo $Repository *> $null
        if ($LASTEXITCODE -eq 0) {
            return $true
        }

        if ($LASTEXITCODE -eq 1) {
            return $false
        }

        throw "Failed to check GitHub release '$Tag'. gh exited with code $LASTEXITCODE."
    }
    finally {
        $script:ErrorActionPreference = $previousErrorActionPreference
    }
}

function Test-GitHubTag {
    param(
        [Parameter(Mandatory = $true)][string]$Tag,
        [Parameter(Mandatory = $true)][string]$Repository
    )

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        gh api "repos/$Repository/git/ref/tags/$Tag" *> $null
        if ($LASTEXITCODE -eq 0) {
            return $true
        }

        if ($LASTEXITCODE -eq 1) {
            return $false
        }

        throw "Failed to check GitHub tag '$Tag'. gh exited with code $LASTEXITCODE."
    }
    finally {
        $script:ErrorActionPreference = $previousErrorActionPreference
    }
}

Assert-Command 'git'
Assert-Command 'gh'
if (-not $SkipBuild) {
    Assert-Command 'dotnet'
}

$currentVersionTag = "v$(Get-ProjectVersion)"

if (-not $Tag) {
    $Tag = $currentVersionTag
}
elseif ($Tag -ne $currentVersionTag) {
    throw "Release tag '$Tag' does not match current project version tag '$currentVersionTag'."
}

if (-not $Title) {
    $Title = $Tag
}

if ($NotesFile -and $Notes) {
    throw 'Use either -Notes or -NotesFile, not both.'
}

if (-not $NotesFile -and -not $Notes) {
    $Notes = Get-DefaultReleaseNotes
}

$repoRoot = (Resolve-Path $PSScriptRoot).Path
Push-Location $repoRoot
try {
    if (-not $AllowDirty) {
        $status = git status --porcelain
        if ($status) {
            throw 'Working tree is dirty. Commit or stash changes, or pass -AllowDirty.'
        }
    }

    if (-not (Test-GitHubTag -Tag $Tag -Repository $Repository)) {
        throw "GitHub tag '$Tag' does not exist in '$Repository'. Create and push the current version tag before running this script."
    }

    if (-not $SkipBuild) {
        $env:CI = 'true'
        & (Join-Path $repoRoot 'build.ps1')
        if ($LASTEXITCODE -ne 0) {
            throw 'Build failed.'
        }
    }

    $artifactRoot = Join-Path $repoRoot "artifacts\release\$Tag"
    if (Test-Path $artifactRoot) {
        Remove-Item $artifactRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $artifactRoot | Out-Null

    $assetDefinitions = @(
        @{
            SourceFile = 'PenguinTools.CLI/bin/Release/net10.0/publish/WinX64-NativeAOT/PenguinTools.CLI.exe'
            AssetName  = "PenguinTools.CLI.$Tag.exe"
        },
        @{
            Source = 'PenguinTools.CLI/bin/Release/net10.0/publish/WinX64-SelfContained-SingleFile'
            AssetName = "PenguinTools.CLI.$Tag.external-assets.zip"
        }
    )

    $assetPaths = foreach ($asset in $assetDefinitions) {
        $destination = Join-Path $artifactRoot $asset.AssetName

        if ($asset.SourceFile) {
            $source = Join-Path $repoRoot $asset.SourceFile
            Write-Host "Copying $($asset.AssetName)..."
            Copy-ReleaseAsset -Source $source -Destination $destination
        }
        else {
            $source = Join-Path $repoRoot $asset.Source
            Write-Host "Compressing $($asset.AssetName)..."
            Compress-PublishOutput -Source $source -Destination $destination
        }

        $destination
    }

    $releaseExists = Test-GitHubRelease -Tag $Tag -Repository $Repository

    if ($releaseExists) {
        $editArgs = @('release', 'edit', $Tag, '--repo', $Repository, '--title', $Title)
        if ($NotesFile) {
            $editArgs += @('--notes-file', $NotesFile)
        }
        else {
            $editArgs += @('--notes', $Notes)
        }

        Write-Host "Updating GitHub release $Tag..."
        gh @editArgs
        if ($LASTEXITCODE -ne 0) {
            throw "Release update failed for '$Tag'."
        }

        $uploadArgs = @('release', 'upload', $Tag)
        $uploadArgs += $assetPaths
        $uploadArgs += @('--repo', $Repository)
        if ($Clobber) {
            $uploadArgs += '--clobber'
        }

        Write-Host "Uploading assets to existing release $Tag..."
        gh @uploadArgs
        if ($LASTEXITCODE -ne 0) {
            throw "Asset upload failed for release '$Tag'."
        }
    }
    else {
        $releaseArgs = @('release', 'create', $Tag)
        $releaseArgs += $assetPaths
        $releaseArgs += @('--repo', $Repository, '--title', $Title, '--verify-tag')

        if ($NotesFile) {
            $releaseArgs += @('--notes-file', $NotesFile)
        }
        else {
            $releaseArgs += @('--notes', $Notes)
        }

        if ($Draft) {
            $releaseArgs += '--draft'
        }

        if ($Prerelease) {
            $releaseArgs += '--prerelease'
        }

        Write-Host "Creating GitHub release $Tag..."
        gh @releaseArgs
        if ($LASTEXITCODE -ne 0) {
            throw "Release creation failed for '$Tag'."
        }
    }

    Write-Host "Release assets are in $artifactRoot"
}
finally {
    Pop-Location
}
