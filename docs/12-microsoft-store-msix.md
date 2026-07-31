# Microsoft Store MSIX 배포

Store판은 Tauri 호스트와 C# 엔진을 함께 넣은 데스크톱 MSIX다. x64와 ARM64를
각각 만든 뒤 하나의 `msixbundle`로 묶는다. GitHub판 포터블 파일이나 설치
프로그램을 MSIX 안에 다시 넣지 않으며 WPF 실행 파일도 포함하지 않는다.

## Store판 동작

- 업데이트 설치와 배포는 Microsoft Store가 맡는다. 앱 안의 GitHub 업데이트
  확인 버튼과 Store 페이지 열기 기능은 표시하지 않으며, Store 빌드에서 업데이트
  확인 명령을 직접 호출해도 거부한다.
- `Windows 시작 시 실행`은 `HKCU\...\Run`을 쓰지 않는다. 매니페스트의
  `windows.startupTask`와 Windows `StartupTask` API로만 켜고 끈다. 기본값은
  꺼짐이며 사용자가 Sentory 설정에서 직접 켰을 때만 등록된다.
- Discord를 다시 시작할 때는 현재 프로세스에 접근성 실행 옵션만 전달한다.
  Discord의 로그인 시작 레지스트리를 새로 만들거나 바꾸지 않는다. GitHub 설치판
  제거기에 남아 있는 구버전 복원 명령도 Store판에서는 실행할 수 없다.
- GitHub판과 Store판은 같은 사용자 보관함을 사용하므로 동시에 실행하지 않는다.
  Tauri 단일 실행 식별자도 같아서 먼저 실행된 한 인스턴스만 유지된다.

## 데이터 위치

제거 뒤에도 남아야 하는 데이터와 다시 만들 수 있는 로컬 데이터를 나눠 저장한다.

| 종류 | 위치 | MSIX 제거 시 |
|---|---|---|
| 보관함 DB, 사진, 설정, 동기화 상태 | `%LOCALAPPDATA%\Sentory` | 유지 |
| 로그, 링크 미리보기, OCR 모델 | `%LOCALAPPDATA%\Packages\<PFN>\LocalState\Sentory` | 제거될 수 있음 |

Store판도 GitHub판과 같은 `%LOCALAPPDATA%\Sentory` 보관함을 읽는다. MSIX를
지우거나 다시 설치해도 보관함과 설정이 남고, 패키지 영역의 로그와 캐시는 없어져도
필요할 때 다시 만들어진다. Windows 설정의 앱 `초기화`는 패키지 영역을 지우므로
문제 해결용으로만 사용한다.

모든 기록을 직접 없애려면 Sentory를 완전히 종료한 뒤
`%LOCALAPPDATA%\Sentory`를 삭제한다. Store판 제거만으로 이 폴더를 지우지 않는다.

## 제품 identity

Partner Center에서 앱 이름을 예약한 뒤 `제품 관리 > 제품 ID`의 다음 값을
그대로 사용한다.

- `Package/Identity/Name`
- `Package/Identity/Publisher`
- `Package/Properties/PublisherDisplayName`

Sentory의 값은 [`installer/msix/StoreIdentity.json`](../installer/msix/StoreIdentity.json)에
기록했다. 예제 형식은
[`StoreIdentity.example.json`](../installer/msix/StoreIdentity.example.json)에서
확인할 수 있다.

## 검수용 x64 패키지

현재 PC에서는 Store 제출본과 별도로 자체 서명한 x64 시험 번들을 만든다. 먼저
Store Publisher와 같은 Subject를 가진 로컬 검수 인증서를 준비한다.

```powershell
$thumbprint = .\scripts\New-MsixTestCertificate.ps1

.\scripts\Publish-MsixStoreBundle.ps1 `
  -PackageVersion 2.0.2.0 `
  -Architectures x64 `
  -OutputRoot artifacts\store-test `
  -SignedTest `
  -CertificateThumbprint $thumbprint `
  -SkipBuild
```

