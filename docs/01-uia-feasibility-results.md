# Sentory UI Automation 1차 타당성 조사 결과

작성일: 2026-07-16

## 결론 요약

전체 구현 가능성은 아직 확정하지 않는다.

- Discord: 현재 실행 상태에서는 핵심 접근성 트리가 노출되지 않아 `DetectionUnavailable`
- KakaoTalk: 기본 창은 일부 UIA 정보가 노출되지만 실제 채팅창이 열려 있지 않아 채팅 전송 감지는 `NotYetTested`
- 실제 메시지 전송, 첨부 업로드, 취소, 실패 시나리오는 아직 수행하지 않음
- 이번 조사 중 외부 상대에게 메시지를 보내거나 파일을 전송하지 않음

## 조사 환경

- Windows 11 x64, 빌드 `10.0.26100`
- Discord 1.0.9247
- KakaoTalk 26.6.0.5208
- .NET 8 SDK 8.0.423
- Windows PowerShell 5.1 / .NET Framework 4.8.9336

## UIA 런타임 호환성 조사

### 재현 결과

Discord 주 창에서 동일한 `AutomationElement.FindAll(TreeScope.Descendants, ...)` 호출을 비교했다.

| 실행 환경 | 결과 |
|---|---|
| .NET 8 WindowsDesktop UIA | 반환되지 않음 |
| .NET 10 WindowsDesktop UIA | 반환되지 않음 |
| .NET 10.0.9 self-contained UIA | 반환되지 않음 |
| Windows PowerShell 7.6 / UIA 10.0.9 | 약 20ms, 하위 요소 2개 |
| Windows PowerShell 5.1 / .NET Framework UIA | 약 11ms, 하위 요소 2개 |

단순 타깃 프레임워크나 UIA 패치 버전 문제가 아니라 호스트 실행 환경과 Electron UIA 공급자 사이의 호환성 문제로 판단한다.

### 채택한 진단 구조

- .NET 8 `Sentory.Diagnostics`
  - CLI
  - 프로브 실행
  - 15초 타임아웃
  - 프로브 프로세스 강제 종료
  - JSON 검증 및 저장
- Windows PowerShell 5.1 격리 UIA 프로브
  - UIA 트리 조회
  - 컨트롤 유형과 지원 패턴 수집
  - 민감한 원문 비수집

이 구조는 UIA 호출이 멈춘 경우 결과를 commit하지 않는 fail-closed 정책과 맞는다.

최종 제품에서도 동일한 외부 프로세스 구조를 그대로 사용할지는 동적 이벤트 검증 후 결정한다. 장기 실행 성능이 충분하지 않으면 COM 기반 전용 워커 또는 다른 공개 접근성 API를 추가 조사한다.

## 개인정보 보호 확인

현재 프로브 결과에는 다음 정보가 포함되지 않는다.

- 컨트롤 `Name` 원문
- `ValuePattern` 값
- `TextPattern` 본문
- 창 제목 원문
- 메시지 본문
- 대화 상대 이름
- 전체 실행 파일 경로
- 파일 원본 경로

이름과 창 제목은 정확한 길이 대신 범위만 기록한다. Runtime ID는 SHA-256 일부 값으로 변환한다. AutomationId, ClassName, FrameworkId는 제한된 식별자 문자만 허용하고 나머지는 `<redacted>` 처리한다.

## Discord 정적 조사

### UIA 결과

Raw, Control, Content View가 모두 같은 결과를 보였다.

- 총 요소: 3개
- `ControlType.Window`: 1개
- `ControlType.Pane`: 2개
- 포커스 가능한 요소: 0개
- 확인된 클래스:
  - `Chrome_WidgetWin_1`
  - `Chrome_RenderWidgetHostHWND`
  - 식별자 규칙에 맞지 않아 redacted된 D3D 중간 창
- 확인된 패턴:
  - `WindowPattern`
  - `TransformPattern`

### 현재 판정

다음 항목은 현재 실행 상태에서 모두 식별 불가다.

- 메시지 입력창
- 입력 상태
- 첨부 미리보기
- 전송 버튼
- 메시지 목록
- 현재 사용자 발신 메시지
- 첨부 파일 정보
- 전송 실패 상태

