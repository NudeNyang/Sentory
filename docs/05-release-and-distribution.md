# 공개 배포와 라이선스 운영

## 제품 정보

- 제품명: Sentory
- 제작자·게시자: NudeNyang
- 현재 배포 버전: `1.1.3`
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
.\scripts\Publish-Release.ps1 -Version 1.1.3
```

`Publish-Release.ps1`은 내부적으로 `SentoryBuildFlavor=Public`을 명시한다. 공개
배포 실행 파일의 제품 버전에 `+developers`가 포함되면 작업을 중단하므로,
설정 화면에 `for Developers`가 표시되는 검수판이 설치형이나 포터블 배포에
들어가지 않는다.

로컬 검수판은 다음 명령으로 만든다. `Publish-Portable.ps1`의 기본 빌드 구분은
`Developer`이며, x64 검수판만 저장소 루트의 `Sentory.exe`를 교체한다.

```powershell
.\scripts\Publish-Portable.ps1 -Runtime win-x64 -BuildFlavor Developer
```

공개판을 수동으로 만들 때도 `-BuildFlavor Public`을 반드시 명시한다. 정식
릴리스에서는 수동 호출 대신 `Publish-Release.ps1`을 사용한다.

`artifacts` 폴더에는 다음 파일이 생성됩니다.

| 파일 | 대상 |
|---|---|
| `Sentory-win-x64-setup.exe` | Intel·AMD Windows 설치형 |
| `Sentory-win-x64-portable.zip` | Intel·AMD Windows 포터블 |
| `Sentory-win-arm64-setup.exe` | Windows on ARM 설치형 |
| `Sentory-win-arm64-portable.zip` | Windows on ARM 포터블 |
| `Sentory-1.1.3-source.zip` | 해당 바이너리에 대응하는 전체 소스 |

각 배포 파일의 `.sha256` 확인값과 `release-manifest.json`도 함께
생성됩니다. 인앱 업데이트는 GitHub Releases API에서 현재 아키텍처와 설치
방식에 맞는 파일을 먼저 내려받고 SHA-256을 확인합니다. 검증을 마친 뒤에만
안내창과 수동 설치 버튼을 표시합니다.

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

## 공개 저장소 갱신

공개 저장소에는 최소한 다음 항목을 같은 버전으로 올립니다.

- 전체 소스 코드와 `v1.1.3` 태그
- `LICENSE.txt`의 GNU GPL v3 전문
- `README.md`, `docs/README.en.md`
- `docs/privacy.md`, `distribution/THIRD-PARTY-NOTICES.txt`, `CHANGELOG.md`,
  `docs/support.md`
- Release의 네 가지 Windows 패키지와 소스 ZIP
- 각 배포 파일의 `.sha256` 및 `release-manifest.json`

GitHub가 자동으로 제공하는 “Source code” 파일만 이용해도 소스를 받을 수
있지만, 바이너리와 정확히 같은 커밋임을 분명히 하기 위해 배포 스크립트가 만든
소스 ZIP도 Release 자산으로 올립니다.

## 공개 전 점검

1. 프로젝트, 설치 프로그램, README와 CHANGELOG의 버전이 모두 같은지 확인합니다.
2. `scripts\Test-RealUseStability.ps1`로 자동 테스트를 실행합니다.
3. 라이트·다크 테마와 네 언어의 설정·라이선스 화면을 확인합니다.
4. Discord의 링크·사진 전송, 취소, 재시작과 채널 이동을 확인합니다.
5. 카카오톡의 링크·사진 붙여넣기와 탐색기 드롭을 확인합니다.
6. 다른 앱에서 다룬 내용이 저장되지 않는지 확인합니다.
7. x64 설치형과 포터블을 별도 데이터 폴더에서 실행합니다.
8. ARM64 패키지는 가능하면 실제 Windows on ARM 장치에서 확인합니다.
9. `v1.1.3` 태그가 배포 파일을 만든 커밋을 가리키는지 확인합니다.
10. Release 자산의 SHA-256 값과 `release-manifest.json`을 대조합니다.

기존 설치본이 없는 환경에서는 아래 명령으로 x64 설치·실행·제거 과정을
자동으로 확인할 수 있습니다.

```powershell
.\scripts\Test-InstallerRoundTrip.ps1
```

기존 설치본과 충돌하지 않는 별도 AppId와 데이터 폴더로 무인 업데이트와 재실행을
확인하려면 아래 명령을 실행합니다.

```powershell
.\scripts\Test-UpdateInstaller.ps1
```

## 문서와 라이선스 파일

- `LICENSE.txt`: GNU GPL v3 전체 내용
- `README.md`: 소개, 다운로드와 소스 빌드 방법
- `docs/privacy.md`: 로컬 데이터, 네트워크 요청과 삭제 방법
- `distribution/THIRD-PARTY-NOTICES.txt`: 제3자 구성 요소와 라이선스
- `CHANGELOG.md`: 버전별 변경 내용과 알려진 제한 사항
- `docs/support.md`: 지원 범위와 안전한 버그 제보 방법
- `distribution/README-KO.txt`: 최종 사용자 설치·사용 안내

현재 실행 파일과 설치 프로그램에는 코드 서명이 적용되지 않았습니다. Windows가
알 수 없는 게시자 또는 평판 기반 경고를 표시할 수 있으므로, 공개 Release에는
공식 저장소 주소와 SHA-256 확인 방법을 함께 안내합니다.

## 버전과 업데이트

Sentory는 의미적 버전 형식을 사용합니다. 앱은 시작 후 GitHub Releases에서 새
버전을 확인하며, 같은 채널의 더 높은 버전이 있을 때만 업데이트를 준비합니다.
확인은 6시간에 한 번으로 제한합니다. 파일 다운로드와 SHA-256 검증이 끝나면
짧은 안내창과 보관함의 수동 설치 버튼을 표시합니다. 이미 검증된 파일이 있으면
다시 내려받지 않습니다.

설치형은 Sentory가 종료되는 동안 설치 파일을 무인 실행하고 완료 뒤 앱을 다시
엽니다. 처음 설치할 때만 Sentory 색상과 전용 그림을 적용한 설치 마법사가
나타납니다. 포터블은 앱이 종료된 뒤 임시 업데이트 프로세스가 파일을 교체합니다.
사용자 데이터는 설치 폴더 밖에 있으므로 두 방식 모두 업데이트 중에 유지됩니다.
