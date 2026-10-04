param(
    [string]$Manifest = (Join-Path $PSScriptRoot '../PenguinTools.Assets/native-tools.json'),
    [string]$CacheDirectory = (Join-Path $PSScriptRoot '../artifacts/tools')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Get-Sha256([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    try { Get-StreamHash $stream } finally { $stream.Dispose() }
}
function Get-StreamHash([IO.Stream]$Stream) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { [BitConverter]::ToString($algorithm.ComputeHash($Stream)).Replace('-', '').ToLowerInvariant() }
    finally { $algorithm.Dispose() }
}
function Get-ChildPath([string]$Parent, [string]$Relative) {
    if ([string]::IsNullOrWhiteSpace($Relative)) { throw 'Native-tool path is empty.' }
    $parts = $Relative.Replace('\', '/').Split('/')
    if ([string]::IsNullOrWhiteSpace($Relative) -or [IO.Path]::IsPathRooted($Relative) -or $Relative.Contains(':') -or
        @($parts | Where-Object { $_ -in @('', '.', '..') -or $_ -ne $_.TrimEnd(' ', '.') }).Count -gt 0) {
        throw "Unsafe native-tool archive path: $Relative"
    }
    $full = [IO.Path]::GetFullPath((Join-Path $Parent $Relative))
    $prefix = [IO.Path]::GetFullPath($Parent).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Archive path escapes its destination: $Relative" }
    return $full
}
function Remove-StagingDirectory([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($cachePrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing cleanup outside the native-tool cache.' }
    if (Test-Path -LiteralPath $full) { Remove-Item -LiteralPath $full -Recurse -Force }
}
function Replace-File([string]$Source, [string]$Destination) {
    if ([IO.File]::Exists($Destination)) { [IO.File]::Replace($Source, $Destination, $null) }
    else { [IO.File]::Move($Source, $Destination) }
}
function Test-RequiredFiles($Tool, [string]$Directory) {
    foreach ($required in $Tool.requiredFiles) {
        if (-not [IO.File]::Exists((Get-ChildPath $Directory $required))) { return $false }
    }
    return $true
}
function Read-Archive([string]$Path, [string]$Destination, [bool]$Extract) {
    $zip = [IO.Compression.ZipFile]::OpenRead($Path)
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $matches = $true
    try {
        foreach ($entry in $zip.Entries) {
            $name = $entry.FullName.Replace('\', '/')
            if ($name.EndsWith('/')) { $null = Get-ChildPath $Destination $name.TrimEnd('/'); continue }
            $path = Get-ChildPath $Destination $name
            if (-not $names.Add($name) -or (($entry.ExternalAttributes -shr 16) -band 0xf000) -eq 0xa000) {
                throw "Duplicate path or symbolic link in native-tool archive: $name"
            }
            $inputStream = $entry.Open()
            try {
                if ($Extract) {
                    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
                    $outputStream = [IO.File]::Open($path, 'CreateNew', 'Write', 'None')
                    try { $inputStream.CopyTo($outputStream) } finally { $outputStream.Dispose() }
                }
                elseif (-not [IO.File]::Exists($path) -or (Get-Sha256 $path) -ne (Get-StreamHash $inputStream)) { $matches = $false }
            }
            finally { $inputStream.Dispose() }
        }
        if ($names.Count -eq 0) { throw 'Native-tool archive is empty.' }
        if (-not $Extract -and (Test-Path -LiteralPath $Destination) -and
            @(Get-ChildItem -LiteralPath $Destination -Recurse -File -Force).Count -ne $names.Count) { $matches = $false }
        return $matches
    }
    finally { $zip.Dispose() }
}
function Prepare-Tool($Tool) {
    if ($Tool.id -notmatch '^[a-z0-9-]+$' -or $Tool.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or
        $Tool.url -notmatch '^https://' -or [string]::IsNullOrWhiteSpace($Tool.release) -or $Tool.type -notin @('file', 'zip')) {
        throw 'Native-tool manifest requires an id, explicit release, HTTPS URL, SHA-256 and file/zip type.'
    }
    $destination = Join-Path $cachePath $Tool.id
    $nonce = [Guid]::NewGuid().ToString('N')
    $stage = Join-Path $cachePath ('.' + $Tool.id + '-' + $nonce + '.stage')
    $backup = Join-Path $cachePath ('.' + $Tool.id + '-' + $nonce + '.backup')
    $download = Join-Path $cachePath ($nonce + '.download')
    try {
        if ($Tool.type -eq 'file') {
            $installed = Get-ChildPath $destination $Tool.fileName
            if ([IO.File]::Exists($installed) -and (Get-Sha256 $installed) -eq $Tool.sha256 -and (Test-RequiredFiles $Tool $destination)) { return }
            Write-Host "Downloading $($Tool.id) $($Tool.release)..."
            Invoke-WebRequest -Uri $Tool.url -OutFile $download -UseBasicParsing -TimeoutSec 120
            $actual = Get-Sha256 $download
            if ($actual -ne $Tool.sha256) { throw "SHA-256 mismatch: expected $($Tool.sha256), received $actual." }
            $stagedFile = Get-ChildPath $stage $Tool.fileName
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($stagedFile)) | Out-Null
            [IO.File]::Move($download, $stagedFile)
        }
        else {
            $downloads = Join-Path $cachePath 'downloads'
            [IO.Directory]::CreateDirectory($downloads) | Out-Null
            $archive = Join-Path $downloads ($Tool.id + '-' + $Tool.sha256 + '.zip')
            if (-not [IO.File]::Exists($archive) -or (Get-Sha256 $archive) -ne $Tool.sha256) {
                Write-Host "Downloading $($Tool.id) $($Tool.release)..."
                Invoke-WebRequest -Uri $Tool.url -OutFile $download -UseBasicParsing -TimeoutSec 120
                $actual = Get-Sha256 $download
                if ($actual -ne $Tool.sha256) { throw "SHA-256 mismatch: expected $($Tool.sha256), received $actual." }
                Replace-File $download $archive
            }
            if ((Read-Archive $archive $destination $false) -and (Test-RequiredFiles $Tool $destination)) { return }
            [IO.Directory]::CreateDirectory($stage) | Out-Null
            $null = Read-Archive $archive $stage $true
        }
        foreach ($required in $Tool.requiredFiles) {
            if (-not [IO.File]::Exists((Get-ChildPath $stage $required))) { throw "Required native-tool file is missing: $required" }
        }
        if ([IO.Directory]::Exists($destination)) { [IO.Directory]::Move($destination, $backup) }
        try { [IO.Directory]::Move($stage, $destination) }
        catch {
            if ([IO.Directory]::Exists($backup)) { [IO.Directory]::Move($backup, $destination) }
            throw
        }
        Remove-StagingDirectory $backup
    }
    catch { throw "Unable to prepare pinned $($Tool.id) $($Tool.release): $($_.Exception.Message)" }
    finally {
        if ([IO.File]::Exists($download)) { [IO.File]::Delete($download) }
        Remove-StagingDirectory $stage
    }
}
$definition = Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json
if ($definition.schemaVersion -ne 1 -or @($definition.tools).Count -eq 0 -or
    @($definition.tools.id | Select-Object -Unique).Count -ne @($definition.tools).Count) {
    throw 'Unsupported native-tool manifest schema or duplicate tool ids.'
}
$cachePath = [IO.Path]::GetFullPath($CacheDirectory)
$cachePrefix = $cachePath.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
[IO.Directory]::CreateDirectory($cachePath) | Out-Null
$deadline = [DateTime]::UtcNow.AddMinutes(5)
$lockStream = $null
while ($null -eq $lockStream) {
    try { $lockStream = [IO.File]::Open((Join-Path $cachePath '.prepare.lock'), 'OpenOrCreate', 'ReadWrite', 'None') }
    catch [IO.IOException] {
        if ([DateTime]::UtcNow -ge $deadline) { throw 'Timed out waiting for native-tool preparation lock.' }
        Start-Sleep -Milliseconds 200
    }
}
try { foreach ($tool in $definition.tools) { Prepare-Tool $tool } }
finally { $lockStream.Dispose() }