따라서 현재 Discord 어댑터가 저장을 수행하면 안 된다.

### 접근성 플래그와 MSAA 결과

Discord를 Squirrel 실행 경로로 다음 인자를 전달해 재시작했다.

```text
Update.exe --processStart Discord.exe --process-start-args --force-renderer-accessibility
```

주 프로세스 명령줄에서 플래그가 실제로 전달된 것을 확인했다.

플래그 적용 후에도 UIA Raw/Control/Content View는 여전히 창 1개와 Pane 2개만 노출했다.

그러나 같은 Chromium 렌더러 HWND를 MSAA `IAccessible`로 조사하자 다음 구조가 확인됐다.

- 노드 수: 약 1,700개
- 최대 깊이: 23
- 주요 역할:
  - Document
  - Grouping
  - List
  - ListItem
  - OutlineItem
  - Link
  - StaticText
  - Text
  - PushButton
  - CheckButton

따라서 Discord는 UIA 전용 어댑터가 아니라 격리된 MSAA 어댑터를 사용하면 타당성이 있다.

### Discord URL 동적 검증

재시작 후 사용자가 비공개 테스트 채널을 열고 고유한 `example.com` 테스트 URL을 사용했다.

메시지 입력:

- MSAA `ROLE_SYSTEM_TEXT` 후보가 정확히 1개
- 빈 contenteditable 값 길이: 2
- Ctrl+V 후 값 길이: 48
- 입력값은 고정 테스트 URL과 정확히 일치
- 입력 포커스와 Discord 전경 창 일치 확인

메시지 목록:

- MSAA `ROLE_SYSTEM_LIST` 후보 중 상태와 구조로 메시지 목록 식별
- 전송 전 ChildCount: 39
- 전송 전 직접 ListItem: 21
- 화면 내 ListItem: 6

붙여넣기만 한 상태:

- 메시지 목록 ChildCount: 39 유지
- 직접 ListItem: 21 유지
- Confirmed 생성 안 함

붙여넣은 뒤 삭제:

- 입력값: 테스트 URL에서 빈 상태로 변경
- 메시지 목록 ChildCount: 41 유지
- 최신 메시지 항목에서 삭제 테스트 URL 불일치
- Confirmed 생성 안 함

Shift+Enter:

- 입력값 길이만 증가
- 메시지 목록 ChildCount: 41 유지
- 최신 메시지 항목에서 Shift+Enter 테스트 URL 불일치
- Confirmed 생성 안 함

실제 Enter 전송:

- 입력값: 48에서 빈 contenteditable 상태 2로 변경
- 메시지 목록 ChildCount: 39에서 41로 증가
- 직접 ListItem: 21에서 22로 증가
- 화면 내 ListItem: 6에서 7로 증가
- 새로 추가된 마지막 ListItem 상태: 화면 내 항목
- 마지막 ListItem 하위 노드만 최소 범위로 검사
- 테스트 URL이 새 항목 Name/Value에서 정확히 일치
- WinEventHook에서 Chromium 렌더러의 SHOW/HIDE/REORDER 계열 변화 확인

과거 메시지 본문은 읽지 않았다. 새로 추가된 마지막 ListItem 하위 트리만 호출자가 제공한 테스트 URL과 비교했다.

### Discord 현재 판정

Discord URL 전송은 다음 신호를 모두 요구하면 `Confirmed` 판정이 가능하다.

1. 같은 대화방의 Pending URL 후보가 존재
2. 입력 MSAA 값이 후보 URL과 일치
3. 전송 전에는 메시지 목록이 증가하지 않음
4. 전송 시도 후 입력값이 빈 contenteditable 상태로 전환
5. 메시지 목록에 새 직접 ListItem이 증가
6. 새 마지막 ListItem이 화면 내 메시지 상태
7. 새 항목의 최소 하위 트리에 후보 URL이 정확히 존재
8. correlation ID가 아직 commit되지 않음

삭제와 Shift+Enter는 위 조건을 충족하지 않으므로 Confirmed로 전환되지 않는다.

현재 타당성이 확인된 범위는 Discord의 URL 붙여넣기 전송이다.

