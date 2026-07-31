[CmdletBinding()]
param(
    [string]$SourceImage = "src\Sentory.Tauri\src-tauri\icons\Sentory.png",
    [string]$OutputDirectory = "installer\msix\Assets"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Split-Path -Parent $PSScriptRoot))

function Resolve-RepositoryPath {
    param([Parameter(Mandatory)][string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }
    return [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $Path))
}

$sourcePath = Resolve-RepositoryPath $SourceImage
$outputPath = Resolve-RepositoryPath $OutputDirectory
if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
    throw "MSIX 아이콘 원본을 찾지 못했습니다: $sourcePath"
}

Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$source = [System.Drawing.Image]::FromFile($sourcePath)
try {
    $assets = [ordered]@{
        "StoreLogo.png" = 50
        "Square44x44Logo.png" = 44
        "Square150x150Logo.png" = 150
    }
    $targetSizes = @(16, 20, 24, 30, 32, 36, 40, 44, 48, 60, 64, 72, 80, 96, 256)
    foreach ($targetSize in $targetSizes) {
        $assets["Square44x44Logo.targetsize-$($targetSize)_altform-unplated.png"] =
            $targetSize
        $assets["Square44x44Logo.targetsize-$($targetSize)_altform-lightunplated.png"] =
            $targetSize
    }
    foreach ($asset in $assets.GetEnumerator()) {
        $bitmap = [System.Drawing.Bitmap]::new(
            $asset.Value,
            $asset.Value,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $bitmap.SetResolution(96, 96)
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([System.Drawing.Color]::Transparent)
                $graphics.CompositingMode =
                    [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                $graphics.CompositingQuality =
                    [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                $graphics.InterpolationMode =
                    [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.PixelOffsetMode =
                    [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.SmoothingMode =
                    [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $graphics.DrawImage(
                    $source,
                    [System.Drawing.Rectangle]::new(
                        0,
                        0,
                        $asset.Value,
                        $asset.Value))
            }
            finally {
                $graphics.Dispose()
            }
            $destination = Join-Path $outputPath $asset.Key
            $bitmap.Save(
                $destination,
                [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            $bitmap.Dispose()
        }
    }
}
finally {
    $source.Dispose()
}

Write-Host "MSIX 시각 자산을 만들었습니다: $outputPath"
