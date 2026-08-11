[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string]$PackageVersion = "2.0.7.0",
    [string]$PackageIdentityName,
    [string]$Publisher,
    [string]$PublisherDisplayName,
    [string]$IdentityFile = "installer\msix\StoreIdentity.json",
    [string]$OutputRoot = "artifacts\store",
    [ValidateSet("x64", "arm64")]
    [string[]]$Architectures = @("x64", "arm64"),
    [string]$CertificateThumbprint,
    [switch]$UnsignedTest,
    [switch]$SignedTest,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Split-Path -Parent $PSScriptRoot))
$tauriRoot = Join-Path $repositoryRoot "src\Sentory.Tauri"

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
    $tool = Get-ChildItem `
        -LiteralPath $kitsRoot `
        -Recurse `
        -Filter $Name `
        -File `
        -ErrorAction SilentlyContinue |
        Where-Object {
            $_.FullName -match '\\x64\\' -or
            $_.DirectoryName -like "*App Certification Kit*"
        } |
        Sort-Object @{ Expression = { $_.VersionInfo.FileVersionRaw }; Descending = $true } |
        Select-Object -First 1
    if (-not $tool) {
        throw "Windows SDK 도구를 찾지 못했습니다: $Name"
    }
    return $tool.FullName
}

if ($UnsignedTest -and -not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    throw "unsigned 시험용 번들은 인증서로 서명할 수 없습니다."
}
if ($UnsignedTest -and $SignedTest) {
    throw "unsigned 시험과 서명된 시험 모드는 함께 사용할 수 없습니다."
}
if ($SignedTest -and [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    throw "서명된 시험용 번들에는 검수 인증서 지문이 필요합니다."
}
$Architectures = @($Architectures | Select-Object -Unique)
if ($Architectures.Count -eq 0) {
    throw "MSIX를 만들 아키텍처를 하나 이상 지정해야 합니다."
}

if (-not [string]::IsNullOrWhiteSpace($IdentityFile)) {
    $identityPath = Resolve-RepositoryPath $IdentityFile
    if (-not (Test-Path -LiteralPath $identityPath -PathType Leaf)) {
        throw "Store identity 파일을 찾지 못했습니다: $identityPath"
    }
    $identity = Get-Content -Raw -LiteralPath $identityPath |
        ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace($PackageIdentityName)) {
        $PackageIdentityName = $identity.packageIdentityName
    }
    if ([string]::IsNullOrWhiteSpace($Publisher)) {
        $Publisher = $identity.publisher
    }
    if ([string]::IsNullOrWhiteSpace($PublisherDisplayName)) {
        $PublisherDisplayName = $identity.publisherDisplayName
    }
}
if ([string]::IsNullOrWhiteSpace($PackageIdentityName) -or
    [string]::IsNullOrWhiteSpace($Publisher) -or
    [string]::IsNullOrWhiteSpace($PublisherDisplayName)) {
    throw "Partner Center의 Name, Publisher, PublisherDisplayName이 필요합니다."
}

$outputRootFull = Resolve-RepositoryPath $OutputRoot
$inputRoot = Join-Path $outputRootFull "input"
$bundleInput = Join-Path $outputRootFull "bundle-input"
$channel = if ($UnsignedTest -or $SignedTest) { "test" } else { "store" }
$unsignedMarker = "OID.2.25.311729368913984317654407730594956997722=1"
$effectivePublisher = if ($UnsignedTest -and
    $Publisher -notmatch [regex]::Escape($unsignedMarker)) {
    "$Publisher, $unsignedMarker"
}
else {
    $Publisher
}
$displayVersion = $PackageVersion.Substring(
    0,
    $PackageVersion.LastIndexOf('.'))
$bundleName = "Sentory-$displayVersion-$channel.msixbundle"
$bundlePath = Join-Path $outputRootFull $bundleName
foreach ($path in @(
        $inputRoot,
        $bundleInput,
        $bundlePath,
        "$bundlePath.sha256")) {
    $verified = Assert-OutputChildPath $outputRootFull $path
    if (Test-Path -LiteralPath $verified) {
        Remove-Item -LiteralPath $verified -Recurse -Force
    }
}
New-Item -ItemType Directory -Path $inputRoot -Force | Out-Null
New-Item -ItemType Directory -Path $bundleInput -Force | Out-Null

$packages = foreach ($architecture in $Architectures) {
    if (-not $SkipBuild) {
        & (Join-Path $PSScriptRoot "Build-Tauri.ps1") `
            -Configuration Release `
            -Architecture $architecture `
            -DistributionChannel MicrosoftStore |
            Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "$architecture Microsoft Store 빌드에 실패했습니다."
        }
    }

    $targetTriple = if ($architecture -eq "arm64") {
        "aarch64-pc-windows-msvc"
    }
    else {
        "x86_64-pc-windows-msvc"
    }
    $targetDirectory = Join-Path `
        $tauriRoot `
        "src-tauri\target\$targetTriple\release"
    $builtExecutable = Join-Path $targetDirectory "sentory-tauri.exe"
    if (-not (Test-Path -LiteralPath $builtExecutable -PathType Leaf)) {
        throw "Microsoft Store 채널 실행 파일을 찾지 못했습니다: $builtExecutable"
    }
    $channelProbe = Start-Process `
        -FilePath $builtExecutable `
        -ArgumentList "--verify-microsoft-store-channel" `
        -PassThru
    if (-not $channelProbe.WaitForExit(10000)) {
        $channelProbe.Kill()
        $channelProbe.WaitForExit()
        throw ("Microsoft Store 채널 검사가 시간 안에 끝나지 않았습니다. " +
            "-SkipBuild를 빼고 다시 빌드하세요: $builtExecutable")
    }
    $channelProbe.Refresh()
    if ($channelProbe.ExitCode -ne 73) {
        throw ("Microsoft Store 채널 실행 파일이 아닙니다. " +
            "-SkipBuild를 빼고 다시 빌드하세요: $builtExecutable")
    }
    $payload = Join-Path $inputRoot $architecture
    New-Item -ItemType Directory -Path $payload -Force | Out-Null
    $payloadFiles = [ordered]@{
        (Join-Path $targetDirectory "sentory-tauri.exe") =
            (Join-Path $payload "Sentory.exe")
        (Join-Path $targetDirectory "sentory-engine.exe") =
            (Join-Path $payload "sentory-engine.exe")
        (Join-Path $repositoryRoot "LICENSE.txt") =
            (Join-Path $payload "LICENSE.txt")
        (Join-Path $repositoryRoot "distribution\README-KO.txt") =
            (Join-Path $payload "README-KO.txt")
        (Join-Path $repositoryRoot "docs\privacy.md") =
            (Join-Path $payload "PRIVACY.md")
        (Join-Path $repositoryRoot "distribution\THIRD-PARTY-NOTICES.txt") =
            (Join-Path $payload "THIRD-PARTY-NOTICES.txt")
        (Join-Path $repositoryRoot "docs\model-provenance.md") =
            (Join-Path $payload "MODEL-PROVENANCE.md")
    }
    foreach ($payloadFile in $payloadFiles.GetEnumerator()) {
        if (-not (Test-Path -LiteralPath $payloadFile.Key -PathType Leaf)) {
            throw "MSIX 페이로드 파일을 찾지 못했습니다: $($payloadFile.Key)"
        }
        Copy-Item -LiteralPath $payloadFile.Key -Destination $payloadFile.Value
    }
    Copy-Item `
        -LiteralPath (Join-Path $repositoryRoot "distribution\licenses") `
        -Destination (Join-Path $payload "licenses") `
        -Recurse

    $arguments = @{
        PackageVersion = $PackageVersion
        Architecture = $architecture
        PayloadDirectory = $payload
        PackageIdentityName = $PackageIdentityName
        Publisher = $Publisher
        PublisherDisplayName = $PublisherDisplayName
        OutputRoot = $outputRootFull
        UnsignedTest = $UnsignedTest
        TestPackage = $SignedTest
    }
    $result = & (Join-Path $PSScriptRoot "Publish-MsixPackage.ps1") @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$architecture MSIX 생성에 실패했습니다."
    }
    $package = @($result | Where-Object { Test-Path -LiteralPath $_ })[-1]
    if (-not $package) {
        throw "$architecture MSIX 출력 경로를 확인하지 못했습니다."
    }
    Copy-Item -LiteralPath $package -Destination $bundleInput
    Get-Item -LiteralPath $package
}

