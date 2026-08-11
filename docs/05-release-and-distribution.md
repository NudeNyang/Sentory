# 공개 배포와 라이선스 운영

## 제품 정보

- 제품명: Sentory
- 제작자·게시자: NudeNyang
- 현재 배포 버전: `2.0.8`
- 라이선스: GNU General Public License v3.0 only (`GPL-3.0-only`)
- 현재 배포 운영체제: Windows 10/11 64비트
- 지원 아키텍처: x64, ARM64
- 공식 저장소: `https://github.com/NudeNyang/Sentory`

Sentory의 원본 소스 코드는 GPL-3.0-only로 공개합니다. 사용, 연구, 수정,
재배포와 상업적 이용이 가능하지만, 바이너리나 수정본을 배포할 때는 GPL이
요구하는 해당 소스 코드와 라이선스 고지를 함께 제공해야 합니다. 포함된 제3자
구성 요소에는 각 구성 요소의 라이선스가 적용됩니다.

기존 `NudeNyang/Sentory-source` 저장소는 개발 이력을 보관하는 작업용
저장소로 남길 수 있습니다. 공개 Release와 같은 버전의 소스는 공식 공개
저장소의 Git 태그와 `Sentory-<버전>-source.zip`에서 누구나 받을 수 있어야
합니다.

## 배포 파일 만들기

먼저 버전 변경과 문서 수정을 커밋하고 작업 트리가 깨끗한지 확인합니다. 배포
스크립트는 현재 커밋을 기준으로 소스 ZIP을 만들기 때문에 커밋하지 않은 변경
사항이 있으면 중단됩니다.

```powershell
git status --short
.\scripts\Publish-TauriRelease.ps1 -Version 2.0.8 -Architecture x64
.\scripts\Publish-TauriRelease.ps1 -Version 2.0.8 -Architecture arm64
```

`Publish-TauriRelease.ps1`은 제품명과 버전, 앱 식별자를 확인하고 `for
Developers`, `Preview` 같은 개발자판 표시가 남아 있으면 작업을 중단한다. C#
엔진을 self-contained 사이드카로 만들고 Tauri 호스트와 함께 선택한 아키텍처의
포터블·설치형에 넣는다. 호스트와 엔진의 PE 아키텍처를 확인하고, 같은 아키텍처의
Windows에서는 실행 파일 자체 점검도 마친다.

Tauri 로컬 검수판은 다음 명령으로 만든다.

```powershell
.\scripts\Build-Tauri.ps1 -Configuration Release -Architecture x64
.\scripts\Build-Tauri.ps1 -Configuration Release -Architecture arm64
```

`artifacts` 폴더에는 다음 파일이 생성됩니다.

| 파일 | 대상 |
|---|---|
| `Sentory-win-x64-setup.exe` | Intel·AMD Windows 설치형 |
| `Sentory-win-x64-portable.zip` | Intel·AMD Windows 포터블 |
| `Sentory-win-arm64-setup.exe` | Windows on ARM 설치형 |
| `Sentory-win-arm64-portable.zip` | Windows on ARM 포터블 |
| `Sentory-2.0.8-source.zip` | 해당 바이너리에 대응하는 전체 소스 |

각 배포 파일의 `.sha256` 확인값과 `release-manifest.json`도 함께 생성된다.

GitHub판은 실행 6초 뒤 첫 자동 확인을 시작하고 이후 6시간마다 GitHub Releases를
조회한다. 새 정식 버전이 있으면 현재 아키텍처와 설치 방식에 맞는 파일을 임시
폴더로 내려받고 SHA-256을 검증한다. 설정의 `지금 확인`은 이 간격을 건너뛴다.
준비된 업데이트는 설정에서 설치할 수 있다. 별도 헬퍼가 Tauri 호스트와 C# 엔진의
종료를 확인한 다음 설치형은 Inno Setup으로 교체하고, 포터블은 압축을 푼 파일을
현재 폴더에 덮어쓴다. 두 방식 모두 끝나면 Sentory를 다시 연다. Microsoft
Store판에는 업데이트 행을 표시하지 않으며 Store가 업데이트를 배포한다.

설치형 패키지는 Inno Setup 6으로 만듭니다. 빌드 PC에 컴파일러가 없으면 다음
명령으로 설치할 수 있습니다.

```powershell
winget install --id JRSoftware.InnoSetup -e
```

## 설치 위치와 사용자 데이터

