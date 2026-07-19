# Sentory

> 현재 배포 후보 버전: **0.9.0-beta**

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
- 선택 모드의 실시간 드래그 사각형으로 여러 카드를 선택하고 배경 클릭으로 해제
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

실행하면 보관함 창이 화면과 작업 표시줄에 바로 나타난다. 창을 닫은 뒤에는
작업 표시줄 알림 영역의 Sentory 아이콘을 우클릭하면 보관함,
감지 일시정지, Windows 자동 실행, Discord 자동 연결과 데이터 폴더를
한 곳에서 관리할 수 있다. 트레이 아이콘을 더블클릭해도 보관함이 열린다.

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

Windows x64·ARM64용 설치형과 포터블 전체 배포 파일은 다음 명령으로 만든다.

```powershell
.\scripts\Publish-Release.ps1 -Version 0.9.0-beta
```

스크립트는 x64·ARM64 단일 실행 파일, 포터블 ZIP, 설치 프로그램과 SHA-256
확인값을 `artifacts`에 만든다. x64 실행 파일은 격리된 데이터 폴더에서 저장소
초기화 자체 점검까지 수행한다. 자세한 절차는
[공개 배포와 라이선스 운영](./docs/05-release-and-distribution.md)을 참고한다.

## 라이선스와 개인정보

Sentory는 개인적이고 비상업적인 용도로만 사용할 수 있다. NudeNyang의 사전
서면 허가 없이 수정, 역공학, 재배포 또는 상업적으로 이용할 수 없다.

- 전체 사용 조건: [LICENSE.txt](./LICENSE.txt)
- 개인정보 및 로컬 데이터: [PRIVACY.md](./PRIVACY.md)
- 제3자 구성 요소: [THIRD-PARTY-NOTICES.txt](./THIRD-PARTY-NOTICES.txt)
- 변경 기록: [CHANGELOG.md](./CHANGELOG.md)
- 지원 정책: [SUPPORT.md](./SUPPORT.md)

세부 구현 범위와 제한 사항은 [PROJECT.md](./PROJECT.md)와
[KakaoTalk 구현 결과](./docs/02-kakao-immediate-capture-implementation.md),
[크로스 플랫폼 및 배포 준비](./docs/03-cross-platform-and-distribution-readiness.md)를
참고하면 된다.
