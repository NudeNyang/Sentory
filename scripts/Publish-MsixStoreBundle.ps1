[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string]$PackageVersion = "2.0.1.0",
    [Parameter(Mandatory)]
    [string]$X64PayloadArchive,
    [Parameter(Mandatory)]
    [string]$Arm64PayloadArchive,
    [string]$PackageIdentityName,
    [string]$Publisher,
    [string]$PublisherDisplayName,
    [string]$IdentityFile = "installer\msix\StoreIdentity.json",
    [string]$OutputRoot = "artifacts\store",
    [string]$CertificateThumbprint,
    [switch]$UnsignedTest
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Split-Path -Parent $PSScriptRoot))

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
$channel = if ($UnsignedTest) { "test" } else { "store" }
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
$archives = [ordered]@{
    x64 = Resolve-RepositoryPath $X64PayloadArchive
    arm64 = Resolve-RepositoryPath $Arm64PayloadArchive
}
foreach ($archive in $archives.Values) {
    if (-not (Test-Path -LiteralPath $archive -PathType Leaf)) {
        throw "포터블 배포 ZIP을 찾지 못했습니다: $archive"
    }
}

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

$packages = foreach ($entry in $archives.GetEnumerator()) {
    $payload = Join-Path $inputRoot $entry.Key
    Expand-Archive -LiteralPath $entry.Value -DestinationPath $payload
    $arguments = @{
        PackageVersion = $PackageVersion
        Architecture = $entry.Key
        PayloadDirectory = $payload
        PackageIdentityName = $PackageIdentityName
        Publisher = $Publisher
        PublisherDisplayName = $PublisherDisplayName
        OutputRoot = $outputRootFull
        UnsignedTest = $UnsignedTest
    }
    $result = & (Join-Path $PSScriptRoot "Publish-MsixPackage.ps1") @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$($entry.Key) MSIX 생성에 실패했습니다."
    }
    $package = @($result | Where-Object { Test-Path -LiteralPath $_ })[-1]
    if (-not $package) {
        throw "$($entry.Key) MSIX 출력 경로를 확인하지 못했습니다."
    }
    Copy-Item -LiteralPath $package -Destination $bundleInput
    Get-Item -LiteralPath $package
}

$makeAppx = Find-WindowsSdkTool "makeappx.exe"
& $makeAppx bundle /d $bundleInput /p $bundlePath /o /v
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
    architectures = @("x64", "arm64")
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
