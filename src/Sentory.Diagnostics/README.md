# Sentory.Diagnostics

Discord와 KakaoTalk의 UI Automation 노출 범위를 조사하는 선행 진단 도구다.

진단 호스트는 .NET 8로 실행한다. 실제 UIA 호출은 Windows 10/11에 기본 포함된 Windows PowerShell 5.1 격리 프로세스에서 수행한다.

이유:

- 현재 Discord 1.0.9246에서 .NET 8/10 `UIAutomationClient.FindAll` 직접 호출이 반환되지 않는 현상이 재현됨
- Windows PowerShell 5.1/.NET Framework UIA에서는 같은 호출이 즉시 반환됨
- 프로브를 별도 프로세스로 실행하면 UIA 공급자가 멈춰도 15초 후 프로브만 종료하고 결과를 폐기할 수 있음

이 도구는 기본적으로 다음 데이터를 읽거나 저장하지 않는다.

- 컨트롤 이름 원문
- 입력값과 메시지 본문
- 창 제목
- 파일 전체 경로
- 대화 상대 이름

## 실행

```powershell
dotnet run --project .\src\Sentory.Diagnostics -- list

dotnet run --project .\src\Sentory.Diagnostics -- snapshot `
  --process Discord `
  --view raw `
  --output .\artifacts\diagnostics\discord-raw.json

dotnet run --project .\src\Sentory.Diagnostics -- watch `
  --process KakaoTalk `
  --seconds 15 `
  --output .\artifacts\diagnostics\kakao-watch.json
```

`watch`는 UIA 구조, 포커스, 활성 상태 변화만 기록한다. 키 전체나 텍스트 값은 기록하지 않는다.

현재 `watch` 명령은 정적 트리 타당성 확인 이후 구현하도록 잠겨 있다.
