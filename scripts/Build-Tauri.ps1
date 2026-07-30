param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [ValidateSet("x64", "arm64")]
    [string]$Architecture = "x64"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$tauriRoot = Join-Path $repositoryRoot "src\Sentory.Tauri"
$cargoBin = Join-Path $env:USERPROFILE ".cargo\bin"
if ($env:Path -notlike "*$cargoBin*") {
    $env:Path = "$cargoBin;$env:Path"
}
$env:CARGO_BUILD_JOBS = "1"

$runtime = "win-$Architecture"
$targetTriple = if ($Architecture -eq "arm64") {
    "aarch64-pc-windows-msvc"
}
else {
    "x86_64-pc-windows-msvc"
}
$visualCppComponent = if ($Architecture -eq "arm64") {
    "Microsoft.VisualStudio.Component.VC.Tools.ARM64"
}
else {
    "Microsoft.VisualStudio.Component.VC.Tools.x86.x64"
}
$hostArchitecture =
    [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
$hostDevShellArchitecture = if ($hostArchitecture -eq "Arm64") {
    "arm64"
}
else {
    "x64"
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} `
    "Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path -LiteralPath $vswhere)) {
    throw "Visual Studio 설치 검색 도구를 찾지 못했습니다."
}
$visualStudioPath = & $vswhere `
    -latest `
    -products * `
    -requires $visualCppComponent `
    -property installationPath
if ([string]::IsNullOrWhiteSpace($visualStudioPath)) {
    throw "Tauri에 필요한 Visual C++ $Architecture 빌드 도구를 찾지 못했습니다."
}
$developerShell = Join-Path $visualStudioPath `
    "Common7\Tools\Microsoft.VisualStudio.DevShell.dll"
Import-Module $developerShell
Enter-VsDevShell `
    -VsInstallPath $visualStudioPath `
    -SkipAutomaticLocation `
    -DevCmdArguments "-arch=$Architecture -host_arch=$hostDevShellArchitecture"

$installedTargets = @(rustup target list --installed)
if ($LASTEXITCODE -ne 0 -or $installedTargets -notcontains $targetTriple) {
    throw "Rust 대상이 설치되어 있지 않습니다: rustup target add $targetTriple"
}

& (Join-Path $PSScriptRoot "Prepare-TauriSidecar.ps1") `
    -Configuration $Configuration `
    -Runtime $runtime

Push-Location $tauriRoot
try {
    if (-not (Test-Path (Join-Path $tauriRoot "node_modules"))) {
        npm install
        if ($LASTEXITCODE -ne 0) {
            throw "Tauri npm 의존성 설치에 실패했습니다."
        }
    }

    npm run tauri -- build --no-bundle --target $targetTriple
    if ($LASTEXITCODE -ne 0) {
        throw "Tauri 빌드에 실패했습니다."
    }
}
finally {
    Pop-Location
}
