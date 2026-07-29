param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$tauriRoot = Join-Path $repositoryRoot "src\Sentory.Tauri"
$cargoBin = Join-Path $env:USERPROFILE ".cargo\bin"
if ($env:Path -notlike "*$cargoBin*") {
    $env:Path = "$cargoBin;$env:Path"
}
$env:CARGO_BUILD_JOBS = "1"

$vswhere = Join-Path ${env:ProgramFiles(x86)} `
    "Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path -LiteralPath $vswhere)) {
    throw "Visual Studio 설치 검색 도구를 찾지 못했습니다."
}
$visualStudioPath = & $vswhere `
    -latest `
    -products * `
    -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
    -property installationPath
if ([string]::IsNullOrWhiteSpace($visualStudioPath)) {
    throw "Tauri에 필요한 Visual C++ x64 빌드 도구를 찾지 못했습니다."
}
$developerShell = Join-Path $visualStudioPath `
    "Common7\Tools\Microsoft.VisualStudio.DevShell.dll"
Import-Module $developerShell
Enter-VsDevShell `
    -VsInstallPath $visualStudioPath `
    -SkipAutomaticLocation `
    -DevCmdArguments "-arch=x64 -host_arch=x64"

& (Join-Path $PSScriptRoot "Prepare-TauriSidecar.ps1") `
    -Configuration $Configuration `
    -Runtime win-x64

Push-Location $tauriRoot
try {
    if (-not (Test-Path (Join-Path $tauriRoot "node_modules"))) {
        npm install
        if ($LASTEXITCODE -ne 0) {
            throw "Tauri npm 의존성 설치에 실패했습니다."
        }
    }

    npm run tauri -- build --no-bundle
    if ($LASTEXITCODE -ne 0) {
        throw "Tauri 개발자판 빌드에 실패했습니다."
    }
}
finally {
    Pop-Location
}
