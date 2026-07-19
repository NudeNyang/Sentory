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
$preservedPortableDirectories = @(
    (Join-Path $artifactsRoot "Sentory-win-x64-portable"),
    (Join-Path $artifactsRoot "Sentory-win-arm64-portable")
)
$preservedArtifactNames = @(
    "Sentory-win-x64-portable.zip",
    "Sentory-win-x64-portable.zip.sha256",
    "Sentory-win-arm64-portable.zip",
    "Sentory-win-arm64-portable.zip.sha256",
    "Sentory-win-x64-setup.exe",
    "Sentory-win-x64-setup.exe.sha256",
    "Sentory-win-arm64-setup.exe",
    "Sentory-win-arm64-setup.exe.sha256",
    "release-manifest.json"
)

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

if (Test-Path -LiteralPath $artifactsRoot) {
    foreach ($artifactDirectory in Get-ChildItem `
            -LiteralPath $artifactsRoot `
            -Directory `
            -Force) {
        $directoryPath = [System.IO.Path]::GetFullPath(
            $artifactDirectory.FullName)
        $isPreservedDirectory = $preservedPortableDirectories.Where({
            $directoryPath.Equals(
                [System.IO.Path]::GetFullPath($_),
                [System.StringComparison]::OrdinalIgnoreCase)
        }).Count -gt 0
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
        $isPreserved = $preservedArtifactNames -contains $artifactFile.Name
        if (-not $isPreserved) {
            $targets.Add((Assert-RepositoryChildPath $fullFilePath))
        }
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
Write-Host "Preserved latest x64 and ARM64 release packages."
