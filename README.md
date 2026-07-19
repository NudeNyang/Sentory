# Sentory

Sentory는 메신저에서 다룬 URL과 사진을 로컬에 보관하는 데스크톱
앱이다. 현재 제공되는 실행 앱과 카카오톡 감지는 Windows용이며,
핵심 데이터·저장소 계층은 macOS와 Linux 이식을 고려해 분리돼 있다.

현재 제품 런타임에서 활성화된 범위:

- Discord 채팅 입력창의 Ctrl+V URL과 실제 전송 확인
- Discord 채팅 입력창의 Ctrl+V 사진과 실제 전송 확인
- Discord는 붙여넣기만 하거나 첨부를 취소하면 저장하지 않음
- Discord 확인 워커를 미리 준비하고 안전한 채팅 대상 캐시를 재사용
- KakaoTalk 개별 채팅 입력창의 Ctrl+V URL
- KakaoTalk 개별 채팅 입력창의 Ctrl+V 사진
- 전송 여부는 관찰하지 않고 `입력 시 저장됨`으로 기록
- 다른 앱과 KakaoTalk 검색창에서는 저장하지 않음
- 카드형 갤러리에서 URL과 사진 확인
- 카드 미리보기 영역을 누르면 URL이나 원본 사진을 바로 열기
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
- 한국어 기본 및 영어·일본어·중국어 화면 언어 전환
- 테마·언어·자동 정리 선택값을 설정 화면에 명확하게 표시
- 테마와 갤러리 창 위치·크기 자동 복원
- 트레이 메뉴에서 Windows 자동 실행 설정
- 트레이 메뉴에서 감지 일시정지 상태 확인
- Discord가 꺼져 있으면 접근성 모드로 자동 시작
- Discord 연결 준비 안내와 보관함의 `지금 적용` 버튼
- 라이트·다크 테마를 따르는 Sentory 전용 트레이 빠른 설정 메뉴

## 실행

```powershell
dotnet run --project .\src\Sentory.App
```

실행 후 작업 표시줄 알림 영역의 Sentory 아이콘을 우클릭하면 보관함,
감지 일시정지, Windows 자동 실행, Discord 자동 연결과 데이터 폴더를
한 곳에서 관리할 수 있다. 트레이 아이콘을 더블클릭해도 보관함이 열린다.

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

다른 Windows PC에 폴더째 전달할 self-contained `win-x64` 무설치판은
다음 명령으로 만든다.

```powershell
.\scripts\Publish-Portable.ps1
```

스크립트는 실행 파일의 저장소 초기화 자체 점검을 거친 뒤
`artifacts\Sentory-win-x64-portable` 폴더, ZIP 파일과 SHA-256 확인값을
만든다. 받는 사람은 ZIP을 완전히 푼 뒤 `Sentory.App.exe`를 실행하면 된다.

세부 구현 범위와 제한 사항은 [PROJECT.md](./PROJECT.md)와
[KakaoTalk 구현 결과](./docs/02-kakao-immediate-capture-implementation.md),
[크로스 플랫폼 및 배포 준비](./docs/03-cross-platform-and-distribution-readiness.md)를
참고하면 된다.