설치형은 관리자 권한 없이 현재 사용자 계정의 다음 위치에 설치됩니다.

```text
%LOCALAPPDATA%\Programs\Sentory
```

포터블은 압축을 푼 폴더에서 실행합니다. 두 방식 모두 사용자 데이터는
`%LOCALAPPDATA%\Sentory`에 저장하며, 프로그램을 제거하거나 실행 파일을
교체해도 자동으로 삭제하지 않습니다.

Microsoft Store판도 보관함 DB, 사진과 설정은 같은 폴더에 저장한다. 패키지 제거와
분리해야 하는 로그, 링크 미리보기와 OCR 모델만 MSIX 패키지의
`LocalState\Sentory`에 둔다. 자세한 제작·검수 절차는
[`12-microsoft-store-msix.md`](./12-microsoft-store-msix.md)에 정리한다.

## 공개 저장소 갱신

공개 저장소에는 최소한 다음 항목을 같은 버전으로 올립니다.

- 전체 소스 코드와 `v2.0.8` 태그
- `LICENSE.txt`의 GNU GPL v3 전문
- `README.md`, `docs/README.en.md`
- `docs/privacy.md`, `distribution/THIRD-PARTY-NOTICES.txt`, `CHANGELOG.md`,
  `docs/support.md`
- Release의 x64·ARM64 설치형·포터블 패키지와 소스 ZIP
- 각 배포 파일의 `.sha256` 및 `release-manifest.json`

GitHub가 자동으로 제공하는 “Source code” 파일만 이용해도 소스를 받을 수
있지만, 바이너리와 정확히 같은 커밋임을 분명히 하기 위해 배포 스크립트가 만든
소스 ZIP도 Release 자산으로 올립니다.

## 공개 전 점검

1. 프로젝트, 설치 프로그램, README와 CHANGELOG의 버전이 모두 같은지 확인합니다.
2. `scripts\Test-RealUseStability.ps1`로 자동 테스트를 실행합니다.
3. 라이트·다크 테마와 네 언어의 설정·라이선스 화면을 확인합니다.
4. Discord의 링크·사진 전송, 취소, 재시작과 채널 이동을 확인합니다.
5. Slack 데스크톱 앱의 링크·사진 붙여넣기 전송, 첨부 취소, 탐색기 드롭과
   대화 이동을 확인합니다.
6. 카카오톡의 링크·사진 붙여넣기와 탐색기 드롭을 확인합니다.
7. 다른 앱에서 다룬 내용이 저장되지 않는지 확인합니다.
8. x64와 ARM64 설치형·포터블을 각각 같은 아키텍처의 Windows에서 실행합니다.
9. 앱 정보에 `for Developers`나 `Preview`가 없는지 확인합니다.
10. `v2.0.8` 태그가 배포 파일을 만든 커밋을 가리키는지 확인합니다.
11. Release 자산의 SHA-256 값과 `release-manifest.json`을 대조합니다.

Tauri 정식판은 `Publish-TauriRelease.ps1`이 포터블 자체 점검을 실행하며
설치형은 별도 환경에서 다음 왕복 검사를 실행한다.

```powershell
.\scripts\Test-InstallerRoundTrip.ps1
```

## 문서와 라이선스 파일

- `LICENSE.txt`: GNU GPL v3 전체 내용
- `README.md`: 소개, 다운로드와 소스 빌드 방법
- `docs/privacy.md`: 로컬 데이터, 네트워크 요청과 삭제 방법
- `distribution/THIRD-PARTY-NOTICES.txt`: 제3자 구성 요소와 라이선스
- `docs/model-provenance.md`: OCR 모델의 고정된 원본, 버전과 SHA-256
- `CHANGELOG.md`: 버전별 변경 내용과 알려진 제한 사항
- `docs/support.md`: 지원 범위와 안전한 버그 제보 방법
- `distribution/README-KO.txt`: 최종 사용자 설치·사용 안내

포터블 압축 파일과 설치판에는 `docs/model-provenance.md`를
`MODEL-PROVENANCE.md`라는 이름으로 포함합니다. 공개 배포 전에 저장소의 OCR
모델과 공식 원본의 SHA-256이 문서에 기록한 값과 같은지 확인합니다.

현재 실행 파일과 설치 프로그램에는 코드 서명이 적용되지 않았습니다. Windows가
알 수 없는 게시자 또는 평판 기반 경고를 표시할 수 있으므로, 공개 Release에는
공식 저장소 주소와 SHA-256 확인 방법을 함께 안내합니다.

