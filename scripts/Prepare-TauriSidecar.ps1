param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot "src\Sentory.Engine.Bridge\Sentory.Engine.Bridge.csproj"
$publishDirectory = Join-Path $repositoryRoot "artifacts\tauri-sidecar\$Runtime"
$binaryDirectory = Join-Path $repositoryRoot "src\Sentory.Tauri\src-tauri\binaries"
$targetTriple = if ($Runtime -eq "win-arm64") {
    "aarch64-pc-windows-msvc"
} else {
    "x86_64-pc-windows-msvc"
}
$destination = Join-Path $binaryDirectory "sentory-engine-$targetTriple.exe"

New-Item -ItemType Directory -Force -Path $publishDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $binaryDirectory | Out-Null

dotnet publish $projectPath `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishDirectory
if ($LASTEXITCODE -ne 0) {
    throw "C# Tauri 사이드카 게시에 실패했습니다."
}

Copy-Item -LiteralPath (Join-Path $publishDirectory "sentory-engine.exe") `
    -Destination $destination `
    -Force

Write-Host "Tauri sidecar prepared: $destination"
