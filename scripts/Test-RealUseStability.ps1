[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repositoryRoot "Sentory.sln"

Push-Location $repositoryRoot
try {
    if (-not $NoBuild) {
        dotnet build $solutionPath --configuration $Configuration --no-restore
        if ($LASTEXITCODE -ne 0) {
            throw "Sentory 빌드에 실패했습니다."
        }
    }

    $arguments = @(
        "test"
        $solutionPath
        "--configuration"
        $Configuration
        "--no-build"
        "--logger"
        "console;verbosity=minimal"
    )
    dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Sentory 안정성 테스트에 실패했습니다."
    }

    Write-Host ""
    Write-Host "자동 안정성 검증을 통과했습니다." -ForegroundColor Green
    Write-Host "남은 실제 사용 검수 항목은 docs/04-real-use-stability-and-detection-status.md를 확인해 주세요."
}
finally {
    Pop-Location
}
