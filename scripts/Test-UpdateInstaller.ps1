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
$sourceExecutable = Join-Path $sourceDirectory "Sentory.exe"
$testInstaller = Join-Path $artifactsRootFull "Sentory-update-test.exe"
$testDirectory = Join-Path $artifactsRootFull ".update-installer-check"
$restartedExecutable = Join-Path $testDirectory "Sentory.exe"
$healthDirectory = Join-Path $artifactsRootFull ".update-installer-health"
$runningDataDirectory = Join-Path $artifactsRootFull ".update-running-app"
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
if (-not (Test-Path -LiteralPath $sourceExecutable)) {
    throw "x64 휴대용 배포 파일을 먼저 만들어 주세요: $sourceExecutable"
}
if (Get-CimInstance Win32_Process -Filter "Name = 'Sentory.exe'") {
    throw "업데이트 검증 전에 실행 중인 Sentory를 모두 종료해 주세요."
}

$numericParts = $version.Split('-', 2)[0].Split('.')
$numericVersion = "{0}.{1}.{2}.0" -f `
    $numericParts[0], $numericParts[1], $numericParts[2]
$testAppId = "{{0FEA6707-89C6-4D6C-B3D3-A83F0508598B}"
$previousDataDirectory = $env:SENTORY_DATA_DIR
$runningSentory = $null
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$existingRunValue = (Get-ItemProperty `
        -Path $runKey `
        -Name Sentory `
        -ErrorAction SilentlyContinue).Sentory
$hadRunValue = $null -ne $existingRunValue

$temporaryPaths = @(
    $testInstaller,
    $testDirectory,
    $healthDirectory,
    $runningDataDirectory,
    $logPath)
foreach ($path in $temporaryPaths) {
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

    $env:SENTORY_DATA_DIR = $runningDataDirectory
    $runningSentory = Start-Process `
        -FilePath $sourceExecutable `
        -WindowStyle Hidden `
        -PassThru
    $runningDeadline = [DateTime]::UtcNow.AddSeconds(15)
    while (-not $runningSentory.HasExited -and
        -not (Test-Path -LiteralPath (
            Join-Path $runningDataDirectory "sentory.db")) -and
        [DateTime]::UtcNow -lt $runningDeadline) {
        Start-Sleep -Milliseconds 100
        $runningSentory.Refresh()
    }
    if ($runningSentory.HasExited) {
        throw "기존 Sentory 실행 상태를 만들지 못했습니다."
    }

    $env:SENTORY_DATA_DIR = $healthDirectory
    $install = Start-Process `
        -FilePath $testInstaller `
        -ArgumentList @(
            "/SILENT",
            "/SUPPRESSMSGBOXES",
            "/CLOSEAPPLICATIONS",
            "/NORESTART",
            "/NOICONS",
            "/SENTORYUPDATE=1",
            "/DIR=$testDirectory",
            "/LOG=$logPath") `
        -WindowStyle Normal `
        -PassThru

    Start-Sleep -Milliseconds 500
    if (-not $runningSentory.HasExited) {
        Stop-Process -Id $runningSentory.Id -Force
        $runningSentory.WaitForExit()
    }

    $progressWindowObserved = $false
    $installDeadline = [DateTime]::UtcNow.AddMinutes(2)
    while (-not $install.HasExited -and
        [DateTime]::UtcNow -lt $installDeadline) {
        $install.Refresh()
        $progressWindowObserved = $progressWindowObserved -or [bool](
            Get-Process -ErrorAction SilentlyContinue |
                Where-Object {
                    $_.MainWindowHandle -ne 0 -and
                    $_.MainWindowTitle -like "Sentory $version*"
                } |
                Select-Object -First 1)
        Start-Sleep -Milliseconds 100
    }
    if (-not $install.HasExited) {
        Stop-Process -Id $install.Id -Force
        throw "업데이트 설치 검증이 제한 시간 안에 끝나지 않았습니다."
    }
    $install.WaitForExit()
    if ($install.ExitCode -ne 0) {
        throw "업데이트 설치 검증 종료 코드: $($install.ExitCode)"
    }
    if (-not $progressWindowObserved) {
        throw "업데이트 설치 진행창이 표시되지 않았습니다."
    }
    $restartDeadline = [DateTime]::UtcNow.AddSeconds(20)
    $restartedSentory = $null
    while (-not $restartedSentory -and
        [DateTime]::UtcNow -lt $restartDeadline) {
        $restartedSentory = Get-CimInstance `
                Win32_Process `
                -Filter "Name = 'Sentory.exe'" |
            Where-Object {
                $_.ExecutablePath -eq $restartedExecutable -and
                $_.CommandLine -notmatch "discord-accessibility-worker"
            } |
            Select-Object -First 1
        if (-not $restartedSentory) {
            Start-Sleep -Milliseconds 100
        }
    }
    if (-not $restartedSentory) {
        throw "업데이트 뒤 Sentory가 자동으로 다시 실행되지 않았습니다."
    }
    if (-not (Test-Path -LiteralPath (
        Join-Path $healthDirectory "sentory.db"))) {
        throw "다시 실행된 Sentory가 보관함을 초기화하지 못했습니다."
    }

    Write-Host (
        "실행 중 업데이트 → 진행창 표시 → Sentory 재실행 " +
        "자체 점검을 통과했습니다.") -ForegroundColor Green
}
finally {
    $env:SENTORY_DATA_DIR = $previousDataDirectory
    if ($runningSentory -and -not $runningSentory.HasExited) {
        Stop-Process -Id $runningSentory.Id -Force
        $runningSentory.WaitForExit()
    }
    Get-CimInstance Win32_Process -Filter "Name = 'Sentory.exe'" |
        Where-Object {
            $_.ExecutablePath -eq $sourceExecutable -or
            $_.ExecutablePath -eq $restartedExecutable
        } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force }

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

    foreach ($path in $temporaryPaths) {
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Recurse -Force
        }
    }
}