코드 서명 인증서와 개인키를 준비하기 전에는 서명을 흉내 내거나 임시 키를 공개
배포에 사용하지 않습니다. 현재 업데이트 신뢰 경계는 GitHub 계정, Release 쓰기
권한과 SHA-256 자산이므로 계정 다중 인증과 최소 권한을 유지해야 합니다.

## SignPath Foundation 전환 준비

SignPath Foundation 오픈소스 코드 서명을 신청하는 동안에는
`.github/workflows/tauri-signpath-candidate.yml`을 수동 실행해 x64와 ARM64의
미서명 후보를 GitHub 호스팅 러너에서 만든다. 이 후보는 GitHub Release에
게시하지 않고 워크플로 artifact로만 보관한다. Foundation 승인 전에는 SignPath
제공을 받았다고 표시하거나 미서명 파일을 서명본으로 안내하지 않는다.

승인 후에는 다음 순서로 전환한다.

1. SignPath GitHub App에 공식 저장소 접근 권한을 부여한다.
2. SignPath 프로젝트에 GitHub.com Trusted Build System과 저장소를 연결한다.
3. x64·ARM64 포터블과 Inno Setup 설치형 샘플로 Artifact Configuration을 만들고,
   Sentory 소유 PE만 서명하며 제3자 구성 요소는 제외한다.
4. 제품명 `Sentory`와 요청한 버전이 PE 메타데이터에 일치하도록 제한한다.
5. `main`과 정식 태그에서 나온 GitHub 호스팅 러너 빌드만 release-signing 정책이
   받도록 제한하고, 매 요청에 수동 승인을 요구한다.
6. CI 제출 토큰은 GitHub Actions Secret에만 저장한다. 조직 ID, 프로젝트 slug,
   정책 slug와 Artifact Configuration slug를 확정한 다음 SignPath 공식 Action을
   후보 빌드와 연결한다.
7. 내부 `Sentory.exe`와 `sentory-engine.exe`를 먼저 서명한 뒤 포터블 ZIP과 Inno
   Setup 설치형을 만들고, 마지막으로 설치형 자체를 서명한다. 최종 파일에서
   SHA-256과 `release-manifest.json`을 다시 만든다.
8. `signtool verify /pa /all /v`로 모든 Sentory 실행 파일과 설치형의 서명, 인증서
   체인과 타임스탬프를 확인한 뒤에만 GitHub Release를 공개한다.

공개 저장소의 역할, 개인정보 안내와 검증 기준은
[`code-signing-policy.md`](./code-signing-policy.md)에 둔다. Foundation 승인이
끝나기 전의 Release와 2.0.8 이하 파일은 미서명 상태로 유지한다.

## 버전과 업데이트

Sentory는 의미적 버전 형식을 사용한다. 2.0.8 Tauri판은 GitHub Releases에서 같은
채널의 더 높은 버전을 자동으로 찾는다. 설치형은 `setup.exe`, 포터블은
`portable.zip`을 고르고 GitHub가 제공한 digest 또는 함께 게시한 `.sha256` 파일로
검증한다. 다운로드 크기는 256MB로 제한하며 검증이 끝난 파일만 설치 헬퍼에
넘긴다. 실패한 확인 시각은 지워 다음 실행에서 다시 시도할 수 있게 한다.

버전 선택과 동기화는 Codex가 맡습니다. 변경 범위에 따라 의미적 버전의 다음
번호를 정하고 프로젝트, 설치 프로그램, 배포 스크립트, 테스트와 문서의 버전을
같이 바꿉니다. 사용자가 버전을 직접 지정한 경우에는 그 값을 우선합니다.

정식 릴리즈 문서의 한국어 문장은 게시 전에 `humanize-korean` 스킬로 윤문합니다.
윤문할 때 사실, 날짜, 버전과 고유명사는 바꾸지 않으며 결과 요약은 Git에 넣지
않는 `_workspace`에 보관합니다. 이 절차를 마친 문서만 GitHub Release 본문으로
사용합니다.

설치형은 기존 1.5.1과 같은 AppId와 설치 위치를 사용한다. 포터블은 앱을 완전히
종료한 뒤 압축을 새 폴더에 풀어 교체한다. 사용자 데이터는 설치 폴더 밖에 있으므로
두 방식 모두 버전을 바꿀 때 유지된다.
