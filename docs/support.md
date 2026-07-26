# Sentory 지원 정책

Sentory는 개인 프로젝트로 운영되므로 정해진 답변 시간이나 지속적인 지원을
보장하지 않습니다.

## 버그를 제보할 때 필요한 정보

- Sentory 버전과 x64·ARM64 구분
- Windows 버전
- 문제가 발생한 메신저와 재현 순서
- 기대한 결과와 실제 결과
- `%LOCALAPPDATA%\Sentory\logs`의 관련 진단 기록

진단 기록을 공유하기 전에는 개인 정보가 포함되어 있지 않은지 직접 확인해
주세요. 채팅 원문, 사진, 계정 정보와 비공개 URL은 공개 이슈에 첨부하지 마세요.

## 지원 범위

- Windows 10/11 64비트
- Discord 데스크톱 앱
- Slack 데스크톱 앱
- Windows 카카오톡 데스크톱 앱

메신저 업데이트로 화면 또는 접근성 구조가 변경되면 감지가 일시적으로 중단될
수 있다. macOS와 Linux 앱은 현재 제공하지 않는다.

## 기능 요청과 라이선스 문의

버그 제보, 기능 요청과 GPL 적용에 관한 문의는 공식 GitHub 저장소에서 받습니다.
주소는 `https://github.com/NudeNyang/Sentory`입니다.

## 진단 로그 보내기

문제가 발생한 PC에서는 다음 파일 하나만 보내면 됩니다.

```text
%LOCALAPPDATA%\Sentory\logs\sentory.log
```

설정의 `데이터 폴더 열기`를 누른 뒤 `logs` 폴더에서 `sentory.log`를 찾을 수
있습니다. 이 파일에는 앱 동작과 Discord·Slack 접근성 확인 단계가 함께
기록됩니다. URL 원문, 사진 내용, 메시지 본문과 상대 이름은 기록하지 않습니다.

이전 버전의 `sentory.previous.log`와 `diagnostics\discord-capture.log`가 남아
있으면 새 버전 첫 실행 때 `sentory.log`로 합친 뒤 기존 파일을 정리합니다.
