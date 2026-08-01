param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [ValidateSet("x64", "arm64")]
    [string]$Architecture = "x64"
)

$ErrorActionPreference = "Stop"
& (Join-Path $PSScriptRoot "Build-Tauri.ps1") `
    -Configuration $Configuration `
    -Architecture $Architecture `
    -DeveloperBuild
if ($LASTEXITCODE -ne 0) {
    throw "Tauri 개발자판 빌드에 실패했습니다."
}
