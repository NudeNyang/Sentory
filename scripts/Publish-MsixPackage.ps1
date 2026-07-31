[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string]$PackageVersion,
    [Parameter(Mandatory)]
    [ValidateSet("x64", "arm64")]
    [string]$Architecture,
    [Parameter(Mandatory)]
    [string]$PayloadDirectory,
    [Parameter(Mandatory)]
    [string]$PackageIdentityName,
    [Parameter(Mandatory)]
    [string]$Publisher,
    [Parameter(Mandatory)]
    [string]$PublisherDisplayName,
    [string]$OutputRoot = "artifacts\store",
    [switch]$UnsignedTest,
    [switch]$TestPackage
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Split-Path -Parent $PSScriptRoot))
$manifestTemplate = Join-Path `
    $repositoryRoot `
    "installer\msix\AppxManifest.xml.template"
$assetDirectory = Join-Path $repositoryRoot "installer\msix\Assets"
$unsignedMarker = "OID.2.25.311729368913984317654407730594956997722=1"

function Resolve-RepositoryPath {
    param([Parameter(Mandatory)][string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }
    return [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $Path))
}

function Assert-OutputChildPath {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Path)

    $rootFull = [System.IO.Path]::GetFullPath($Root).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $pathFull = [System.IO.Path]::GetFullPath($Path)
    $prefix = $rootFull + [System.IO.Path]::DirectorySeparatorChar
    if (-not $pathFull.StartsWith(
            $prefix,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "MSIX 출력 폴더 밖의 경로는 정리할 수 없습니다: $pathFull"
    }
    return $pathFull
}

function Find-WindowsSdkTool {
    param([Parameter(Mandatory)][string]$Name)

    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10"
    $candidates = Get-ChildItem `
        -LiteralPath $kitsRoot `
        -Recurse `
        -Filter $Name `
        -File `
        -ErrorAction SilentlyContinue |
        Where-Object {
            $_.FullName -match '\\x64\\' -or
            $_.DirectoryName -like "*App Certification Kit*"
        } |
        Sort-Object @{ Expression = { $_.VersionInfo.FileVersionRaw }; Descending = $true }
    $tool = $candidates | Select-Object -First 1
    if (-not $tool) {
        throw "Windows SDK 도구를 찾지 못했습니다: $Name"
    }
    return $tool.FullName
}

function Get-PeMachine {
    param([Parameter(Mandatory)][string]$Path)

    $stream = [System.IO.File]::OpenRead($Path)
    $reader = [System.IO.BinaryReader]::new($stream)
    try {
        $stream.Position = 0x3C
        $peOffset = $reader.ReadInt32()
        $stream.Position = $peOffset + 4
        return $reader.ReadUInt16()
    }
    finally {
        $reader.Dispose()
        $stream.Dispose()
    }
}

function Escape-Xml {
    param([Parameter(Mandatory)][string]$Value)

    return [System.Security.SecurityElement]::Escape($Value)
}

if ($PackageIdentityName -notmatch '^[A-Za-z0-9.-]{3,50}$' -or
    $PackageIdentityName.EndsWith('.')) {
    throw "Package Identity Name 형식이 올바르지 않습니다. Partner Center 값을 그대로 입력하세요."
}
$versionParts = $PackageVersion.Split('.')
if ($versionParts | Where-Object { [int]$_ -gt 65535 }) {
    throw "MSIX 버전의 각 숫자는 0~65535 범위여야 합니다."
}
if ($UnsignedTest -and $Publisher -notmatch [regex]::Escape($unsignedMarker)) {
    $Publisher = "$Publisher, $unsignedMarker"
}
if (-not $UnsignedTest -and $Publisher -match [regex]::Escape($unsignedMarker)) {
    throw "Store 제출용 Publisher에는 unsigned 시험용 OID를 넣을 수 없습니다."
}
if ($Publisher -notmatch '^[A-Za-z][A-Za-z0-9.]*=.+$') {
    throw "Publisher는 Partner Center가 제공한 X.509 DN 형식이어야 합니다."
}
if ([string]::IsNullOrWhiteSpace($PublisherDisplayName)) {
    throw "PublisherDisplayName이 필요합니다."
}

$payloadRoot = Resolve-RepositoryPath $PayloadDirectory
$outputRootFull = Resolve-RepositoryPath $OutputRoot
foreach ($required in @("Sentory.exe", "sentory-engine.exe")) {
    $requiredPath = Join-Path $payloadRoot $required
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "MSIX 페이로드 파일을 찾지 못했습니다: $requiredPath"
    }
}
if (-not (Test-Path -LiteralPath $manifestTemplate -PathType Leaf)) {
    throw "MSIX 매니페스트 템플릿을 찾지 못했습니다."
}
if (-not (Test-Path -LiteralPath $assetDirectory -PathType Container)) {
    throw "MSIX 시각 자산이 없습니다. New-MsixAssets.ps1을 먼저 실행하세요."
}

$expectedMachine = if ($Architecture -eq "arm64") { 0xAA64 } else { 0x8664 }
foreach ($binaryName in @("Sentory.exe", "sentory-engine.exe")) {
    $binaryPath = Join-Path $payloadRoot $binaryName
    $actualMachine = Get-PeMachine $binaryPath
    if ($actualMachine -ne $expectedMachine) {
        throw ("MSIX 페이로드 아키텍처가 올바르지 않습니다. " +
            "파일: {0}, 예상: 0x{1:X4}, 실제: 0x{2:X4}" -f `
            $binaryName, $expectedMachine, $actualMachine)
    }
}

