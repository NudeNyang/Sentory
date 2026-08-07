[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version = "2.0.6",
    [string]$OutputRoot = "artifacts",
    [ValidateSet("x64", "arm64")]
    [string]$Architecture = "x64",
    [switch]$SkipSourceArchive,
    [switch]$SkipManifest
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Split-Path -Parent $PSScriptRoot))
$tauriRoot = Join-Path $repositoryRoot "src\Sentory.Tauri"
$tauriConfigPath = Join-Path $tauriRoot "src-tauri\tauri.conf.json"
$cargoManifestPath = Join-Path $tauriRoot "src-tauri\Cargo.toml"
$packageManifestPath = Join-Path $tauriRoot "package.json"
$webAppPath = Join-Path $tauriRoot "web\app.js"
$webHtmlPath = Join-Path $tauriRoot "web\index.html"
$engineRuntimePath = Join-Path `
    $repositoryRoot `
    "src\Sentory.Engine.Bridge\EngineRuntimeHost.cs"
$targetTriple = if ($Architecture -eq "arm64") {
    "aarch64-pc-windows-msvc"
}
else {
    "x86_64-pc-windows-msvc"
}
$targetDirectory = Join-Path `
    $tauriRoot `
    "src-tauri\target\$targetTriple\release"
$installerScript = Join-Path $repositoryRoot "installer\Sentory.iss"
$licensePath = Join-Path $repositoryRoot "LICENSE.txt"
$iconPath = Join-Path $repositoryRoot "src\Sentory.Tauri\src-tauri\icons\Sentory.ico"

if (-not [System.IO.Path]::IsPathRooted($OutputRoot)) {
    $OutputRoot = Join-Path $repositoryRoot $OutputRoot
}
$outputRootFull = [System.IO.Path]::GetFullPath($OutputRoot)
$stagingDirectory = Join-Path `
    $outputRootFull `
    "Sentory-win-$Architecture-portable"
$portableArchive = Join-Path `
    $outputRootFull `
    "Sentory-win-$Architecture-portable.zip"

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

$dirtyFiles = git -C $repositoryRoot status --porcelain
if ($LASTEXITCODE -ne 0) {
    throw "Git 작업 트리 상태를 확인하지 못했습니다."
}
if ($dirtyFiles) {
    throw "소스와 실행 파일이 정확히 일치하도록 변경 사항을 먼저 커밋해 주세요."
}

$escapedVersion = [regex]::Escape($Version)
$tauriConfig = Get-Content -Raw -LiteralPath $tauriConfigPath
$cargoManifest = Get-Content -Raw -LiteralPath $cargoManifestPath
$packageManifest = Get-Content -Raw -LiteralPath $packageManifestPath
$engineRuntime = Get-Content -Raw -LiteralPath $engineRuntimePath
$publicIdentity = @(
    $tauriConfig,
    $cargoManifest,
    $packageManifest,
    $engineRuntime,
    (Get-Content -Raw -LiteralPath $webAppPath),
    (Get-Content -Raw -LiteralPath $webHtmlPath)) -join "`n"
if ($tauriConfig -notmatch '"productName"\s*:\s*"Sentory"' -or
    $tauriConfig -notmatch '"identifier"\s*:\s*"com\.nudenyang\.sentory"' -or
    $tauriConfig -notmatch ('"version"\s*:\s*"' + $escapedVersion + '"') -or
    $cargoManifest -notmatch ('version\s*=\s*"' + $escapedVersion + '"') -or
    $packageManifest -notmatch ('"version"\s*:\s*"' + $escapedVersion + '"') -or
    $engineRuntime -notmatch (
        'CurrentVersion\s*=\s*"' + $escapedVersion + '"')) {
    throw "Tauri 제품 정보와 배포 버전이 일치하지 않습니다: $Version"
}
if ($publicIdentity -match 'for Developers|Tauri Preview|com\.sentory\.preview') {
    throw "공개 배포 파일에 개발자판 표시가 남아 있습니다."
}

& (Join-Path $PSScriptRoot "Build-Tauri.ps1") `
    -Configuration Release `
    -Architecture $Architecture
if ($LASTEXITCODE -ne 0) {
    throw "Tauri Release 빌드에 실패했습니다."
}