$makeAppx = Find-WindowsSdkTool "makeappx.exe"
& $makeAppx bundle /d $bundleInput /p $bundlePath /bv $PackageVersion /o /v
if ($LASTEXITCODE -ne 0) {
    throw "MakeAppx가 MSIX 번들을 만들지 못했습니다."
}

if (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    $signTool = Find-WindowsSdkTool "signtool.exe"
    & $signTool sign `
        /fd SHA256 `
        /sha1 $CertificateThumbprint.Replace(" ", "") `
        $bundlePath
    if ($LASTEXITCODE -ne 0) {
        throw "MSIX 번들 서명에 실패했습니다."
    }
}

$hash = (Get-FileHash -LiteralPath $bundlePath -Algorithm SHA256).Hash
"$hash  $bundleName" |
    Set-Content -LiteralPath "$bundlePath.sha256" -Encoding ascii

$summary = [ordered]@{
    product = "Sentory"
    packageVersion = $PackageVersion
    channel = $channel
    identityName = $PackageIdentityName
    publisher = $effectivePublisher
    architectures = @($Architectures)
    signed = -not [string]::IsNullOrWhiteSpace($CertificateThumbprint)
    bundle = [ordered]@{
        name = $bundleName
        size = (Get-Item -LiteralPath $bundlePath).Length
        sha256 = $hash.ToLowerInvariant()
    }
    packages = @($packages | ForEach-Object {
        [ordered]@{
            name = $_.Name
            size = $_.Length
            sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    })
}
$summaryPath = Join-Path $outputRootFull "msix-package-manifest.json"
$summary | ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath $summaryPath -Encoding utf8NoBOM

foreach ($temporaryPath in @(
        $inputRoot,
        $bundleInput,
        (Join-Path $outputRootFull "staging"))) {
    $verified = Assert-OutputChildPath $outputRootFull $temporaryPath
    if (Test-Path -LiteralPath $verified) {
        Remove-Item -LiteralPath $verified -Recurse -Force
    }
}

Write-Host ""
Write-Host "Sentory Microsoft Store MSIX 번들을 만들었습니다." -ForegroundColor Green
Write-Host "번들: $bundlePath"
Write-Host "SHA-256: $hash"
Write-Output $bundlePath