$channel = if ($UnsignedTest -or $TestPackage) { "test" } else { "store" }
$stageRoot = Join-Path $outputRootFull "staging\$channel-$Architecture"
$packageName = "Sentory-$PackageVersion-$channel-$Architecture.msix"
$packagePath = Join-Path $outputRootFull $packageName
foreach ($path in @($stageRoot, $packagePath, "$packagePath.sha256")) {
    $verified = Assert-OutputChildPath $outputRootFull $path
    if (Test-Path -LiteralPath $verified) {
        Remove-Item -LiteralPath $verified -Recurse -Force
    }
}
New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null
Copy-Item -Path (Join-Path $payloadRoot "*") `
    -Destination $stageRoot `
    -Recurse `
    -Force
Copy-Item -LiteralPath $assetDirectory `
    -Destination (Join-Path $stageRoot "Assets") `
    -Recurse `
    -Force

$manifest = Get-Content -Raw -LiteralPath $manifestTemplate
$manifest = $manifest.Replace(
    "__PACKAGE_IDENTITY_NAME__",
    (Escape-Xml $PackageIdentityName))
$manifest = $manifest.Replace("__PUBLISHER__", (Escape-Xml $Publisher))
$manifest = $manifest.Replace("__PACKAGE_VERSION__", $PackageVersion)
$manifest = $manifest.Replace("__ARCHITECTURE__", $Architecture)
$manifest = $manifest.Replace(
    "__PUBLISHER_DISPLAY_NAME__",
    (Escape-Xml $PublisherDisplayName))
$manifestPath = Join-Path $stageRoot "AppxManifest.xml"
$manifest | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
[xml](Get-Content -Raw -LiteralPath $manifestPath) | Out-Null

$makePri = Find-WindowsSdkTool "makepri.exe"
$priConfigPath = Join-Path $stageRoot "priconfig.xml"
$resourcesPriPath = Join-Path $stageRoot "resources.pri"
try {
    & $makePri createconfig /cf $priConfigPath /dq en-US /o | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "MakePri 설정 파일 생성에 실패했습니다."
    }
    & $makePri new `
        /pr $stageRoot `
        /cf $priConfigPath `
        /of $resourcesPriPath `
        /o |
        Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "MSIX 리소스 인덱스 생성에 실패했습니다."
    }
}
finally {
    if (Test-Path -LiteralPath $priConfigPath -PathType Leaf) {
        Remove-Item -LiteralPath $priConfigPath -Force
    }
}

$makeAppx = Find-WindowsSdkTool "makeappx.exe"
New-Item -ItemType Directory -Path $outputRootFull -Force | Out-Null
& $makeAppx pack /d $stageRoot /p $packagePath /o /v /h SHA256
if ($LASTEXITCODE -ne 0) {
    throw "MakeAppx가 $Architecture MSIX를 만들지 못했습니다."
}

$hash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash
"$hash  $packageName" |
    Set-Content -LiteralPath "$packagePath.sha256" -Encoding ascii

Write-Host "Sentory $Architecture MSIX를 만들었습니다: $packagePath"
Write-Output $packagePath