New-Item -ItemType Directory -Path $outputRootFull -Force | Out-Null
Remove-OutputPathIfPresent $stagingDirectory
Remove-OutputPathIfPresent $portableArchive
Remove-OutputPathIfPresent "$portableArchive.sha256"
New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null

$builtExecutable = Join-Path $targetDirectory "sentory-tauri.exe"
$builtEngine = Join-Path $targetDirectory "sentory-engine.exe"
foreach ($requiredBinary in @($builtExecutable, $builtEngine)) {
    if (-not (Test-Path -LiteralPath $requiredBinary)) {
        throw "Tauri 배포 파일을 찾지 못했습니다: $requiredBinary"
    }
}

$stagedExecutable = Join-Path $stagingDirectory "Sentory.exe"
Copy-Item -LiteralPath $builtExecutable -Destination $stagedExecutable
Copy-Item -LiteralPath $builtEngine -Destination `
    (Join-Path $stagingDirectory "sentory-engine.exe")
Copy-Item -LiteralPath $licensePath -Destination `
    (Join-Path $stagingDirectory "LICENSE.txt")
Copy-Item -LiteralPath (Join-Path $repositoryRoot "distribution\README-KO.txt") `
    -Destination (Join-Path $stagingDirectory "README-KO.txt")
Copy-Item -LiteralPath (Join-Path $repositoryRoot "docs\privacy.md") `
    -Destination (Join-Path $stagingDirectory "PRIVACY.md")
Copy-Item -LiteralPath (Join-Path $repositoryRoot "distribution\THIRD-PARTY-NOTICES.txt") `
    -Destination (Join-Path $stagingDirectory "THIRD-PARTY-NOTICES.txt")
Copy-Item -LiteralPath (Join-Path $repositoryRoot "docs\model-provenance.md") `
    -Destination (Join-Path $stagingDirectory "MODEL-PROVENANCE.md")
Copy-Item -LiteralPath (Join-Path $repositoryRoot "distribution\licenses") `
    -Destination (Join-Path $stagingDirectory "licenses") `
    -Recurse

$versionInfo = (Get-Item -LiteralPath $stagedExecutable).VersionInfo
if ($versionInfo.ProductVersion -notmatch ("^" + $escapedVersion) -or
    $versionInfo.ProductName -ne "Sentory" -or
    $versionInfo.FileDescription -match 'Preview|Developer') {
    throw "정식 Tauri 실행 파일의 제품 정보가 올바르지 않습니다."
}