`-SkipBuild`는 같은 커밋의 x64 Store 채널 실행 파일을 이미 만든 경우에만 쓴다.
처음부터 만들 때는 이 옵션을 뺀다. 공개키 인증서는
`artifacts\store-test\Sentory-TestCertificate.cer`에 저장되고 개인키는 현재
Windows 사용자의 인증서 저장소 밖으로 내보내지 않는다.

패키징 직전에는 실행 파일의 Store 채널 검사를 다시 수행한다. 마지막 빌드가
GitHub판이거나 채널을 확인할 수 없는 오래된 파일이면 `-SkipBuild`를 사용해도
번들을 만들지 않는다.

작업 표시줄·창 전환·시작 메뉴에는 Windows 셸이 배율에 맞춰 고르는
`targetsize` 아이콘을 쓴다. 패키징 스크립트는 16~256px의 `unplated` 및
`lightunplated` 자산을 넣고 MakePri로 `resources.pri`를 만든다. 이 파일이 없으면
Windows가 기본 아이콘에 색상 배경을 붙일 수 있으므로 수동 패키징에서도 생략하지
않는다.

실행 중인 GitHub판 Sentory를 트레이에서 완전히 종료한 뒤 관리자 PowerShell에서
다음 명령으로 인증서를 신뢰하고 패키지를 설치한다.

```powershell
.\scripts\Install-MsixTestPackage.ps1
```

`AllowUnsigned`는 실행 파일 활성화를 포함한 이 패키지에 사용할 수 없다. 설치
스크립트는 번들의 서명자와 공개키 인증서가 일치하는지 확인한 뒤 인증서를 로컬
컴퓨터의 `TrustedPeople`에 넣고 설치한다.

검수 순서는 다음과 같다.

1. 기존 GitHub판을 쓰던 PC에서는 같은 보관함과 설정이 그대로 열리는지 확인한다.
   첫 메신저 선택 화면은 새 Windows 계정이나 Windows Sandbox·VM처럼
   `%LOCALAPPDATA%\Sentory`가 없는 환경에서 확인한다.
2. 설정의 앱 정보에 `수동 업데이트 확인`이 없는지 확인한다.
   앱 안에는 제품 버전 `2.0.2`를 표시하고 Windows의 설치된 패키지 정보에는
   MSIX 버전 `2.0.2.0`을 표시한다.
3. 작업 표시줄과 작업 표시줄 우클릭 메뉴의 Sentory 아이콘에 별도 색상 배경이
   붙지 않고 투명하게 표시되는지 확인한다. 밝은 테마와 어두운 테마에서 각각 본다.
4. `Windows 시작 시 실행`을 켠 뒤 작업 관리자의 `시작 앱`에 Sentory가 나타나는지
   확인하고, 다시 끄면 사용 안 함으로 바뀌는지 확인한다.
5. Discord를 처음 켰을 때 한 번만 자동 재시작 동의를 받는지 확인한다. 동의 후
   Discord가 필요한 경우에만 15초 안내 뒤 다시 시작하는지도 확인한다.
6. 링크와 사진을 하나씩 보내 보관함에 저장되는지 확인한다. 웹에서 복사한 글자
   포함 이미지가 OCR 검색으로 찾아지는지도 확인한다.
7. 앱을 제거한 뒤 `%LOCALAPPDATA%\Sentory`의 DB·사진·설정이 남아 있는지 확인한다.
   같은 시험 패키지를 다시 설치해 기존 보관함이 열리는지 확인한다.

시험판 검수를 마치면 Windows 설정의 `설치된 앱`에서 Sentory를 제거한다. 이 작업은
사용자 데이터 폴더를 건드리지 않는다. 이어서 관리자 PowerShell에서 검수 인증서도
지운다.

```powershell
$thumbprint = (Get-Content `
  .\artifacts\store-test\Sentory-TestCertificate.thumbprint.txt `
  -Raw).Trim()
Remove-Item "Cert:\LocalMachine\TrustedPeople\$thumbprint"
Remove-Item "Cert:\CurrentUser\My\$thumbprint"
```

개인키가 든 두 번째 인증서는 시험 번들을 다시 만들 계획이면 검수가 끝날 때까지
남겨도 된다. 외부에 배포하거나 Partner Center에 제출하는 파일에는 이 인증서와
시험 번들을 포함하지 않는다.

