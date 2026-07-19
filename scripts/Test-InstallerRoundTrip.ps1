[CmdletBinding()]
param(
    [ValidateSet("x64")]
    [string]$Architecture = "x64",
    [string]$ArtifactsRoot = "artifacts"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Split-Path -Parent $PSScriptRoot))
if (-not [System.IO.Path]::IsPathRooted($ArtifactsRoot)) {
    $ArtifactsRoot = Join-Path $repositoryRoot $ArtifactsRoot
}

$artifactsRootFull = [System.IO.Path]::GetFullPath($ArtifactsRoot)
$artifactsPrefix = $artifactsRootFull.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) +
    [System.IO.Path]::DirectorySeparatorChar
$testDirectory = [System.IO.Path]::GetFullPath(
    (Join-Path $artifactsRootFull ".installer-check-$Architecture"))
$installLogPath = [System.IO.Path]::GetFullPath(
    (Join-Path $artifactsRootFull "installer-check-$Architecture.log"))
if (-not $testDirectory.StartsWith(
        $artifactsPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "설치 점검 경로가 artifacts 폴더 밖입니다: $testDirectory"
}

function Remove-TestDirectoryIfPresent {
    if (Test-Path -LiteralPath $testDirectory) {
        $verifiedPath = [System.IO.Path]::GetFullPath($testDirectory)
        if (-not $verifiedPath.StartsWith(
                $artifactsPrefix,
                [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "검증되지 않은 경로는 삭제할 수 없습니다: $verifiedPath"
        }

        Remove-Item -LiteralPath $verifiedPath -Recurse -Force
    }
}

$uninstallRoots = @(
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall",
    "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall",
    "HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
)
foreach ($root in $uninstallRoots) {
    if (-not (Test-Path -LiteralPath $root)) {
        continue
    }

    foreach ($key in Get-ChildItem -LiteralPath $root) {
        $item = Get-ItemProperty -LiteralPath $key.PSPath -ErrorAction SilentlyContinue
        if ($item.DisplayName -like "Sentory*" -and
            -not [string]::IsNullOrWhiteSpace($item.InstallLocation)) {
            throw "기존 Sentory 설치가 있어 왕복 검증을 중단합니다: $($item.InstallLocation)"
        }
    }
}

$installerPath = Join-Path `
    $artifactsRootFull `
    "Sentory-win-$Architecture-setup.exe"
if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "설치 파일을 찾지 못했습니다: $installerPath"
}

$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$existingRunValue = (Get-ItemProperty `
        -Path $runKey `
        -Name Sentory `
        -ErrorAction SilentlyContinue).Sentory
$hadRunValue = $null -ne $existingRunValue
$previousDataDirectory = $env:SENTORY_DATA_DIR
$installationSucceeded = $false

Remove-TestDirectoryIfPresent
try {
    $install = Start-Process `
        -FilePath $installerPath `
        -ArgumentList @(
            "/VERYSILENT",
            "/SUPPRESSMSGBOXES",
            "/NORESTART",
            "/NOICONS",
            "/DIR=$testDirectory",
            "/LOG=$installLogPath") `
        -WindowStyle Hidden `
        -Wait `
        -PassThru
    if ($install.ExitCode -ne 0) {
        throw (
            "설치 프로그램 종료 코드: $($install.ExitCode). " +
            "로그: $installLogPath")
    }
    $installationSucceeded = $true

    $installedExecutable = Join-Path $testDirectory "Sentory.exe"
    foreach ($requiredFile in @(
            $installedExecutable,
            (Join-Path $testDirectory "LICENSE.txt"),
            (Join-Path $testDirectory "PRIVACY.md"),
            (Join-Path $testDirectory "THIRD-PARTY-NOTICES.txt"),
            (Join-Path $testDirectory "licenses\Apache-2.0.txt"))) {
        if (-not (Test-Path -LiteralPath $requiredFile)) {
            throw "설치본의 필수 파일이 없습니다: $requiredFile"
        }
    }

    $env:SENTORY_DATA_DIR = Join-Path $testDirectory ".health"
    $health = Start-Process `
        -FilePath $installedExecutable `
        -ArgumentList "--verify-installation" `
        -WindowStyle Hidden `
        -Wait `
        -PassThru
    if ($health.ExitCode -ne 0) {
        throw "설치본 자체 점검 종료 코드: $($health.ExitCode)"
    }

    $uninstaller = Join-Path $testDirectory "unins000.exe"
    $uninstall = Start-Process `
        -FilePath $uninstaller `
        -ArgumentList @(
            "/VERYSILENT",
            "/SUPPRESSMSGBOXES",
            "/NORESTART") `
        -WindowStyle Hidden `
        -Wait `
        -PassThru
    if ($uninstall.ExitCode -ne 0) {
        throw "제거 프로그램 종료 코드: $($uninstall.ExitCode)"
    }

    if (Test-Path -LiteralPath $installedExecutable) {
        throw "제거 후 Sentory.exe가 남아 있습니다."
    }

    Write-Host "x64 설치 → 실행 자체 점검 → 제거 왕복 검증을 통과했습니다." `
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

    if ($hadRunValue) {
        New-Item -Path $runKey -Force | Out-Null
        Set-ItemProperty `
            -Path $runKey `
            -Name Sentory `
            -Value $existingRunValue
    }
    else {
        Remove-ItemProperty `
            -Path $runKey `
            -Name Sentory `
            -ErrorAction SilentlyContinue
    }

    Remove-TestDirectoryIfPresent
    if ($installationSucceeded -and
        (Test-Path -LiteralPath $installLogPath)) {
        Remove-Item -LiteralPath $installLogPath -Force
    }
}