### Discord 1.0.9248 실앱 재검증

2026-07-18에 Discord 1.0.9248과 Sentory 실앱을 사용해 비공개 채널에서
고유한 `example.com` URL을 붙여넣고 전송했다.

- 접근성 플래그 없이 실행한 Discord에서는 캡처 이벤트가 생성되지 않았다.
- `--force-renderer-accessibility`로 다시 시작한 뒤에는 확인 워커가 생성됐다.
- 입력창 URL 일치, 입력창 비움, 직접 메시지 항목 증가, 최신 메시지 URL
  일치를 모두 통과했다.
- `capture_events`에 `DiscordConfirmedSend`, `Confirmed` 기록이 생성됐다.

Discord 재시작은 작성 중인 메시지와 통화에 영향을 줄 수 있으므로 자동으로
수행하지 않는다. Sentory 트레이 메뉴의 `Discord 연결 복구`에서 사용자 확인을
받은 뒤 접근성 플래그를 포함해 Discord를 다시 시작하도록 보완했다.

아직 검증되지 않은 범위:

- 전송 버튼 클릭
- 한글 IME 조합 중 Enter
- 네트워크 실패와 재시도
- 이미지 붙여넣기
- 파일 및 이미지 드래그 앤 드롭
- 복수 첨부
- 첨부 취소와 업로드 실패
- 접근성 플래그 없이 MSAA 트리 유지 여부

진단 스크립트는 매 실행마다 전체 트리를 수집해 수 초가 걸린다. 제품 구현에서는 장기 실행 격리 워커, WinEventHook 트리거, 대상 List/Input 부분 탐색으로 최적화해야 한다. 메시지 전송 자체는 기다리지 않고 확인과 저장을 비동기로 수행한다.

### 다음 Discord 검증

1. 사용자가 Discord 비공개 테스트 채널을 다시 연 상태에서 MSAA 트리 수집
2. 메시지 입력 역할과 메시지 목록 역할의 안정적인 경로 추출
3. 붙여넣기 전/후 입력 노드 변화 비교
4. Enter 전/후 새 발신 ListItem/OutlineItem 생성 비교
5. 발신 메시지와 상대 메시지를 구분할 상태/구조 조사
6. 첨부 미리보기와 실패/재시도 노드 조사
7. 접근성 플래그 없이도 MSAA 트리가 유지되는지 별도 비교

Discord 재시작은 사용자의 작성 중 메시지나 통화에 영향을 줄 수 있으므로 자동으로 수행하지 않았다.

## KakaoTalk 정적 및 동적 조사

### 현재 결과

현재 기본 창에서 Raw, Control, Content View가 같은 결과를 보였다.

- 총 요소: 31개
- `ControlType.Window`: 1개
- `ControlType.Pane`: 25개
- `ControlType.Document`: 1개
- `ControlType.Text`: 1개
- `ControlType.Image`: 2개
- `ControlType.Hyperlink`: 1개
- 포커스 가능한 요소: 3개
- 확인된 패턴:
  - `WindowPattern`
  - `TransformPattern`
  - `TextPattern`
  - `ValuePattern`
  - `InvokePattern`
  - `ScrollPattern`
  - `ScrollItemPattern`
  - `ItemContainerPattern`

현재 구조에는 KakaoTalk 네이티브 영역과 임베디드 Chromium 문서 영역이 함께 존재한다.

### 채팅창 정적 결과

`나와의 채팅` 창은 별도 HWND로 열렸다.

- 채팅 입력 후보:
  - 클래스: `RICHEDIT50W`
  - AutomationId: `1006`
  - `TextPattern` 지원
  - `ValuePattern` 지원
- 메시지 목록 후보:
  - 클래스: `EVA_VH_ListControl_Dblclk`
  - AutomationId: `100`
  - UIA 자식과 패턴 없음

MSAA로 메시지 목록을 재귀 조사한 결과:

- 노드 수: 24개
- 최대 깊이: 3
- 창 장식, 스크롤바, 버튼 역할은 노출됨
- 개별 메시지 본문 또는 발신 메시지 항목은 노출되지 않음

### 붙여넣기 검증

고유한 `example.com` 테스트 URL을 사용했다.

