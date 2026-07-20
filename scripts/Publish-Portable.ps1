[CmdletBinding()]
param(
    [ValidateSet("Release")]
    [string]$Configuration = "Release",
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = "artifacts"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot "src\Sentory.App\Sentory.App.csproj"
$publishDirectory = Join-Path $repositoryRoot "src\Sentory.App\bin\publish\$Runtime"
$guidePath = Join-Path $repositoryRoot "distribution\README-KO.txt"
$licensePath = Join-Path $repositoryRoot "LICENSE.txt"
$copyingPath = Join-Path $repositoryRoot "COPYING"
$privacyPath = Join-Path $repositoryRoot "PRIVACY.md"
$noticesPath = Join-Path $repositoryRoot "THIRD-PARTY-NOTICES.txt"
$thirdPartyLicensesPath = Join-Path $repositoryRoot "distribution\licenses"
$repositoryExecutable = Join-Path $repositoryRoot "Sentory.exe"

if (-not [System.IO.Path]::IsPathRooted($OutputRoot)) {
    $OutputRoot = Join-Path $repositoryRoot $OutputRoot
}

$outputRootFull = [System.IO.Path]::GetFullPath($OutputRoot)
$stagingDirectory = Join-Path $outputRootFull "Sentory-$Runtime-portable"
$archivePath = Join-Path $outputRootFull "Sentory-$Runtime-portable.zip"
$checksumPath = "$archivePath.sha256"
$healthDataDirectory = Join-Path $outputRootFull ".portable-check-$Runtime"

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
        /p:PublishProfile=$Runtime
    if ($LASTEXITCODE -ne 0) {
        throw "Sentory 휴대용 빌드에 실패했습니다."
    }

    $publishedExecutable = Join-Path $publishDirectory "Sentory.exe"
    if (-not (Test-Path -LiteralPath $publishedExecutable)) {
        throw "배포 실행 파일을 찾지 못했습니다: $publishedExecutable"
    }

    New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null
    $stagedExecutable = Join-Path $stagingDirectory "Sentory.exe"
    $stagedGuide = Join-Path $stagingDirectory "README-KO.txt"
    $stagedLicense = Join-Path $stagingDirectory "LICENSE.txt"
    $stagedCopying = Join-Path $stagingDirectory "COPYING"
    $stagedPrivacy = Join-Path $stagingDirectory "PRIVACY.md"
    $stagedNotices = Join-Path $stagingDirectory "THIRD-PARTY-NOTICES.txt"
    $stagedThirdPartyLicenses = Join-Path $stagingDirectory "licenses"
    Copy-Item -LiteralPath $publishedExecutable -Destination $stagedExecutable
    Copy-Item -LiteralPath $guidePath -Destination $stagedGuide
    Copy-Item -LiteralPath $licensePath -Destination $stagedLicense
    Copy-Item -LiteralPath $copyingPath -Destination $stagedCopying
    Copy-Item -LiteralPath $privacyPath -Destination $stagedPrivacy
    Copy-Item -LiteralPath $noticesPath -Destination $stagedNotices
    Copy-Item `
        -LiteralPath $thirdPartyLicensesPath `
        -Destination $stagedThirdPartyLicenses `
        -Recurse
    foreach ($requiredDocument in @(
            $stagedGuide,
            $stagedLicense,
            $stagedCopying,
            $stagedPrivacy,
            $stagedNotices,
            (Join-Path $stagedThirdPartyLicenses "Apache-2.0.txt"))) {
        if (-not (Test-Path -LiteralPath $requiredDocument)) {
            throw "휴대용 필수 문서를 복사하지 못했습니다: $requiredDocument"
        }
    }

    $expectedMachine = if ($Runtime -eq "win-arm64") { 0xAA64 } else { 0x8664 }
    $stream = [System.IO.File]::OpenRead($stagedExecutable)
    $reader = [System.IO.BinaryReader]::new($stream)
    try {
        $stream.Position = 0x3C
        $peOffset = $reader.ReadInt32()
        $stream.Position = $peOffset + 4
        $actualMachine = $reader.ReadUInt16()
        if ($actualMachine -ne $expectedMachine) {
            throw ("배포 실행 파일 아키텍처가 올바르지 않습니다. " +
                "예상: 0x{0:X4}, 실제: 0x{1:X4}" -f `
                $expectedMachine, $actualMachine)
        }
    }
    finally {
        $reader.Dispose()
        $stream.Dispose()
    }

    $hostArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
    $canRunHealthCheck =
        ($Runtime -eq "win-x64" -and $hostArchitecture -eq "X64") -or
        ($Runtime -eq "win-arm64" -and $hostArchitecture -eq "Arm64")
    if ($canRunHealthCheck) {
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
    }
    else {
        Write-Host "$Runtime 실행 점검은 동일 아키텍처 장치에서 수행해야 합니다."
    }

    $publishedPdb = Get-ChildItem -LiteralPath $stagingDirectory `
        -Filter "*.pdb" `
        -File
    if ($publishedPdb) {
        throw "휴대용 폴더에 디버그 심볼이 포함되어 있습니다."
    }

    if ($Runtime -eq "win-x64") {
        try {
            Copy-Item `
                -LiteralPath $stagedExecutable `
                -Destination $repositoryExecutable `
                -Force
        }
        catch [System.IO.IOException] {
            Write-Warning (
                "루트 Sentory.exe가 실행 중이어서 교체하지 못했습니다. " +
                "배포 패키지 생성은 계속합니다.")
        }
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
    Write-Host "바로 실행: $repositoryExecutable"
    Write-Host "폴더: $stagingDirectory"
    Write-Host "압축: $archivePath"
    Write-Host "확인값: $checksumPath"
}
finally {
    Pop-Location
}
