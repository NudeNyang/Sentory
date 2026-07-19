# 공개 배포와 라이선스 운영

## 제품 정보

- 제품명: Sentory
- 제작자·게시자: NudeNyang
- 현재 개발 버전: `0.9.1-beta`
- 라이선스: Sentory Personal Use License 1.0
- 사용 범위: 개인적·비상업적 용도
- 현재 배포 운영체제: Windows 10/11 64비트
- 지원 아키텍처: x64, ARM64

Sentory는 오픈소스 소프트웨어가 아니다. 소스 저장소를 공개하더라도 라이선스가
허용하는 범위를 넘어 수정, 재배포 또는 상업적으로 사용할 수 없다. 현재 권장
운영 방식은 소스 저장소를 비공개로 유지하고 배포 전용 저장소에 바이너리와
문서만 공개하는 것이다.

## 저장소 구성

2026년 7월 20일 기준으로 소스 저장소와 공개 배포 저장소를 분리해 구성했다.

| 구분 | GitHub 저장소 | 로컬 폴더 | 기본 브랜치 | 공개 범위와 역할 |
|---|---|---|---|---|
| 비공개 소스 | `NudeNyang/Sentory-source` | `C:\Users\awds4\Documents\Sentory` | `master` | 전체 소스, 테스트, 빌드 스크립트와 내부 문서 관리 |
| 공개 배포 | `NudeNyang/Sentory` | `C:\Users\awds4\Documents\GitHub\Sentory` | `main` | 사용자용 README, 라이선스 문서와 GitHub Release 제공 |

공개 배포 저장소 주소는 `https://github.com/NudeNyang/Sentory`다. 현재 배포판은
Windows용이지만 향후 지원 메신저를 추가하고 macOS와 Linux로 지원 범위를 넓힐
예정이다. 공개 저장소에는 전체 소스를 복사하지 않고 사용자에게 필요한 문서와
배포 파일만 게시한다.

두 저장소의 커밋은 로컬에서 따로 관리한다. 문서와 배포 파일을 준비한 뒤 실제
GitHub 푸시와 Release 발행은 저장소 소유자가 직접 수행한다.

## 배포 파일

다음 명령 하나로 네 종류의 패키지와 SHA-256 확인값을 만든다.

```powershell
.\scripts\Publish-Release.ps1 -Version 0.9.1-beta
```

| 파일 | 대상 |
|---|---|
| `Sentory-win-x64-setup.exe` | Intel·AMD Windows 설치형 |
| `Sentory-win-x64-portable.zip` | Intel·AMD Windows 포터블 |
| `Sentory-win-arm64-setup.exe` | Windows on ARM 설치형 |
| `Sentory-win-arm64-portable.zip` | Windows on ARM 포터블 |

각 파일 옆에는 `.sha256` 확인값이 생성된다. `release-manifest.json`에는 버전,
크기와 SHA-256이 기록된다. 인앱 업데이트는 GitHub Releases API에서 현재
아키텍처와 설치 방식에 맞는 파일을 선택하고, Release의 SHA-256 digest 또는
함께 올린 `.sha256` 파일로 다운로드 결과를 검증한다.

설치형 패키지는 Inno Setup 6으로 생성한다. 빌드 PC에 컴파일러가 없으면 다음
명령으로 설치한다.

```powershell
winget install --id JRSoftware.InnoSetup -e
```

## 설치 방식

설치형은 관리자 권한 없이 현재 사용자 계정의 다음 위치를 사용한다.

```text
%LOCALAPPDATA%\Programs\Sentory
```

포터블은 사용자가 선택한 폴더에서 실행한다. 두 방식 모두 사용자 데이터는
`%LOCALAPPDATA%\Sentory`에 저장하며 제거 또는 실행 파일 교체 시 보존한다.

## 아키텍처 검증

x64 빌드 PC에서도 ARM64 패키지를 교차 게시할 수 있다. 배포 스크립트는 PE
헤더의 머신 형식을 확인하여 x64와 ARM64 파일이 섞이는 것을 차단한다.

x64 패키지는 빌드 중 격리된 데이터 폴더로 실제 실행 자체 점검을 수행한다.
ARM64 실행 자체 점검과 Discord·카카오톡 통합 검수는 ARM64 Windows 장치에서
별도로 수행해야 한다. 실제 장치 검수가 끝나기 전에는 ARM64 Release 설명에
이 제한을 명시한다.

## 공개 전 점검표

1. 작업 트리가 깨끗한지 확인한다.
2. `scripts\Test-RealUseStability.ps1`로 전체 테스트를 실행한다.
3. 라이트·다크 모드와 네 언어의 설정·라이선스 화면을 확인한다.
4. Discord 링크·사진 전송과 취소, 재시작, 채널 이동을 확인한다.
5. 카카오톡 채팅 입력과 다른 앱 오탐 방지를 확인한다.
6. x64 설치형을 새 사용자 폴더에 설치·실행·제거한다.
7. x64 포터블 ZIP을 새 폴더에서 실행한다.
8. ARM64 실제 장치에서 두 ARM64 패키지를 확인한다.
9. Git 태그와 프로젝트 버전, CHANGELOG가 일치하는지 확인한다.
10. ZIP, 설치 파일과 모든 `.sha256`을 GitHub Release에 올린다.

x64 설치·실행·제거 왕복 검증은 다음 명령으로 반복할 수 있다. 기존 Sentory
설치가 감지되면 안전을 위해 실행하지 않는다.

```powershell
.\scripts\Test-InstallerRoundTrip.ps1
```

## 공개 문서

- `LICENSE.txt`: 개인 사용 라이선스 전체 조건
- `PRIVACY.md`: 로컬 데이터, 네트워크 요청과 삭제 방법
- `THIRD-PARTY-NOTICES.txt`: 포함된 제3자 구성 요소와 라이선스
- `CHANGELOG.md`: 버전별 변경 내용과 알려진 제한 사항
- `SUPPORT.md`: 지원 범위와 안전한 버그 제보 방법
- `distribution/README-KO.txt`: 최종 사용자 설치·사용 안내

현재 베타 산출물은 코드 서명되지 않는다. 따라서 Windows가 알 수 없는 게시자
또는 평판 기반 경고를 표시할 수 있다. 공개 Release에는 SHA-256 확인 방법을
명시하고, 정식 1.0 배포 전에는 실행 파일과 설치 프로그램의 코드 서명을
완료한다.

## 버전과 업데이트 정책

베타는 `0.9.x-beta`, 정식 공개는 `1.0.0`부터 의미적 버전 형식을 사용한다.
`0.9.1-beta`부터 앱 시작 뒤 GitHub Releases를 자동 확인한다. 베타 앱은 새
베타와 정식 버전을 모두 확인하고, 정식 앱은 정식 Release만 확인한다. 확인은
6시간에 한 번으로 제한한다. 사용자가 설치를 승인하면 설치형은 현재
아키텍처의 설치 파일을 실행하고, 포터블은 별도 임시 업데이트 프로세스가 앱
종료 후 파일을 교체하고 다시 실행한다. 사용자 데이터는 설치 폴더 밖에 있어
업데이트 과정에서 유지된다.