## 제출용 x64·ARM64 번들

x64와 ARM64 C++ 빌드 도구가 모두 있는 환경에서는 다음 명령 하나로 Store 채널
실행 파일과 번들을 만든다.

```powershell
.\scripts\Publish-MsixStoreBundle.ps1 -PackageVersion 2.0.2.0
```

기본 출력은 `artifacts/store`이다.

- `Sentory-2.0.2-store.msixbundle`: Partner Center에 올릴 파일
- `Sentory-2.0.2-store.msixbundle.sha256`: 번들 확인값
- `Sentory-2.0.2.0-store-x64.msix`: x64 개별 패키지
- `Sentory-2.0.2.0-store-arm64.msix`: ARM64 개별 패키지
- `msix-package-manifest.json`: identity, 아키텍처, 크기와 SHA-256

Store 제출용 파일은 로컬 검수 인증서로 서명하지 않는다. 인증을 통과한 패키지는
Microsoft Store가 서명한다. `-SignedTest`나 `-UnsignedTest`로 만든 파일은
제출하면 안 된다.

현재 x64 개발 PC에는 Visual C++ ARM64 도구가 없으므로 x64 시험판은 로컬에서
검수하고, 최종 ARM64는 네이티브 Windows ARM 러너에서 같은 커밋과
`MicrosoftStore` 채널로 빌드해야 한다. 최종 번들을 만들기 전 두 실행 파일의 PE
아키텍처와 패키지 매니페스트를 스크립트가 다시 검사한다. 저장소의
`Tauri Microsoft Store MSIX` 워크플로를 수동 실행하면 x64·ARM64 제출용 번들을
하나의 검수 artifact로 받을 수 있으며 GitHub Release에는 자동 게시하지 않는다.

## Partner Center 제출

Packages 화면에는 `Sentory-2.0.2-store.msixbundle` 하나만 올린다. 개별 MSIX,
`.sha256`, JSON manifest, 검수 인증서와 시험판은 제출하지 않는다.

매니페스트는 `runFullTrust` 제한 기능을 사용한다. `제한된 기능` 설명에는 다음
내용을 제품 동작에 맞게 적는다.

> Sentory is a user-controlled desktop utility that detects paste, drop, and send actions
> in supported Windows messenger applications and stores only the links and images the
> user sends. Full-trust access is required for Windows UI Automation, global input event
> observation, clipboard image handling, local OCR, a notification-area process, and
> restarting Discord with an accessibility argument after explicit one-time consent. The
> app does not read message history, collect unrelated clipboard contents, or upload the
> user's library to a Sentory-operated server.

인증 참고 사항에는 첫 실행에서 모든 메신저 감지가 꺼져 있고 검수자가 직접 켤 수
있다는 점, Discord 자동 재시작은 최초 동의 뒤 필요한 경우에만 실행된다는 점,
보관함 데이터는 로컬에 저장된다는 점을 덧붙인다.

WACK은 현재 선택적인 사전 검사 도구이며 유지보수가 중단된 상태다. x64 검수판으로
실행해 보는 것은 유용하지만 최종 판정은 Partner Center 인증 결과를 기준으로 한다.

Microsoft 공식 참고 문서:

- [MSIX 패키지와 로컬 검증](https://learn.microsoft.com/windows/msix/package/packaging-uwp-apps)
- [검수용 자체 서명 인증서 만들기](https://learn.microsoft.com/windows/msix/package/create-certificate-package-signing)
- [SignTool로 MSIX 서명하기](https://learn.microsoft.com/windows/msix/package/sign-app-package-using-signtool)
- [패키징된 데스크톱 앱의 실행과 제거](https://learn.microsoft.com/windows/msix/desktop/desktop-to-uwp-behind-the-scenes)
- [데스크톱 앱의 StartupTask 선언](https://learn.microsoft.com/windows/apps/desktop/modernize/desktop-to-uwp-extensions)
- [제한된 기능 제출 설명](https://learn.microsoft.com/windows/apps/publish/publish-your-app/msix/manage-submission-options)
- [MSIX 인증 절차](https://learn.microsoft.com/windows/apps/publish/publish-your-app/msix/app-certification-process)
