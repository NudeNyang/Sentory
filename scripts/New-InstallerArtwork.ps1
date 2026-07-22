[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Split-Path -Parent $PSScriptRoot))
$logoPath = Join-Path $repositoryRoot "src\Sentory.App\Assets\Sentory.png"
$assetsDirectory = Join-Path $repositoryRoot "installer\Assets"
$wizardImagePath = Join-Path $assetsDirectory "SentoryWizard.bmp"
$smallImagePath = Join-Path $assetsDirectory "SentoryWizardSmall.bmp"

Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Path $assetsDirectory -Force | Out-Null

function New-SentoryBitmap {
    param(
        [Parameter(Mandatory)][int]$Width,
        [Parameter(Mandatory)][int]$Height,
        [Parameter(Mandatory)][string]$OutputPath,
        [Parameter(Mandatory)][bool]$IncludeWordmark,
        [int]$Scale = 4
    )

    $bitmap = [System.Drawing.Bitmap]::new($Width * $Scale, $Height * $Scale)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $logo = [System.Drawing.Image]::FromFile($logoPath)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.InterpolationMode =
            [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode =
            [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.Clear([System.Drawing.ColorTranslator]::FromHtml("#DED8CF"))

        if ($IncludeWordmark) {
            $scaledWidth = $Width * $Scale
            $scaledHeight = $Height * $Scale
            $logoSize = 66 * $Scale
            $logoLeft = [int](($scaledWidth - $logoSize) / 2)
            $graphics.DrawImage(
                $logo,
                $logoLeft,
                30 * $Scale,
                $logoSize,
                $logoSize)

            $titleFont = [System.Drawing.Font]::new(
                "Georgia",
                19 * $Scale,
                [System.Drawing.FontStyle]::Bold,
                [System.Drawing.GraphicsUnit]::Pixel)
            $captionFont = [System.Drawing.Font]::new(
                "Malgun Gothic",
                9 * $Scale,
                [System.Drawing.FontStyle]::Regular,
                [System.Drawing.GraphicsUnit]::Pixel)
            $textBrush = [System.Drawing.SolidBrush]::new(
                [System.Drawing.ColorTranslator]::FromHtml("#292722"))
            $mutedBrush = [System.Drawing.SolidBrush]::new(
                [System.Drawing.ColorTranslator]::FromHtml("#6D6861"))
            $linePen = [System.Drawing.Pen]::new(
                [System.Drawing.ColorTranslator]::FromHtml("#CEC7BC"))
            try {
                $title = "Sentory"
                $titleSize = $graphics.MeasureString($title, $titleFont)
                $graphics.DrawString(
                    $title,
                    $titleFont,
                    $textBrush,
                    [single](($scaledWidth - $titleSize.Width) / 2),
                    [single](108 * $Scale))
                $linePen.Width = $Scale
                $graphics.DrawLine(
                    $linePen,
                    24 * $Scale,
                    148 * $Scale,
                    ($Width - 24) * $Scale,
                    148 * $Scale)
                $graphics.DrawString(
                    "사진과 링크를 한 곳에서",
                    $captionFont,
                    $mutedBrush,
                    [System.Drawing.RectangleF]::new(
                        20 * $Scale,
                        164 * $Scale,
                        ($Width - 40) * $Scale,
                        34 * $Scale))
                $graphics.DrawString(
                    "1.4.2",
                    $captionFont,
                    $mutedBrush,
                    [single](24 * $Scale),
                    [single](($Height - 32) * $Scale))
            }
            finally {
                $titleFont.Dispose()
                $captionFont.Dispose()
                $textBrush.Dispose()
                $mutedBrush.Dispose()
                $linePen.Dispose()
            }
        }
        else {
            $padding = 7 * $Scale
            $graphics.DrawImage(
                $logo,
                $padding,
                $padding,
                ($Width * $Scale) - ($padding * 2),
                ($Height * $Scale) - ($padding * 2))
        }

        $bitmap.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Bmp)
    }
    finally {
        $logo.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

New-SentoryBitmap `
    -Width 164 `
    -Height 314 `
    -OutputPath $wizardImagePath `
    -IncludeWordmark $true
New-SentoryBitmap `
    -Width 55 `
    -Height 55 `
    -OutputPath $smallImagePath `
    -IncludeWordmark $false

Write-Host "Sentory 설치 화면 이미지를 만들었습니다."
Write-Host "- $wizardImagePath"
Write-Host "- $smallImagePath"
