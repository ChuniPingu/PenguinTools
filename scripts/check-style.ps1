[CmdletBinding()]
param([switch]$Fix)

$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    $formatArgs = @('format', 'PenguinTools.slnx', '--no-restore', '--severity', 'warn', '--exclude', 'External', '--verbosity', 'minimal')
    if (-not $Fix) {
        $formatArgs += '--verify-no-changes'
    }
    & dotnet @formatArgs
    if ($LASTEXITCODE -ne 0) {
        throw 'C# formatting/style check failed. Run ./scripts/check-style.ps1 -Fix after restoring the solution.'
    }
}
finally {
    Pop-Location
}
