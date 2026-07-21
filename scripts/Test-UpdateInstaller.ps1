[CmdletBinding()]
param(
    [string]$ArtifactsRoot = "artifacts"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Split-Path -Parent $PSScriptRoot))
if (-not [System.IO.Path]::IsPathRooted($ArtifactsRoot)) {
    $ArtifactsRoot = Join-Path $repositoryRoot $ArtifactsRoot
}

$artifactsRootFull = [System.IO.Path]::GetFullPath($ArtifactsRoot)
$sourceDirectory = Join-Path $artifactsRootFull "Sentory-win-x64-portable"
$testInstaller = Join-Path $artifactsRootFull "Sentory-update-test.exe"
$testDirectory = Join-Path $artifactsRootFull ".update-installer-check"
$healthDirectory = Join-Path $artifactsRootFull ".update-installer-health"
$logPath = Join-Path $artifactsRootFull "update-installer-check.log"
$installerScript = Join-Path $repositoryRoot "installer\Sentory.iss"
$licensePath = Join-Path $repositoryRoot "LICENSE.txt"
$iconPath = Join-Path $repositoryRoot "src\Sentory.App\Assets\Sentory.ico"
$projectPath = Join-Path $repositoryRoot "src\Sentory.App\Sentory.App.csproj"
$projectText = Get-Content -Raw -LiteralPath $projectPath
$version = [regex]::Match(
    $projectText,
    '<Version>([^<]+)</Version>').Groups[1].Value
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "프로젝트 버전을 확인하지 못했습니다."
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
    throw "Inno Setup 6 컴파일러가 필요합니다."
}
if (-not (Test-Path -LiteralPath $sourceDirectory)) {
    throw "x64 휴대용 배포 폴더를 먼저 만들어 주세요: $sourceDirectory"
}

$numericParts = $version.Split('-', 2)[0].Split('.')
$numericVersion = "{0}.{1}.{2}.0" -f `
    $numericParts[0], $numericParts[1], $numericParts[2]
$testAppId = "{{0FEA6707-89C6-4D6C-B3D3-A83F0508598B}"
$previousDataDirectory = $env:SENTORY_DATA_DIR

foreach ($path in @($testInstaller, $testDirectory, $healthDirectory, $logPath)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}

try {
    & $isccPath `
        "/DMyVersion=$version" `
        "/DMyNumericVersion=$numericVersion" `
        "/DMyArch=x64" `
        "/DMyAppId=$testAppId" `
        "/DMyOutputBaseFilename=Sentory-update-test" `
        "/DSourceDir=$sourceDirectory" `
        "/DOutputDir=$artifactsRootFull" `
        "/DLicenseFile=$licensePath" `
        "/DIconFile=$iconPath" `
        $installerScript
    if ($LASTEXITCODE -ne 0) {
        throw "업데이트 검증용 설치 파일을 만들지 못했습니다."
    }

    $env:SENTORY_DATA_DIR = $healthDirectory
    $install = Start-Process `
        -FilePath $testInstaller `
        -ArgumentList @(
            "/VERYSILENT",
            "/SUPPRESSMSGBOXES",
            "/CLOSEAPPLICATIONS",
            "/NORESTART",
            "/NOICONS",
            "/SENTORYUPDATE=1",
            "/SENTORYTEST=1",
            "/DIR=$testDirectory",
            "/LOG=$logPath") `
        -WindowStyle Hidden `
        -Wait `
        -PassThru
    if ($install.ExitCode -ne 0) {
        throw "업데이트 설치 검증 종료 코드: $($install.ExitCode)"
    }
    if (-not (Test-Path -LiteralPath (Join-Path $healthDirectory "sentory.db"))) {
        throw "업데이트 뒤 Sentory 자체 점검이 실행되지 않았습니다."
    }

    Write-Host "무인 업데이트 → Sentory 재실행 자체 점검을 통과했습니다." `
        -ForegroundColor Green
}
finally {
    $env:SENTORY_DATA_DIR = $previousDataDirectory
    $uninstaller = Join-Path $testDirectory "unins000.exe"
    if (Test-Path -LiteralPath $uninstaller) {
        Start-Process `
            -FilePath $uninstaller `
            -ArgumentList @(
                "/VERYSILENT",
                "/SUPPRESSMSGBOXES",
                "/NORESTART") `
            -WindowStyle Hidden `
            -Wait | Out-Null
    }

    foreach ($path in @($testInstaller, $testDirectory, $healthDirectory, $logPath)) {
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Recurse -Force
        }
    }
}
