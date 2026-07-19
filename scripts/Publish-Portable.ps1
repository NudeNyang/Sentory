[CmdletBinding()]
param(
    [ValidateSet("Release")]
    [string]$Configuration = "Release",
    [ValidateSet("win-x64")]
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = "artifacts"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot "src\Sentory.App\Sentory.App.csproj"
$publishDirectory = Join-Path $repositoryRoot "src\Sentory.App\bin\publish\win-x64"
$guidePath = Join-Path $repositoryRoot "distribution\README-KO.txt"

if (-not [System.IO.Path]::IsPathRooted($OutputRoot)) {
    $OutputRoot = Join-Path $repositoryRoot $OutputRoot
}

$outputRootFull = [System.IO.Path]::GetFullPath($OutputRoot)
$stagingDirectory = Join-Path $outputRootFull "Sentory-win-x64-portable"
$archivePath = Join-Path $outputRootFull "Sentory-win-x64-portable.zip"
$checksumPath = "$archivePath.sha256"
$healthDataDirectory = Join-Path $outputRootFull ".portable-check"

function Assert-OutputChildPath {
    param([Parameter(Mandatory)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $rootPrefix = $outputRootFull.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) +
        [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith(
            $rootPrefix,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "출력 폴더 밖의 경로는 정리할 수 없습니다: $fullPath"
    }

    return $fullPath
}

function Remove-OutputPathIfPresent {
    param([Parameter(Mandatory)][string]$Path)

    $verifiedPath = Assert-OutputChildPath $Path
    if (Test-Path -LiteralPath $verifiedPath) {
        Remove-Item -LiteralPath $verifiedPath -Recurse -Force
    }
}

New-Item -ItemType Directory -Path $outputRootFull -Force | Out-Null
Remove-OutputPathIfPresent $stagingDirectory
Remove-OutputPathIfPresent $archivePath
Remove-OutputPathIfPresent $checksumPath
Remove-OutputPathIfPresent $healthDataDirectory

Push-Location $repositoryRoot
try {
    dotnet publish $projectPath `
        --configuration $Configuration `
        --runtime $Runtime `
        /p:PublishProfile=win-x64
    if ($LASTEXITCODE -ne 0) {
        throw "Sentory 휴대용 빌드에 실패했습니다."
    }

    $publishedExecutable = Join-Path $publishDirectory "Sentory.App.exe"
    if (-not (Test-Path -LiteralPath $publishedExecutable)) {
        throw "배포 실행 파일을 찾지 못했습니다: $publishedExecutable"
    }

    New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null
    $stagedExecutable = Join-Path $stagingDirectory "Sentory.App.exe"
    $stagedGuide = Join-Path $stagingDirectory "README-KO.txt"
    Copy-Item -LiteralPath $publishedExecutable -Destination $stagedExecutable
    Copy-Item -LiteralPath $guidePath -Destination $stagedGuide
    if (-not (Test-Path -LiteralPath $stagedGuide)) {
        throw "휴대용 사용 방법 파일을 복사하지 못했습니다."
    }

    $previousDataDirectory = $env:SENTORY_DATA_DIR
    try {
        $env:SENTORY_DATA_DIR = $healthDataDirectory
        $process = Start-Process `
            -FilePath $stagedExecutable `
            -ArgumentList "--verify-installation" `
            -WindowStyle Hidden `
            -Wait `
            -PassThru
        if ($process.ExitCode -ne 0) {
            throw "배포 실행 파일 자체 점검에 실패했습니다. 종료 코드: $($process.ExitCode)"
        }
    }
    finally {
        $env:SENTORY_DATA_DIR = $previousDataDirectory
        Remove-OutputPathIfPresent $healthDataDirectory
    }

    $publishedPdb = Get-ChildItem -LiteralPath $stagingDirectory `
        -Filter "*.pdb" `
        -File
    if ($publishedPdb) {
        throw "휴대용 폴더에 디버그 심볼이 포함되어 있습니다."
    }

    Compress-Archive `
        -Path (Join-Path $stagingDirectory "*") `
        -DestinationPath $archivePath `
        -CompressionLevel Optimal
    $hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    "$hash  $(Split-Path -Leaf $archivePath)" |
        Set-Content -LiteralPath $checksumPath -Encoding ascii

    Write-Host ""
    Write-Host "Sentory 휴대용 배포 파일을 만들었습니다." -ForegroundColor Green
    Write-Host "폴더: $stagingDirectory"
    Write-Host "압축: $archivePath"
    Write-Host "확인값: $checksumPath"
}
finally {
    Pop-Location
}
