[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version = "1.0.1",
    [string]$OutputRoot = "artifacts"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Split-Path -Parent $PSScriptRoot))
$portableScript = Join-Path $PSScriptRoot "Publish-Portable.ps1"
$installerScript = Join-Path $repositoryRoot "installer\Sentory.iss"
$projectPath = Join-Path `
    $repositoryRoot `
    "src\Sentory.App\Sentory.App.csproj"
$licensePath = Join-Path $repositoryRoot "LICENSE.txt"
$iconPath = Join-Path $repositoryRoot "src\Sentory.App\Assets\Sentory.ico"

if (-not [System.IO.Path]::IsPathRooted($OutputRoot)) {
    $OutputRoot = Join-Path $repositoryRoot $OutputRoot
}

$outputRootFull = [System.IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path $outputRootFull -Force | Out-Null

$gitDirectory = Join-Path $repositoryRoot ".git"
if (-not (Test-Path -LiteralPath $gitDirectory)) {
    throw "소스 배포 ZIP을 만들려면 Git 저장소에서 실행해야 합니다."
}
$dirtyFiles = git -C $repositoryRoot status --porcelain
if ($LASTEXITCODE -ne 0) {
    throw "Git 작업 트리 상태를 확인하지 못했습니다."
}
if ($dirtyFiles) {
    throw "소스와 실행 파일이 정확히 일치하도록 변경 사항을 먼저 커밋해 주세요."
}

$projectText = Get-Content -Raw -LiteralPath $projectPath
$escapedVersion = [regex]::Escape($Version)
if ($projectText -notmatch "<Version>$escapedVersion</Version>") {
    throw "프로젝트 버전과 배포 버전이 일치하지 않습니다: $Version"
}

foreach ($runtime in @("win-x64", "win-arm64")) {
    & $portableScript `
        -Configuration Release `
        -Runtime $runtime `
        -OutputRoot $outputRootFull
    if ($LASTEXITCODE -ne 0) {
        throw "$runtime 휴대용 배포 생성에 실패했습니다."
    }
}

$isccCandidates = @(
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
)
$isccPath = $isccCandidates |
    Where-Object { Test-Path -LiteralPath $_ } |
    Select-Object -First 1
if (-not $isccPath) {
    throw "Inno Setup 6 컴파일러가 필요합니다. winget install JRSoftware.InnoSetup"
}

$numericParts = $Version.Split('-', 2)[0].Split('.')
$numericVersion = "{0}.{1}.{2}.0" -f `
    $numericParts[0], $numericParts[1], $numericParts[2]

foreach ($architecture in @("x64", "arm64")) {
    $runtime = "win-$architecture"
    $sourceDirectory = Join-Path `
        $outputRootFull `
        "Sentory-$runtime-portable"
    & $isccPath `
        "/DMyVersion=$Version" `
        "/DMyNumericVersion=$numericVersion" `
        "/DMyArch=$architecture" `
        "/DSourceDir=$sourceDirectory" `
        "/DOutputDir=$outputRootFull" `
        "/DLicenseFile=$licensePath" `
        "/DIconFile=$iconPath" `
        $installerScript
    if ($LASTEXITCODE -ne 0) {
        throw "$architecture 설치형 배포 생성에 실패했습니다."
    }
}

$sourceArchivePath = Join-Path $outputRootFull "Sentory-$Version-source.zip"
if (Test-Path -LiteralPath $sourceArchivePath) {
    Remove-Item -LiteralPath $sourceArchivePath -Force
}
git -C $repositoryRoot archive `
    --format=zip `
    "--prefix=Sentory-$Version-source/" `
    "--output=$sourceArchivePath" `
    HEAD
if ($LASTEXITCODE -ne 0) {
    throw "현재 커밋의 소스 배포 ZIP을 만들지 못했습니다."
}

$assetPaths = @(
    (Join-Path $outputRootFull "Sentory-win-x64-portable.zip"),
    (Join-Path $outputRootFull "Sentory-win-arm64-portable.zip"),
    (Join-Path $outputRootFull "Sentory-win-x64-setup.exe"),
    (Join-Path $outputRootFull "Sentory-win-arm64-setup.exe"),
    $sourceArchivePath
)
$assets = foreach ($assetPath in $assetPaths) {
    if (-not (Test-Path -LiteralPath $assetPath)) {
        throw "배포 파일을 찾지 못했습니다: $assetPath"
    }

    $hash = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash
    $checksumPath = "$assetPath.sha256"
    "$hash  $(Split-Path -Leaf $assetPath)" |
        Set-Content -LiteralPath $checksumPath -Encoding ascii
    $file = Get-Item -LiteralPath $assetPath
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
    publishedAt = [DateTimeOffset]::UtcNow.ToString("O")
    assets = @($assets)
}
$manifestPath = Join-Path $outputRootFull "release-manifest.json"
$manifest | ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath $manifestPath -Encoding utf8

Write-Host ""
Write-Host "Sentory $Version 전체 배포 파일을 만들었습니다." -ForegroundColor Green
Write-Host "출력 폴더: $outputRootFull"
Get-ChildItem -LiteralPath $outputRootFull -File |
    Where-Object {
        $_.Name -match 'Sentory-win-(x64|arm64)-(portable|setup)' -or
        $_.Name -match '^Sentory-[0-9].*-source\.zip(?:\.sha256)?$' -or
        $_.Name -eq 'release-manifest.json'
    } |
    Sort-Object Name |
    ForEach-Object { Write-Host "- $($_.Name)" }
