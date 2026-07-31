[CmdletBinding()]
param(
    [string]$BundlePath = "artifacts\store-test\Sentory-2.0.2-test.msixbundle",
    [string]$CertificatePath = "artifacts\store-test\Sentory-TestCertificate.cer"
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

$principal = [Security.Principal.WindowsPrincipal]::new(
    [Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "이 설치 스크립트는 관리자 PowerShell에서 실행해야 합니다."
}

$bundle = Resolve-RepositoryPath $BundlePath
$certificateFile = Resolve-RepositoryPath $CertificatePath
foreach ($required in @($bundle, $certificateFile)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "검수 설치 파일을 찾지 못했습니다: $required"
    }
}

$certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new(
    $certificateFile)
$identity = Get-Content -Raw -LiteralPath (
    Join-Path $repositoryRoot "installer\msix\StoreIdentity.json") |
    ConvertFrom-Json
if ($certificate.Subject -ne [string]$identity.publisher) {
    throw "검수 인증서 Subject가 Store Publisher와 일치하지 않습니다."
}
$signature = Get-AuthenticodeSignature -FilePath $bundle
if (-not $signature.SignerCertificate -or
    $signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) {
    throw "MSIX 서명과 검수 인증서가 일치하지 않습니다."
}

Import-Certificate `
    -FilePath $certificateFile `
    -CertStoreLocation Cert:\LocalMachine\TrustedPeople |
    Out-Null

$trustedSignature = Get-AuthenticodeSignature -FilePath $bundle
if ($trustedSignature.Status -ne "Valid") {
    throw "검수 인증서를 신뢰한 뒤에도 MSIX 서명이 유효하지 않습니다: $($trustedSignature.StatusMessage)"
}

Add-AppxPackage -Path $bundle
Write-Host "Sentory MSIX 검수판을 설치했습니다." -ForegroundColor Green
Write-Host "검수를 마치면 앱을 제거하고 LocalMachine TrustedPeople의 인증서도 지우세요."
Write-Host "검수 인증서 지문: $($certificate.Thumbprint)"
