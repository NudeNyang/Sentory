# Sentory

[한국어](./README.md) | [English](./docs/README.en.md)

> 붙여넣기한 사진과 링크를 한 곳에서

Sentory는 메신저에서 주고받은 링크와 사진을 따로 모아 두는 데스크톱 앱입니다.
나중에 다시 찾을 때 대화방을 거슬러 올라갈 필요 없이 검색하고, 원본을 열거나
클립보드로 다시 복사할 수 있습니다.

현재 Discord와 카카오톡을 지원합니다. 모든 클립보드 기록을 수집하는 방식은
아닙니다. 지원 메신저의 채팅 입력 영역을 확인한 뒤 정해진 조건에 맞는 링크와
사진만 저장합니다. 다른 메신저도 차례로 지원할 예정입니다.

## 지원 메신저

| 메신저 | 저장 시점 | 저장하지 않는 경우 |
| --- | --- | --- |
| Discord | 붙여넣은 링크나 사진을 실제로 전송한 뒤 | 붙여넣기만 한 경우, `Shift+Enter`, 첨부 취소 |
| 카카오톡 | 개별 채팅창에 링크·사진을 붙여넣거나 탐색기 사진을 드롭했을 때 | 직접 입력한 주소, 검색창 입력, 채팅창 밖의 입력 |

다른 앱에서 복사하거나 붙여넣은 내용은 보관함에 들어가지 않습니다. 메신저별
감지는 설정에서 따로 끄고 켤 수 있습니다.

## 주요 기능

- 링크와 사진을 카드 형태로 보관
- 한 번에 전송한 여러 링크·사진을 묶음 카드로 저장하고 한꺼번에 다시 복사
- 묶음 안의 사진과 링크를 넘겨 보며 개별 복사 또는 원본 열기
- 제목, URL, 도메인과 사진 속 OCR 글자 검색 및 메신저·종류·기간 필터
- PP-OCRv5 모바일 모델을 이용한 다국어 사진의 로컬 자동 이름 생성
- 보관함에 표시된 제목과 같은 파일명으로 사진 열기
- 최신순, 오래된순, 저장 횟수, 복사 횟수 정렬
- 즐겨찾기, 다중 선택, 일괄 삭제와 자동 정리
- 페이지 제목, 사이트 아이콘, 대표 이미지와 설명을 포함한 링크 미리보기
- 라이트·다크 테마와 한국어·영어·일본어·중국어 UI
- 새 설치에서 기본 활성화되는 Windows 시작 시 자동 실행과 트레이 메뉴
- GitHub Releases를 이용한 인앱 자동 확인과 보관함의 수동 업데이트 버튼

## 데이터 저장

사진, 링크, 설정과 사용 기록은 다음 로컬 폴더에 저장됩니다.

```text
%LOCALAPPDATA%\Sentory
```

Sentory가 운영하는 서버로 보관 데이터를 보내지 않으며 분석 도구나 광고 추적기도
넣지 않았습니다. 링크 미리보기를 가져올 때는 해당 웹사이트에 일반적인 네트워크
요청을 보낼 수 있습니다. 사진 OCR은 Windows에서 로컬로 처리되며 인식된 글자는
검색을 위해 Sentory 데이터베이스에 저장됩니다. 자세한 내용은
[개인정보 및 로컬 데이터 안내](./docs/privacy.md)를 확인해 주세요.

## 다운로드