1. 입력창을 비운 상태에서는 TextPattern 본문 길이가 0
2. Ctrl+V 후 TextPattern 길이가 테스트 URL 길이와 일치
3. ValuePattern 값도 끝의 제어문자를 제외하면 테스트 URL과 정확히 일치
4. 붙여넣기만 한 상태에서는 어떤 저장 확정도 수행하지 않음
5. 테스트 과정에서 원문 입력값은 로그에 저장하지 않고 일치 여부만 기록

KakaoTalk 입력창은 Win32 `GetWindowTextLength`에서는 항상 0을 반환하지만 UIA Text/Value Pattern으로는 입력 상태를 확인할 수 있다.

### 전송 검증

`나와의 채팅`에 고유 테스트 URL을 실제로 전송했다.

Enter 후:

- 테스트 URL은 입력 패턴에서 사라짐
- 입력창은 `메시지 입력`으로 추정되는 placeholder 형태로 돌아옴
- 메시지 목록 MSAA 노드 수: 전송 전후 24개
- 메시지 목록 MSAA 구조 서명: 전송 전후 동일

`SetWinEventHook`도 비교했다.

- 붙여넣기 시 입력 HWND에서 SHOW/HIDE/CREATE/DESTROY/TEXT 관련 이벤트 발생
- 전송 시 입력 HWND에서 다수의 상태 및 텍스트 이벤트 발생
- 메시지 목록 HWND에서는 새 메시지 생성으로 식별할 SHOW/REORDER/NAMECHANGE 이벤트가 발생하지 않음

### 현재 판정

다음 항목은 가능하다.

- 채팅 입력창 식별
- 붙여넣은 URL 존재와 정확한 일치 확인
- 입력 삭제 및 입력 상태 변화 확인
- Enter 후 입력 후보가 사라지는 상태 확인

다음 핵심 항목은 불가능하다.

- 새로 생성된 내 발신 메시지 식별
- 상대 메시지와 내 메시지 구분
- 전송 실패와 실제 성공 구분
- 입력 초기화가 전송 성공 때문인지 다른 UI 동작 때문인지 확정

따라서 현재 공개 UIA/MSAA/WinEvent API 범위에서는 KakaoTalk `Confirmed` 상태를 만들 수 없다.

fail-closed 원칙에 따라 KakaoTalk 자동 DB commit은 현재 지원 불가로 판정한다.

KakaoTalk를 다시 지원 대상으로 검토하려면 다음 중 하나가 필요하다.

- 카카오가 제공하는 공식 전송 완료 이벤트/API
- 향후 KakaoTalk 접근성 트리 개선
- 개별 발신 메시지를 노출하는 공개 MSAA/UIA 공급자

프로세스 주입, 패킷 감청, 클라이언트 변조는 대안으로 사용하지 않는다.

## 드래그 앤 드롭

아직 검증하지 않았다.

UIA 첨부 미리보기가 식별된 이후에만 Explorer 원본 파일과의 정확한 상관관계를 조사한다. 원본 경로나 데이터 객체를 정확히 얻지 못하는 경우 추측하지 않고 미지원 처리한다.

## 현재 게이트 상태

| 게이트 | Discord | KakaoTalk |
|---|---|---|
| A. 핵심 트리 가시성 | URL 경로 통과 | 입력 통과, 메시지 목록 실패 |
| B. 내 발신 메시지 구분 | 새 항목과 URL 일치로 통과 | 실패 |
| C. 콘텐츠 상관관계 | URL 통과 | 입력만 가능 |
| D. 실패/취소 구분 | 삭제/Shift+Enter 통과, 전송 실패 미검증 | 실패 |
| E. 성능/안정성 | UIA 실패, 격리 MSAA 성공 | 격리 프로브 성공 |

## 다음 진행에 필요한 화면 상태

Discord URL 경로 타당성 검증은 완료됐다.

KakaoTalk 검증도 현재 공개 API 범위에서 완료됐으며 자동 저장은 지원 불가로 판정했다.

다음 구현 단계로 이동하려면 KakaoTalk 어댑터를 `DetectionUnavailable` 상태로 남기고 Discord 우선 MVP로 범위를 조정해야 한다.
