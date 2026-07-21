# 크로스 플랫폼 및 배포 준비

## 현재 결론

Sentory의 현재 실행 앱은 WPF와 Win32 API를 사용하는 Windows 전용
프로그램이다. 이번 구조 정리로 데이터 모델, 저장소, 설정 파일과 핵심
캡처 흐름은 운영체제와 분리됐다.

macOS와 Linux에서 바로 실행되는 앱이 완성된 것은 아니다. 이후에는
공통 코드를 유지한 채 다음 두 부분만 플랫폼별로 구현한다.

1. Avalonia 기반 데스크톱 UI와 트레이 셸
2. 각 운영체제 및 메신저에 맞는 캡처 런타임

## 프로젝트 경계

| 프로젝트 | 대상 | 역할 |
|---|---|---|
| `Sentory.Core` | 모든 OS | 모델, URL 처리, 캡처 조정, 런타임 계약 |
| `Sentory.Infrastructure` | 모든 OS | SQLite, 사진 파일, 사용자 설정 |
| `Sentory.Platform.Windows` | Windows | Win32 훅, 카카오톡 창 검증, WPF 클립보드 |
| `Sentory.App` | Windows | WPF 갤러리, 트레이, Windows 자동 실행 |
| `Sentory.Diagnostics` | Windows | Windows UI 구조 조사 도구 |

공통 프로젝트에서는 `System.Windows`, Windows Forms, 레지스트리,
`user32.dll` 같은 Windows API를 참조하지 않는다.

`ICaptureRuntime`은 앱 셸과 플랫폼 감지기를 연결하는 공통 계약이다.
현재는 `KakaoCaptureRuntime`이 이를 구현한다. 다른 운영체제에서는
동일한 계약을 구현하는 별도 런타임을 추가한다.

## 데이터 위치

데이터베이스, 사진과 설정은 설치 폴더가 아니라 사용자 데이터
디렉터리에 저장한다.

| 운영체제 | 기본 위치 |
|---|---|
| Windows | `%LOCALAPPDATA%\Sentory` |
| macOS | `~/Library/Application Support/Sentory` |
| Linux | `$XDG_DATA_HOME/Sentory` |
| Linux 대체 경로 | `~/.local/share/Sentory` |

폴더 내부 형식은 모든 운영체제에서 동일하다.

- `sentory.db`: SQLite 데이터베이스
- `images/`: PNG 이미지
- `gallery-settings.json`: 테마, 정렬, 창 상태

설치 파일을 삭제하거나 새 버전으로 교체해도 이 폴더는 자동으로
삭제하지 않는다. 제거 프로그램을 만들 때도 사용자에게 별도 동의를
받기 전에는 데이터 폴더를 보존한다.

## Windows 배포

다른 PC에 .NET 런타임을 별도로 설치하지 않아도 실행할 수 있는
`win-x64`와 `win-arm64` self-contained 단일 파일 배포 프로필을 제공한다.
설치 프로그램과 압축을 풀고 실행하는 포터블 폴더를 함께 제공한다.

```powershell
.\scripts\Publish-Release.ps1 -Version 1.4.0
```

결과 위치:

```text
artifacts\Sentory-win-x64-portable.zip
artifacts\Sentory-win-x64-setup.exe
artifacts\Sentory-win-arm64-portable.zip
artifacts\Sentory-win-arm64-setup.exe
artifacts\release-manifest.json
```

배포 스크립트는 실제 배포 실행 파일을 격리된 임시 데이터 폴더에서
실행해 SQLite 데이터베이스, 사진·미리보기·로그 폴더 초기화가 가능한지
확인한다. 자체 점검 데이터는 끝난 뒤 삭제하며 실제 사용자 데이터에는
접근하지 않는다.

받는 사람은 ZIP을 완전히 푼 뒤 `Sentory.exe`를 실행한다. 포터블 업데이트는
Sentory가 종료된 뒤 전용 프로세스가 실행 폴더의 파일을 새 버전으로 교체한다.
설치형 업데이트는 검증된 설치 파일을 무인 실행하고 완료 뒤 Sentory를 다시 연다.
데이터는 `%LOCALAPPDATA%\Sentory`에 있으므로 실행 폴더를 바꿔도 유지된다.

공개 배포 전에는 다음 작업이 추가로 필요하다.

1. 코드 서명 인증서로 실행 파일과 설치 프로그램 서명
2. Windows 10/11과 메신저 지원 버전별 회귀 테스트
3. ARM64 실제 장치에서 ARM64 패키지와 메신저 통합 검수
4. 공식 GitHub 주소와 문의 경로 확정

새 설치는 첫 정상 실행에서 Windows 자동 실행을 기본으로 등록한다. 기존
설치의 설정 파일에 자동 실행 선택이 없으면 현재 레지스트리 상태를 가져와
사용자가 켜거나 꺼 둔 값을 유지한다. 이후 설정 또는 트레이에서 바꾼 값은
설정 파일과 레지스트리에 함께 반영한다. 설치 자체 점검과 Discord 접근성
워커는 자동 실행 상태를 변경하지 않는다.

## macOS·Linux 이식 순서

1. Avalonia 앱 프로젝트를 추가하고 현재 갤러리 화면을 이식
2. 공통 SQLite 저장소와 설정 저장소를 연결
3. 트레이, 원본 열기, 클립보드 복사를 OS 서비스 인터페이스로 분리
4. 각 OS에서 가능한 메신저 감지 방식 조사
5. 오탐 방지 기준을 통과한 플랫폼 어댑터만 활성화
6. macOS 서명·notarization, Linux 패키징을 별도로 구성

카카오톡 감지는 Windows 카카오톡의 HWND 구조와 Win32 API에
의존한다. macOS용 카카오톡은 별도의 조사와 구현이 필요하며, Linux는
공식 카카오톡 데스크톱 앱 제공 여부와 실행 환경에 따라 지원 범위가
달라진다.

## 변경 시 지켜야 할 규칙

- 공통 프로젝트에 OS 전용 API를 추가하지 않는다.
- 새 OS 기능은 별도 `Sentory.Platform.<OS>` 프로젝트에 둔다.
- 데이터베이스 변경은 기존 사용자 데이터를 보존하는 마이그레이션을
  함께 제공한다.
- 저장 파일에는 설치 경로나 특정 사용자 PC의 절대 경로를 기록하지
  않는다.
- 플랫폼 신호가 불확실하면 저장하지 않는 fail-closed 원칙을 유지한다.
