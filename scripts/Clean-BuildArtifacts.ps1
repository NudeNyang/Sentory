[CmdletBinding(SupportsShouldProcess)]
param()

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Split-Path -Parent $PSScriptRoot))
$repositoryPrefix = $repositoryRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) +
    [System.IO.Path]::DirectorySeparatorChar
$artifactsRoot = Join-Path $repositoryRoot "artifacts"
$preservedBackupDirectory = Join-Path $artifactsRoot "developer-wpf-1.5.1"

function Assert-RepositoryChildPath {
    param([Parameter(Mandatory)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith(
            $repositoryPrefix,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Cannot clean a path outside the Sentory repository: $fullPath"
    }

    return $fullPath
}

function Get-PathBytes {
    param([Parameter(Mandatory)][string]$Path)

    if (Test-Path -LiteralPath $Path -PathType Leaf) {
        return [int64](Get-Item -LiteralPath $Path).Length
    }

    $sum = (Get-ChildItem `
            -LiteralPath $Path `
            -File `
            -Recurse `
            -Force `
            -ErrorAction SilentlyContinue |
        Measure-Object Length -Sum).Sum
    if ($null -eq $sum) {
        return [int64]0
    }

    return [int64]$sum
}

$targets = [System.Collections.Generic.List[string]]::new()
foreach ($sourceRootName in @("src", "tests")) {
    $sourceRoot = Join-Path $repositoryRoot $sourceRootName
    foreach ($projectDirectory in Get-ChildItem `
            -LiteralPath $sourceRoot `
            -Directory) {
        foreach ($generatedName in @("bin", "obj", "artifacts")) {
            $generatedPath = Join-Path `
                $projectDirectory.FullName `
                $generatedName
            if (Test-Path -LiteralPath $generatedPath) {
                $targets.Add((Assert-RepositoryChildPath $generatedPath))
            }
        }
    }
}

$temporaryDirectory = Join-Path $repositoryRoot "tmp"
if (Test-Path -LiteralPath $temporaryDirectory) {
    $targets.Add((Assert-RepositoryChildPath $temporaryDirectory))
}

$tauriGeneratedTargets = @(
    (Join-Path $repositoryRoot "src\Sentory.Tauri\src-tauri\target"),
    (Join-Path $repositoryRoot "src\Sentory.Tauri\src-tauri\binaries")
)
foreach ($tauriTarget in $tauriGeneratedTargets) {
    if (Test-Path -LiteralPath $tauriTarget) {
        $targets.Add((Assert-RepositoryChildPath $tauriTarget))
    }
}

if (Test-Path -LiteralPath $artifactsRoot) {
    foreach ($artifactDirectory in Get-ChildItem `
            -LiteralPath $artifactsRoot `
            -Directory `
            -Force) {
        $directoryPath = [System.IO.Path]::GetFullPath(
            $artifactDirectory.FullName)
        $isPreservedDirectory = $directoryPath.Equals(
            [System.IO.Path]::GetFullPath($preservedBackupDirectory),
            [System.StringComparison]::OrdinalIgnoreCase)
        if (-not $isPreservedDirectory) {
            $targets.Add((Assert-RepositoryChildPath `
                $artifactDirectory.FullName))
        }
    }

    foreach ($artifactFile in Get-ChildItem `
            -LiteralPath $artifactsRoot `
            -File `
            -Force) {
        $fullFilePath = [System.IO.Path]::GetFullPath(
            $artifactFile.FullName)
        $targets.Add((Assert-RepositoryChildPath $fullFilePath))
    }
}

$verifiedTargets = @($targets | Sort-Object -Unique)
$reclaimedBytes = [int64]0
foreach ($target in $verifiedTargets) {
    $reclaimedBytes += Get-PathBytes $target
    if ($PSCmdlet.ShouldProcess($target, "Remove generated build output")) {
        Remove-Item -LiteralPath $target -Recurse -Force
    }
}

Write-Host ""
Write-Host "Sentory build cleanup completed." -ForegroundColor Green
Write-Host "Removed paths: $($verifiedTargets.Count)"
Write-Host ("Reclaimed space: {0:N1} MB" -f ($reclaimedBytes / 1MB))
Write-Host "Preserved executable: $(Join-Path $repositoryRoot 'Sentory.exe')"
Write-Host "Preserved engine: $(Join-Path $repositoryRoot 'sentory-engine.exe')"
Write-Host "Preserved latest backup: $preservedBackupDirectory"
