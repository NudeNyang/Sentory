[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Executable,
    [Parameter(Mandatory)]
    [string]$VmName,
    [Parameter(Mandatory)]
    [string]$VmUsername,
    [Parameter(Mandatory)]
    [string]$VmPasswordFile,
    [Parameter(Mandatory)]
    [string]$GuestTargetPath,
    [string]$VBoxManagePath =
        "C:\Program Files\Oracle\VirtualBox\VBoxManage.exe",
    [switch]$AllowStoppedInstance
)

$ErrorActionPreference = "Stop"
$executableFull = [System.IO.Path]::GetFullPath($Executable)
$passwordFileFull = [System.IO.Path]::GetFullPath($VmPasswordFile)

foreach ($requiredPath in @(
        $executableFull,
        $passwordFileFull,
        $VBoxManagePath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "필수 파일을 찾지 못했습니다: $requiredPath"
    }
}

$candidate = Get-Item -LiteralPath $executableFull
if ($candidate.VersionInfo.ProductVersion -notmatch '\+developers') {
    throw "QA VM에는 for Developers 빌드만 배포할 수 있습니다."
}

function Invoke-VBoxManage {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments,
        [switch]$IgnoreExitCode
    )

    & $VBoxManagePath @Arguments
    $exitCode = $LASTEXITCODE
    $script:LastVBoxExitCode = $exitCode
    if (-not $IgnoreExitCode -and $exitCode -ne 0) {
        throw "VBoxManage가 종료 코드 $exitCode`(으`)로 실패했습니다."
    }

}

function Invoke-GuestPowerShell {
    param(
        [Parameter(Mandatory)]
        [string]$Command,
        [switch]$IgnoreExitCode
    )

    $encodedCommand = [Convert]::ToBase64String(
        [Text.Encoding]::Unicode.GetBytes($Command))
    $arguments = @(
        "guestcontrol",
        $VmName,
        "run",
        "--username", $VmUsername,
        "--passwordfile", $passwordFileFull,
        "--exe", "C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
        "--wait-stdout",
        "--wait-stderr",
        "--timeout", "60000",
        "--",
        "powershell.exe",
        "-NoProfile",
        "-NonInteractive",
        "-ExecutionPolicy", "Bypass",
        "-EncodedCommand", $encodedCommand)
    return Invoke-VBoxManage `
        -Arguments $arguments `
        -IgnoreExitCode:$IgnoreExitCode
}

$runningVms = & $VBoxManagePath list runningvms
if ($LASTEXITCODE -ne 0 -or
    $runningVms -notmatch ('^"' + [regex]::Escape($VmName) + '"\s')) {
    throw "QA VM이 실행 중이 아닙니다: $VmName"
}

$guestDirectory = [System.IO.Path]::GetDirectoryName($GuestTargetPath)
$guestFileName = [System.IO.Path]::GetFileName($GuestTargetPath)
if ([string]::IsNullOrWhiteSpace($guestDirectory) -or
    [string]::IsNullOrWhiteSpace($guestFileName)) {
    throw "VM 대상 실행 파일 경로가 올바르지 않습니다: $GuestTargetPath"
}

$guestUpdatePath = Join-Path `
    $guestDirectory `
    ([System.IO.Path]::GetFileNameWithoutExtension($guestFileName) +
        ".update.exe")
$escapedTarget = $GuestTargetPath.Replace("'", "''")
$escapedUpdate = $guestUpdatePath.Replace("'", "''")

Invoke-VBoxManage -Arguments @(
    "guestcontrol",
    $VmName,
    "copyto",
    "--username", $VmUsername,
    "--passwordfile", $passwordFileFull,
    $executableFull,
    $guestUpdatePath) | Out-Null

Invoke-GuestPowerShell -Command (
    "& '$escapedUpdate' --verify-installation; " +
    "if (`$LASTEXITCODE -ne 0) { exit `$LASTEXITCODE }") | Out-Null

$shutdownCommand =
    "& '$escapedUpdate' --request-shutdown; exit `$LASTEXITCODE"
Invoke-GuestPowerShell `
    -Command $shutdownCommand `
    -IgnoreExitCode | Out-Null
if ($script:LastVBoxExitCode -ne 0 -and -not $AllowStoppedInstance) {
    throw (
        "VM Sentory가 정상 종료 요청에 응답하지 않았습니다. " +
        "앱이 이미 종료된 상태라면 -AllowStoppedInstance를 사용하세요.")
}

$backupSuffix = [DateTimeOffset]::Now.ToString("yyyyMMdd-HHmmss")
$guestBackupPath = "$GuestTargetPath.backup-$backupSuffix"
$escapedBackup = $guestBackupPath.Replace("'", "''")
$replaceCommand = @"
if (Test-Path -LiteralPath '$escapedTarget') {
    Copy-Item -LiteralPath '$escapedTarget' -Destination '$escapedBackup'
}
Move-Item -LiteralPath '$escapedUpdate' -Destination '$escapedTarget' -Force
"@
Invoke-GuestPowerShell -Command $replaceCommand | Out-Null

$taskName = "Sentory QA Relaunch $([Guid]::NewGuid().ToString('N'))"
$escapedTaskName = $taskName.Replace("'", "''")
$launchCommand = @"
`$service = New-Object -ComObject 'Schedule.Service'
`$service.Connect()
`$folder = `$service.GetFolder('\')
`$task = `$service.NewTask(0)
`$task.Principal.UserId = "`$env:USERDOMAIN\`$env:USERNAME"
`$task.Principal.LogonType = 3
`$task.Settings.Enabled = `$true
`$task.Settings.ExecutionTimeLimit = 'PT0S'
`$action = `$task.Actions.Create(0)
`$action.Path = '$escapedTarget'
`$folder.RegisterTaskDefinition(
    '$escapedTaskName', `$task, 6, `$null, `$null, 3) | Out-Null
try {
    `$folder.GetTask('$escapedTaskName').Run(`$null) | Out-Null
    `$deadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 500
        try {
            `$signal = [Threading.EventWaitHandle]::OpenExisting(
                'Global\Sentory.Desktop.Shutdown')
            `$signal.Dispose()
            exit 0
        }
        catch [Threading.WaitHandleCannotBeOpenedException] {
        }
    } while ([DateTimeOffset]::UtcNow -lt `$deadline)
    exit 1
}
finally {
    `$folder.DeleteTask('$escapedTaskName', 0)
}
"@
Invoke-GuestPowerShell -Command $launchCommand | Out-Null

$hostHash = (Get-FileHash -LiteralPath $executableFull -Algorithm SHA256).Hash
$guestHash = (Invoke-GuestPowerShell -Command (
        "(Get-FileHash -LiteralPath '$escapedTarget' -Algorithm SHA256).Hash") |
    Select-Object -Last 1).Trim()
if (-not [string]::Equals(
        $hostHash,
        $guestHash,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "VM 실행 파일 SHA-256이 게시 파일과 일치하지 않습니다."
}

Write-Host "QA VM 개발자판 배포를 완료했습니다." -ForegroundColor Green
Write-Host "VM: $VmName"
Write-Host "대상: $GuestTargetPath"
Write-Host "백업: $guestBackupPath"
Write-Host "SHA-256: $hostHash"