현재 정식 버전은 **1.1.2**입니다. Windows 10/11 64비트에서 사용할 수 있으며,
[Releases](https://github.com/NudeNyang/Sentory/releases)에서 PC에 맞는 파일을
내려받으면 됩니다. macOS와 Linux 버전도 계획하고 있지만 아직 배포 일정은
정해지지 않았습니다.

| 사용 환경 | 설치형 | 포터블 |
| --- | --- | --- |
| 일반적인 Intel·AMD Windows PC | `Sentory-win-x64-setup.exe` | `Sentory-win-x64-portable.zip` |
| Windows on ARM PC | `Sentory-win-arm64-setup.exe` | `Sentory-win-arm64-portable.zip` |

대부분의 PC에서는 x64 설치형을 선택하시면 됩니다. 설치하지 않고 사용하려면
포터블 ZIP을 완전히 푼 뒤 `Sentory.exe`를 실행하세요. 두 방식 모두 .NET을 따로
설치할 필요가 없습니다.

현재 파일에는 코드 서명이 적용되지 않았습니다. Windows에서 알 수 없는 게시자나
SmartScreen 경고가 나타날 수 있습니다. 이 저장소의 공식 Release에서 받은
파일인지 확인하고, 필요하면 함께 제공되는 `.sha256` 파일과 SHA-256 값을
비교해 주세요.

## 처음 사용하기

1. Sentory를 실행합니다. 보관함 창과 작업 표시줄 아이콘이 함께 나타납니다.
2. 카카오톡 개별 채팅창에 링크나 사진을 붙여넣습니다. Discord에서는 붙여넣은
   내용을 전송해야 저장됩니다.
3. 보관함에서 카드를 열어 내용을 확인하거나 다시 복사합니다.

새로 설치한 Sentory는 Windows 자동 실행이 기본으로 켜집니다. 원하지 않으면
설정 화면이나 트레이 메뉴에서 끌 수 있으며, 이후 업데이트에서도 선택한 상태가
유지됩니다.

새 버전 안내를 닫은 뒤에도 보관함 상단의 업데이트 설치 버튼으로 다시 진행할
수 있습니다. 안내창에는 버전과 설치 가능 여부만 짧게 표시됩니다.

Discord를 처음 연결할 때 접근성 모드 적용을 위해 Discord가 다시 시작될 수
있습니다. 연결 상태와 메신저별 감지 설정은 Sentory 설정 화면에서 확인할 수
있습니다.

## 알아둘 점

Discord나 카카오톡의 화면 구조가 바뀌면 감지가 일시적으로 멈출 수 있습니다.
Windows on ARM용 파일은 빌드와 실행 파일 구조 검증을 마쳤지만 실제 ARM64
장치에서의 최종 검수는 남아 있습니다.

문제를 제보할 때는 개인정보가 담긴 대화나 원본 사진 대신 Sentory 버전, Windows
버전, 사용한 메신저와 재현 순서를 적어 주세요. 자세한 내용은
[지원 정책](./docs/support.md)에 정리해 두었습니다.

## 소스 코드와 빌드

Sentory의 소스 코드는 이 저장소에서 공개합니다. 배포 파일과 정확히 대응하는
소스는 각 Release의 `Sentory-<버전>-source.zip` 또는 같은 버전의 Git 태그에서
받을 수 있습니다.

```powershell
git clone https://github.com/NudeNyang/Sentory.git
cd Sentory
dotnet build .\Sentory.sln --configuration Release
dotnet test .\Sentory.sln --configuration Release
.\scripts\Publish-Release.ps1 -Version 1.1.2
```

배포 스크립트는 Windows x64·ARM64 설치형과 포터블 패키지, SHA-256 확인값,
해당 버전의 소스 ZIP, `release-manifest.json`을 `artifacts` 폴더에 만듭니다.
구현과 배포 절차는
[개발 문서](./docs/development.md)와
[공개 배포 문서](./docs/05-release-and-distribution.md)에서 확인할 수 있습니다.
그 밖의 기록은 [문서 모음](./docs/README.md)에 정리되어 있습니다.

## 라이선스

Sentory는 **GNU General Public License v3.0 only**로 배포합니다. 라이선스 조건을
지키는 범위에서 사용, 연구, 수정, 재배포할 수 있으며 상업적 이용도 가능합니다.
수정한 버전이나 실행 파일을 배포할 때는 GPL이 요구하는 방식으로 해당 소스 코드와
라이선스 고지를 함께 제공해야 합니다. 제3자 구성 요소는 각 구성 요소의 라이선스를
따릅니다.

Copyright © 2026 NudeNyang

- [GNU GPL v3 전체 내용](./LICENSE.txt)
- [개인정보 및 로컬 데이터](./docs/privacy.md)
- [제3자 구성 요소](./distribution/THIRD-PARTY-NOTICES.txt)
- [변경 기록](./CHANGELOG.md)
- [지원 정책](./docs/support.md)
