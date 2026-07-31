[CmdletBinding()]
param(
    [string]$IdentityFile = "installer\msix\StoreIdentity.json",
    [string]$OutputPath = "artifacts\store-test\Sentory-TestCertificate.cer",
    [string]$FriendlyName = "Sentory MSIX local test"
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

$identityPath = Resolve-RepositoryPath $IdentityFile
if (-not (Test-Path -LiteralPath $identityPath -PathType Leaf)) {
    throw "Store identity 파일을 찾지 못했습니다: $identityPath"
}
$identity = Get-Content -Raw -LiteralPath $identityPath | ConvertFrom-Json
$publisher = [string]$identity.publisher
if ([string]::IsNullOrWhiteSpace($publisher)) {
    throw "Store identity에 Publisher가 없습니다."
}

$minimumExpiry = (Get-Date).AddDays(30)
$certificate = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object {
        $extensionOids = @($_.Extensions | ForEach-Object { $_.Oid.Value })
        $_.Subject -eq $publisher -and
        $_.FriendlyName -eq $FriendlyName -and
        $_.HasPrivateKey -and
        $_.NotAfter -gt $minimumExpiry -and
        $extensionOids -contains "2.5.29.37" -and
        $extensionOids -contains "2.5.29.19"
    } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if (-not $certificate) {
    $certificate = New-SelfSignedCertificate `
        -Type Custom `
        -Subject $publisher `
        -FriendlyName $FriendlyName `
        -CertStoreLocation Cert:\CurrentUser\My `
        -KeyAlgorithm RSA `
        -KeyLength 2048 `
        -HashAlgorithm SHA256 `
        -KeyUsage DigitalSignature `
        -KeyExportPolicy NonExportable `
        -NotAfter (Get-Date).AddYears(2) `
        -TextExtension @(
            "2.5.29.37={text}1.3.6.1.5.5.7.3.3",
            "2.5.29.19={text}")
}

$certificatePath = Resolve-RepositoryPath $OutputPath
$certificateDirectory = Split-Path -Parent $certificatePath
New-Item -ItemType Directory -Path $certificateDirectory -Force | Out-Null
Export-Certificate `
    -Cert $certificate `
    -FilePath $certificatePath `
    -Type CERT `
    -Force |
    Out-Null

$thumbprintPath = [System.IO.Path]::ChangeExtension(
    $certificatePath,
    ".thumbprint.txt")
$certificate.Thumbprint |
    Set-Content -LiteralPath $thumbprintPath -Encoding ascii

Write-Host "Sentory MSIX 검수 인증서를 준비했습니다: $certificatePath"
Write-Host "인증서 지문: $($certificate.Thumbprint)"
Write-Output $certificate.Thumbprint
