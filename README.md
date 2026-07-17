# Sentory

Sentory는 메신저에서 다룬 URL과 사진을 로컬에 보관하는 데스크톱
앱이다. 현재 제공되는 실행 앱과 카카오톡 감지는 Windows용이며,
핵심 데이터·저장소 계층은 macOS와 Linux 이식을 고려해 분리돼 있다.

현재 제품 런타임에서 활성화된 범위:

- KakaoTalk 개별 채팅 입력창의 Ctrl+V URL
- KakaoTalk 개별 채팅 입력창의 Ctrl+V 사진
- 전송 여부는 관찰하지 않고 `입력 시 저장됨`으로 기록
- 다른 앱과 KakaoTalk 검색창에서는 저장하지 않음
- 카드형 갤러리에서 URL과 사진 확인
- 카드 복사 버튼 또는 우클릭으로 URL·사진 복사
- 검색, 필터, 원본 열기, 항목 삭제
- 최신순, 오래된순, 많이 저장한 순, 이름순 정렬
- 많이 복사한 순, 최근 복사한 순 정렬
- 전체 기간, 오늘, 최근 7일, 최근 30일 필터
- 마지막 정렬 방식 자동 복원
- 즐겨찾기 추가와 즐겨찾기만 보기
- 복사 횟수와 마지막 복사 시간 기록
- 따뜻한 베이지 라이트 기본 테마
- 상단 테마 버튼으로 다크 테마 전환
- 테마와 갤러리 창 위치·크기 자동 복원
- 트레이 메뉴에서 Windows 자동 실행 설정
- 트레이 메뉴에서 감지 일시정지 상태 확인

## 실행

```powershell
dotnet run --project .\src\Sentory.App
```

실행 후 작업 표시줄 알림 영역의 Sentory 아이콘에서 갤러리를 열거나,
감지를 일시정지하고 데이터 폴더를 열 수 있다. 트레이 아이콘을
더블클릭해도 갤러리가 열린다.

개발 중 갤러리를 바로 열려면 다음처럼 실행할 수 있다.

```powershell
dotnet run --project .\src\Sentory.App -- --gallery
```

기본 데이터 위치:

```text
%LOCALAPPDATA%\Sentory
```

설치 폴더와 데이터 폴더는 분리된다. 앱을 다른 위치로 옮기거나
업데이트해도 기존 데이터는 사용자 데이터 폴더에 남는다.

## 빌드와 테스트

```powershell
dotnet build .\Sentory.sln --configuration Release
dotnet test .\Sentory.sln --configuration Release
```

다른 Windows PC에 전달할 self-contained `win-x64` 빌드는 다음
명령으로 만든다.

```powershell
dotnet publish .\src\Sentory.App /p:PublishProfile=win-x64
```

세부 구현 범위와 제한 사항은 [PROJECT.md](./PROJECT.md)와
[KakaoTalk 구현 결과](./docs/02-kakao-immediate-capture-implementation.md),
[크로스 플랫폼 및 배포 준비](./docs/03-cross-platform-and-distribution-readiness.md)를
참고하면 된다.