function Assert-PeArchitecture {
    param([Parameter(Mandatory)][string]$Path)

    $expectedMachine = if ($Architecture -eq "arm64") { 0xAA64 } else { 0x8664 }
    $stream = [System.IO.File]::OpenRead($Path)
    $reader = [System.IO.BinaryReader]::new($stream)
    try {
        $stream.Position = 0x3C
        $peOffset = $reader.ReadInt32()
        $stream.Position = $peOffset + 4
        $actualMachine = $reader.ReadUInt16()
        if ($actualMachine -ne $expectedMachine) {
            throw ("배포 실행 파일 아키텍처가 올바르지 않습니다. " +
                "파일: {0}, 예상: 0x{1:X4}, 실제: 0x{2:X4}" -f `
                $Path, $expectedMachine, $actualMachine)
        }
    }
    finally {
        $reader.Dispose()
        $stream.Dispose()
    }
}

Assert-PeArchitecture $stagedExecutable
Assert-PeArchitecture (Join-Path $stagingDirectory "sentory-engine.exe")

$hostArchitecture =
    [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
$canRunVerification =
    ($Architecture -eq "x64" -and $hostArchitecture -eq "X64") -or
    ($Architecture -eq "arm64" -and $hostArchitecture -eq "Arm64")
if ($canRunVerification) {
    $verification = Start-Process `
        -FilePath $stagedExecutable `
        -ArgumentList "--verify-installation" `
        -WindowStyle Hidden `
        -Wait `
        -PassThru
    if ($verification.ExitCode -ne 0) {
        throw "Tauri 배포 실행 파일 자체 점검에 실패했습니다. 종료 코드: $($verification.ExitCode)"
    }
}
else {
    Write-Host "$Architecture 실행 점검은 같은 아키텍처의 Windows에서 수행해야 합니다."
}

Compress-Archive `
    -Path (Join-Path $stagingDirectory "*") `
    -DestinationPath $portableArchive `
    -CompressionLevel Optimal

$isccCandidates = @(
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"))
$isccPath = $isccCandidates |
    Where-Object { Test-Path -LiteralPath $_ } |
    Select-Object -First 1
if (-not $isccPath) {
    throw "Inno Setup 6 컴파일러가 필요합니다. winget install JRSoftware.InnoSetup"
}
$numericParts = $Version.Split('-', 2)[0].Split('.')
$numericVersion = "{0}.{1}.{2}.0" -f `
    $numericParts[0], $numericParts[1], $numericParts[2]
& $isccPath `
    "/DMyVersion=$Version" `
    "/DMyNumericVersion=$numericVersion" `
    "/DMyArch=$Architecture" `
    "/DSourceDir=$stagingDirectory" `
    "/DOutputDir=$outputRootFull" `
    "/DLicenseFile=$licensePath" `
    "/DIconFile=$iconPath" `
    $installerScript
if ($LASTEXITCODE -ne 0) {
    throw "Tauri $Architecture 설치형 배포 생성에 실패했습니다."
}

$sourceArchive = Join-Path $outputRootFull "Sentory-$Version-source.zip"
if (-not $SkipSourceArchive) {
    Remove-OutputPathIfPresent $sourceArchive
    Remove-OutputPathIfPresent "$sourceArchive.sha256"
    git -C $repositoryRoot archive `
        --format=zip `
        "--prefix=Sentory-$Version-source/" `
        "--output=$sourceArchive" `
        HEAD
    if ($LASTEXITCODE -ne 0) {
        throw "현재 커밋의 소스 배포 ZIP을 만들지 못했습니다."
    }
}

$assetPaths = @(
    $portableArchive,
    (Join-Path $outputRootFull "Sentory-win-$Architecture-setup.exe"))
if (-not $SkipSourceArchive) {
    $assetPaths += $sourceArchive
}
$assets = foreach ($assetPath in $assetPaths) {
    if (-not (Test-Path -LiteralPath $assetPath)) {
        throw "배포 파일을 찾지 못했습니다: $assetPath"
    }
    $hash = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash
    "$hash  $(Split-Path -Leaf $assetPath)" |
        Set-Content -LiteralPath "$assetPath.sha256" -Encoding ascii
    $file = Get-Item -LiteralPath $assetPath
    [ordered]@{
        name = $file.Name
        size = $file.Length
        sha256 = $hash.ToLowerInvariant()
    }
}

if (-not $SkipManifest) {
    $manifestAssetFiles = Get-ChildItem -LiteralPath $outputRootFull -File |
        Where-Object {
            $_.Name -match '^Sentory-win-(x64|arm64)-(portable\.zip|setup\.exe)$' -or
            $_.Name -eq "Sentory-$Version-source.zip"
        } |
        Sort-Object Name
    $manifestAssets = foreach ($file in $manifestAssetFiles) {
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        [ordered]@{
            name = $file.Name
            size = $file.Length
            sha256 = $hash.ToLowerInvariant()
        }
    }
    $manifest = [ordered]@{
        product = "Sentory"
        version = $Version
        channel = if ($Version.Contains('-')) { "beta" } else { "stable" }
        runtime = "Tauri 2"
        publishedAt = [DateTimeOffset]::UtcNow.ToString("O")
        assets = @($manifestAssets)
    }
    $manifestPath = Join-Path $outputRootFull "release-manifest.json"
    $manifest | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath $manifestPath -Encoding utf8
}

Write-Host ""
Write-Host "Sentory $Version Tauri $Architecture 배포 파일을 만들었습니다." -ForegroundColor Green
Write-Host "출력 폴더: $outputRootFull"
Get-ChildItem -LiteralPath $outputRootFull -File |
    Where-Object {
        $_.Name -match "^Sentory-win-$Architecture-(portable|setup)" -or
        $_.Name -match '^Sentory-[0-9].*-source\.zip(?:\.sha256)?$' -or
        $_.Name -eq 'release-manifest.json'
    } |
    Sort-Object Name |
    ForEach-Object { Write-Host "- $($_.Name)" }
