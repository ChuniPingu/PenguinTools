param(
    [string]$Tag,
    [string]$Title,
    [string]$Notes,
    [string]$NotesFile,
    [string]$Repository = 'ChuniPingu/PenguinTools',
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

function Get-PreviousReleaseTag {
    param(
        [Parameter(Mandatory = $true)][string]$CurrentTag,
        [Parameter(Mandatory = $true)][string]$RepoRoot
    )

    $tags = @(git -C $RepoRoot tag --list 'v*' --sort=-version:refname)
    $foundCurrent = $false
    foreach ($candidate in $tags) {
        if ($foundCurrent) {
            return [string]$candidate
        }

        if ($candidate -eq $CurrentTag) {
            $foundCurrent = $true
        }
    }

    return $null
}

function Get-DefaultReleaseNotes {
    param(
        [Parameter(Mandatory = $true)][string]$Tag,
        [Parameter(Mandatory = $true)][string]$Repository,
        [Parameter(Mandatory = $true)][string]$RepoRoot
    )

    $previousTag = Get-PreviousReleaseTag -CurrentTag $Tag -RepoRoot $RepoRoot
    if ($previousTag) {
        return "**Full Changelog**: https://github.com/$Repository/compare/$previousTag...$Tag"
    }

    return "**Full Changelog**: https://github.com/$Repository/releases/tag/$Tag"
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

[xml]$commonProps = Get-Content (Join-Path $PSScriptRoot 'Common.props')
$projectProperties = $commonProps.Project.PropertyGroup
$currentVersionTag = "v$($projectProperties.Version)"

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

$repoRoot = (Resolve-Path $PSScriptRoot).Path

if ($NotesFile) {
    $Notes = Get-Content -LiteralPath (Resolve-Path -LiteralPath $NotesFile).Path -Raw -Encoding utf8
}
elseif (-not $Notes) {
    $Notes = Get-DefaultReleaseNotes -Tag $Tag -Repository $Repository -RepoRoot $repoRoot
}

$uiNotice = @'
图形界面请移至 [PenguinTools.UI](https://github.com/ChuniPingu/PenguinTools.UI/releases/latest)。

For the graphical interface, visit [PenguinTools.UI](https://github.com/ChuniPingu/PenguinTools.UI/releases/latest).
'@
if (!$Notes.Contains($uiNotice)) {
    $Notes = "$uiNotice`n`n$Notes"
}

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

    $releaseRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts\release'))
    $artifactRoot = [IO.Path]::GetFullPath((Join-Path $releaseRoot $Tag))
    if (!$artifactRoot.StartsWith($releaseRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Release artifact directory is outside the expected output directory.'
    }
    if (Test-Path $artifactRoot) {
        Remove-Item -LiteralPath $artifactRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $artifactRoot | Out-Null
    $releaseNotesPath = Join-Path $artifactRoot 'release-notes.md'
    [IO.File]::WriteAllText($releaseNotesPath, $Notes, [Text.UTF8Encoding]::new($false))

    $publishRoot = "PenguinTools.CLI/bin/Release/$($projectProperties.DotNetVersion)/publish"
    $assetDefinitions = @(
        @{
            Source    = "$publishRoot/WinX64"
            AssetName = "PenguinTools.CLI.$Tag.zip"
        },
        @{
            Source    = "$publishRoot/WinX64-NativeAOT"
            AssetName = "PenguinTools.CLI.$Tag.AOT.zip"
        }
    )

    $assetPaths = foreach ($asset in $assetDefinitions) {
        $destination = Join-Path $artifactRoot $asset.AssetName
        $source = Join-Path $repoRoot $asset.Source
        Write-Host "Compressing $($asset.AssetName)..."
        Compress-PublishOutput -Source $source -Destination $destination
        $destination
    }

    $releaseExists = Test-GitHubRelease -Tag $Tag -Repository $Repository

    if ($releaseExists) {
        $editArgs = @('release', 'edit', $Tag, '--repo', $Repository, '--title', $Title)
        $editArgs += @('--notes-file', $releaseNotesPath)

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

        $releaseArgs += @('--notes-file', $releaseNotesPath)

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
