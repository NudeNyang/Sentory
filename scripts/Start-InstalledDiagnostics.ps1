[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version = "1.5.1",
    [string]$ExecutablePath = (
        Join-Path $env:LOCALAPPDATA "Programs\Sentory\Sentory.exe"),
    [string]$DataDirectory = (
        Join-Path $env:LOCALAPPDATA "Sentory-Diagnostics\$Version")
)

$ErrorActionPreference = "Stop"
$executableFullPath = [System.IO.Path]::GetFullPath($ExecutablePath)
$dataDirectoryFullPath = [System.IO.Path]::GetFullPath($DataDirectory)

if (-not (Test-Path -LiteralPath $executableFullPath -PathType Leaf)) {
    throw "Sentory 설치판을 찾을 수 없습니다: $executableFullPath"
}

$runningProcesses = @(Get-Process -Name "Sentory" -ErrorAction SilentlyContinue)
if ($runningProcesses.Count -gt 0) {
    $processIds = $runningProcesses.Id -join ", "
    throw "실행 중인 Sentory를 먼저 종료해 주세요. PID: $processIds"
}

New-Item -ItemType Directory -Path $dataDirectoryFullPath -Force |
    Out-Null

$previousDataDirectory = [Environment]::GetEnvironmentVariable(
    "SENTORY_DATA_DIR",
    [EnvironmentVariableTarget]::Process)

try {
    [Environment]::SetEnvironmentVariable(
        "SENTORY_DATA_DIR",
        $dataDirectoryFullPath,
        [EnvironmentVariableTarget]::Process)
    $process = Start-Process `
        -FilePath $executableFullPath `
        -PassThru
}
finally {
    [Environment]::SetEnvironmentVariable(
        "SENTORY_DATA_DIR",
        $previousDataDirectory,
        [EnvironmentVariableTarget]::Process)
}

[PSCustomObject]@{
    ProcessId = $process.Id
    Version = $Version
    ExecutablePath = $executableFullPath
    DataDirectory = $dataDirectoryFullPath
}
