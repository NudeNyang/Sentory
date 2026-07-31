# Microsoft Store MSIX 배포

Sentory의 Store 배포판은 Tauri 호스트와 C# 엔진을 함께 넣은 데스크톱 MSIX다.
x64와 ARM64를 각각 패키징하고 하나의 `msixbundle`로 묶는다. WPF 실행 파일은
포함하지 않는다.

## 먼저 필요한 값

Partner Center에서 앱 이름을 예약한 뒤 `제품 관리 > 제품 ID`에 표시되는 다음
세 값을 준비한다.

- `Package/Identity/Name`
- `Package/Identity/Publisher`
- `Package/Properties/PublisherDisplayName`

대소문자와 공백을 포함해 화면에 나온 값을 그대로 써야 한다. Sentory에 예약된
실제 값은 [`installer/msix/StoreIdentity.json`](../installer/msix/StoreIdentity.json)에
기록했다. 다른 Store 제품에 재사용할 때는
[`StoreIdentity.example.json`](../installer/msix/StoreIdentity.example.json)을 참고해
세 값을 바꿔야 한다.

## Store 제출용 번들 만들기

2.0.2 공개 릴리즈의 포터블 ZIP을 내려받았다는 전제로 다음처럼 실행한다.

```powershell
.\scripts\Publish-MsixStoreBundle.ps1 `
  -PackageVersion 2.0.2.0 `
  -X64PayloadArchive artifacts\store-input\Sentory-win-x64-portable.zip `
  -Arm64PayloadArchive artifacts\store-input\Sentory-win-arm64-portable.zip
```

결과는 `artifacts/store`에 생성된다.

- `Sentory-2.0.2-store.msixbundle`: Partner Center 제출 파일
- `Sentory-2.0.2-store.msixbundle.sha256`: 번들 SHA-256
- `Sentory-2.0.2.0-store-x64.msix`: x64 개별 패키지
- `Sentory-2.0.2.0-store-arm64.msix`: ARM64 개별 패키지
- `msix-package-manifest.json`: identity와 각 파일의 크기·해시

Store 제출용 MSIX는 로컬 인증서로 서명하지 않아도 된다. Microsoft Store가 인증
후 패키지를 서명한다. Store 밖에 따로 배포하려면 Publisher와 주체가 일치하는 코드
서명 인증서가 필요하며, 현재 사용자 인증서 저장소의 지문을
`-CertificateThumbprint`에 넘기면 번들에 SHA-256 서명을 적용한다.

## 설치 시험용 unsigned 번들

Partner Center identity가 아직 없을 때는 다음 옵션으로 구조와 실행 파일 구성을
검사할 수 있다.

```powershell
.\scripts\Publish-MsixStoreBundle.ps1 `
  -PackageVersion 2.0.2.0 `
  -X64PayloadArchive artifacts\store-input\Sentory-win-x64-portable.zip `
  -Arm64PayloadArchive artifacts\store-input\Sentory-win-arm64-portable.zip `
  -PackageIdentityName NudeNyang.Sentory.Test `
  -Publisher 'CN=NudeNyang' `
  -PublisherDisplayName NudeNyang `
  -IdentityFile '' `
  -UnsignedTest
```

이 모드에서는 Windows가 요구하는 unsigned OID를 Publisher 끝에 추가하고 결과
파일 이름에도 `test`를 넣는다. 실행 파일이 들어 있는 unsigned 패키지를 직접
설치하려면 관리자 PowerShell에서 `Add-AppxPackage -AllowUnsigned`가 필요하다.
시험용 identity는 서명된 Store identity와 다르므로 제출 파일로 사용하면 안 된다.

## 패키지 구성과 인증 확인

- 매니페스트는 `Windows.FullTrustApplication`과 제한 기능 `runFullTrust`를
  선언한다. Sentory가 클립보드, 전역 입력 감지와 메신저 프로세스 연동을 수행하는
  데 필요한 데스크톱 권한이다.
- 앱 데이터는 설치 폴더가 아닌 로컬 앱 데이터에 저장한다. MSIX에서는 AppData가
  패키지별 저장소로 리디렉션되므로 Store 업데이트 사이에는 유지되지만, 기존
  포터블판의 `%LOCALAPPDATA%\Sentory` 데이터가 자동으로 옮겨진다고 가정하면 안
  된다. 포터블판에서 Store판으로 전환하는 사용자 데이터 이전은 별도 검수가
  필요하다.
- 패키징 스크립트는 앱과 엔진의 PE 아키텍처, identity 형식, 4단계 버전,
  매니페스트 XML과 MakeAppx 의미 검증을 확인한다.
- x64 시험 패키지는 WACK 전체 결과 `PASS`를 받았다. 선택 항목인 "차단된 실행
  파일" 검사는 프로세스 실행 API와 실행 파일 안의 `cmd`, `PowerShell` 문자열을
  찾아 `FAIL`로 표시했지만 전체 인증 결과에는 영향을 주지 않았다. WACK 자체도
  현재 선택 검사이며 최종 인증 결과는 Partner Center 제출 과정에서 확인한다.
- Store 등록 정보에는 설명과 스크린샷 한 장 이상이 필요하다. 이 항목은 MSIX
  안이 아니라 Partner Center의 Store 등록 정보 화면에서 입력한다.

Microsoft 공식 참고 문서:

- [제품 identity 확인](https://learn.microsoft.com/windows/apps/publish/view-app-identity-details)
- [MakeAppx로 패키지 만들기](https://learn.microsoft.com/windows/msix/package/create-app-package-with-makeappx-tool)
- [Microsoft Store의 MSIX 서명](https://learn.microsoft.com/windows/apps/package-and-deploy/code-signing-options)
- [Windows 앱 인증 키트](https://learn.microsoft.com/windows/uwp/debug-test-perf/windows-app-certification-kit)
- [패키징된 데스크톱 앱의 AppData 동작](https://learn.microsoft.com/windows/msix/desktop/desktop-to-uwp-prepare)
